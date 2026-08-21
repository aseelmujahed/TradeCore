using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradeCore.Messaging;
using TradeCore.TradingEngine;

namespace TradeCore.Tests;

[Collection(nameof(RabbitMqOrderConsumerReconnectCollection))]
public sealed class RabbitMqOrderConsumerReconnectTests
{
    [Fact]
    public async Task Starts_when_broker_is_unavailable_then_connects_and_processes_a_delivery_after_recovery()
    {
        var recoveredSession = new FakeSession();
        var factory = new FakeSessionFactory(
            _ => Task.FromException<IRabbitMqOrderConsumerSession>(new InvalidOperationException("Broker is unavailable.")),
            processDelivery => recoveredSession.StartAsync(processDelivery));
        var handler = new RecordingHandler();
        var transport = new RecordingTransport();
        var consumer = CreateConsumer(factory, handler, transport);

        await consumer.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => factory.CreateCalls >= 2);

        await recoveredSession.DeliverAsync(Delivery());

        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, transport.Acknowledgements);
        await consumer.StopAsync(CancellationToken.None);
        Assert.True(recoveredSession.Disposed);
    }

    [Fact]
    public async Task Reconnects_after_an_established_session_shuts_down_and_resumes_consuming()
    {
        var initialSession = new FakeSession();
        var recoveredSession = new FakeSession();
        var factory = new FakeSessionFactory(
            processDelivery => initialSession.StartAsync(processDelivery),
            processDelivery => recoveredSession.StartAsync(processDelivery));
        var handler = new RecordingHandler();
        var transport = new RecordingTransport();
        var consumer = CreateConsumer(factory, handler, transport);

        await consumer.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => factory.CreateCalls == 1);
        initialSession.SignalShutdown();
        await WaitUntilAsync(() => factory.CreateCalls >= 2);

        await recoveredSession.DeliverAsync(Delivery());

        Assert.True(initialSession.Disposed);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, transport.Acknowledgements);
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_cancels_a_pending_reconnect_delay_without_waiting_for_the_retry_interval()
    {
        var factory = new FakeSessionFactory(
            _ => Task.FromException<IRabbitMqOrderConsumerSession>(new InvalidOperationException("Broker is unavailable.")));
        var consumer = CreateConsumer(factory, new RecordingHandler(), new RecordingTransport());

        await consumer.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => factory.CreateCalls == 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await consumer.StopAsync(timeout.Token);
    }

    private static RabbitMqOrderConsumer CreateConsumer(
        IRabbitMqOrderConsumerSessionFactory factory,
        RecordingHandler handler,
        RecordingTransport transport) =>
        new(
            Options.Create(new RabbitMqOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "guest",
                Password = "guest",
                OrdersQueue = "orders",
                RetryDelayMilliseconds = 1
            }),
            new ReliableOrderDeliveryProcessor(
                handler,
                transport,
                Options.Create(new RabbitMqOptions
                {
                    HostName = "localhost",
                    Port = 5672,
                    UserName = "guest",
                    Password = "guest",
                    OrdersQueue = "orders",
                    RetryDelayMilliseconds = 1
                }),
                NullLogger<ReliableOrderDeliveryProcessor>.Instance),
            factory,
            NullLogger<RabbitMqOrderConsumer>.Instance);

    private static OrderDelivery Delivery() => new(1, "{\"orderId\":\"b48c0776-6b4e-4d7f-8dfd-44665e5c0d29\"}"u8.ToArray(), 1, Guid.Parse("b48c0776-6b4e-4d7f-8dfd-44665e5c0d29"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("Condition was not met before the test timeout.");
            await Task.Delay(20);
        }
    }

    private sealed class FakeSessionFactory(params Func<Func<OrderDelivery, CancellationToken, Task>, Task<IRabbitMqOrderConsumerSession>>[] attempts)
        : IRabbitMqOrderConsumerSessionFactory
    {
        private readonly Queue<Func<Func<OrderDelivery, CancellationToken, Task>, Task<IRabbitMqOrderConsumerSession>>> _attempts = new(attempts);

        public int CreateCalls { get; private set; }

        public Task<IRabbitMqOrderConsumerSession> CreateAsync(
            RabbitMqOptions options,
            Func<OrderDelivery, CancellationToken, Task> processDelivery,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return _attempts.Count > 0
                ? _attempts.Dequeue()(processDelivery)
                : Task.FromException<IRabbitMqOrderConsumerSession>(new InvalidOperationException("Unexpected reconnect attempt."));
        }
    }

    private sealed class FakeSession : IRabbitMqOrderConsumerSession
    {
        private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<OrderDelivery, CancellationToken, Task>? _processDelivery;

        public bool Disposed { get; private set; }

        public Task WaitForShutdownAsync(CancellationToken cancellationToken) => _shutdown.Task.WaitAsync(cancellationToken);

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            _shutdown.TrySetResult();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _shutdown.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public void SignalShutdown() => _shutdown.TrySetResult();

        public Task DeliverAsync(OrderDelivery delivery) => (_processDelivery ?? throw new InvalidOperationException("Session was not initialized."))(delivery, CancellationToken.None);

        public Task<IRabbitMqOrderConsumerSession> StartAsync(Func<OrderDelivery, CancellationToken, Task> processDelivery)
        {
            _processDelivery = processDelivery;
            return Task.FromResult<IRabbitMqOrderConsumerSession>(this);
        }
    }

    private sealed class RecordingHandler : IOrderMessageHandler
    {
        public int Calls { get; private set; }

        public Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTransport : IOrderMessageDeliveryTransport
    {
        public int Acknowledgements { get; private set; }

        public Task AcknowledgeAsync(OrderDelivery delivery, CancellationToken cancellationToken)
        {
            Acknowledgements++;
            return Task.CompletedTask;
        }

        public Task ScheduleRetryAsync(OrderDelivery delivery, int nextAttempt, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeadLetterAsync(OrderDelivery delivery, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

[CollectionDefinition(nameof(RabbitMqOrderConsumerReconnectCollection), DisableParallelization = true)]
public sealed class RabbitMqOrderConsumerReconnectCollection
{
}
