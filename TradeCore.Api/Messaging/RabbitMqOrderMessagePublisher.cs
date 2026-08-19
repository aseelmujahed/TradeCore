using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TradeCore.Api.Messaging;

public sealed class RabbitMqOrderMessagePublisher(
    IRabbitMqConnectionService connectionService,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqOrderMessagePublisher> logger) : IOrderMessagePublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(OrderSubmittedMessage message, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        var publishedMessage = new RabbitMqPublishedMessage(
            RoutingKey: options.Value.OrdersQueue,
            Body: body,
            ContentType: "application/json",
            Persistent: true,
            MessageId: message.OrderId.ToString());

        logger.LogInformation("Publishing submitted order {OrderId} to RabbitMQ.", message.OrderId);
        try
        {
            await connectionService.PublishAsync(publishedMessage, cancellationToken);
            logger.LogInformation("RabbitMQ confirmed submitted order {OrderId}.", message.OrderId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "RabbitMQ did not confirm submitted order {OrderId}.",
                message.OrderId);
            throw;
        }
    }
}
