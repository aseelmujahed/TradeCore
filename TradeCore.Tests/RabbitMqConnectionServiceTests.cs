using Microsoft.Extensions.Options;
using TradeCore.Api.Messaging;
using TradeCore.Messaging;

namespace TradeCore.Tests;

public sealed class RabbitMqConnectionServiceTests
{
    [Fact]
    public async Task Initialize_declares_configured_orders_queue_with_durable_settings()
    {
        var session = new FakeSession();
        var factory = new FakeFactory(session);
        await using var service = CreateService(factory, "tradecore-orders");

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(1, factory.CreateCalls);
        var declaration = Assert.Single(session.Declarations);
        Assert.Equal("tradecore-orders", declaration.Name);
        Assert.True(declaration.Durable);
        Assert.False(declaration.Exclusive);
        Assert.False(declaration.AutoDelete);
    }

    [Fact]
    public async Task Initialize_reuses_its_existing_connection_and_channel()
    {
        var session = new FakeSession();
        var factory = new FakeFactory(session);
        await using var service = CreateService(factory, "orders");

        await service.InitializeAsync(CancellationToken.None);
        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(1, factory.CreateCalls);
        Assert.Single(session.Declarations);
    }

    [Fact]
    public async Task Initialize_disposes_new_session_when_queue_declaration_fails()
    {
        var session = new FakeSession { DeclarationException = new InvalidOperationException("Broker rejected queue") };
        await using var service = CreateService(new FakeFactory(session), "orders");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeAsync(CancellationToken.None));

        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Dispose_disposes_the_shared_session()
    {
        var session = new FakeSession();
        var service = CreateService(new FakeFactory(session), "orders");
        await service.InitializeAsync(CancellationToken.None);

        await service.DisposeAsync();

        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Publish_uses_the_initialized_shared_session_and_propagates_confirmation_failure()
    {
        var session = new FakeSession { PublishException = new InvalidOperationException("Publish was nacked") };
        await using var service = CreateService(new FakeFactory(session), "orders");
        var message = new RabbitMqPublishedMessage("orders", new byte[] { 1, 2 }, "application/json", true, Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(message, CancellationToken.None));

        Assert.Single(session.Declarations);
        Assert.Equal(message, Assert.Single(session.PublishedMessages));
    }

    private static RabbitMqConnectionService CreateService(FakeFactory factory, string queueName) =>
        new(
            Options.Create(new RabbitMqOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "guest",
                Password = "guest",
                OrdersQueue = queueName
            }),
            factory);

    private sealed class FakeFactory(FakeSession session) : IRabbitMqClientFactory
    {
        public int CreateCalls { get; private set; }

        public Task<IRabbitMqSession> CreateAsync(RabbitMqOptions options, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult<IRabbitMqSession>(session);
        }
    }

    private sealed class FakeSession : IRabbitMqSession
    {
        public List<RabbitMqQueueDeclaration> Declarations { get; } = [];
        public List<RabbitMqPublishedMessage> PublishedMessages { get; } = [];
        public Exception? DeclarationException { get; init; }
        public Exception? PublishException { get; init; }
        public bool Disposed { get; private set; }

        public Task DeclareQueueAsync(RabbitMqQueueDeclaration declaration, CancellationToken cancellationToken)
        {
            Declarations.Add(declaration);
            return DeclarationException is null ? Task.CompletedTask : Task.FromException(DeclarationException);
        }

        public Task PublishAsync(RabbitMqPublishedMessage message, CancellationToken cancellationToken)
        {
            PublishedMessages.Add(message);
            return PublishException is null ? Task.CompletedTask : Task.FromException(PublishException);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
