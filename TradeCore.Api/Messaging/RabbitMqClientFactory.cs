using RabbitMQ.Client;

namespace TradeCore.Api.Messaging;

public sealed class RabbitMqClientFactory : IRabbitMqClientFactory
{
    public async Task<IRabbitMqSession> CreateAsync(
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password
        };

        var connection = await factory.CreateConnectionAsync(cancellationToken);
        try
        {
            var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken);
            return new RabbitMqSession(connection, channel);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class RabbitMqSession(IConnection connection, IChannel channel) : IRabbitMqSession
    {
        public Task DeclareQueueAsync(RabbitMqQueueDeclaration declaration, CancellationToken cancellationToken) =>
            channel.QueueDeclareAsync(
                queue: declaration.Name,
                durable: declaration.Durable,
                exclusive: declaration.Exclusive,
                autoDelete: declaration.AutoDelete,
                arguments: null,
                cancellationToken: cancellationToken);

        public async Task PublishAsync(RabbitMqPublishedMessage message, CancellationToken cancellationToken) =>
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: message.RoutingKey,
                mandatory: true,
                basicProperties: new BasicProperties
                {
                    ContentType = message.ContentType,
                    Persistent = message.Persistent,
                    MessageId = message.MessageId
                },
                body: message.Body,
                cancellationToken: cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await channel.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
