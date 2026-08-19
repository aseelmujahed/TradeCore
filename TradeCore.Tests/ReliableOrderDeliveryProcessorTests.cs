using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCore.Messaging;
using TradeCore.TradingEngine;

namespace TradeCore.Tests;

public sealed class ReliableOrderDeliveryProcessorTests
{
    [Fact]
    public async Task Success_acknowledges_without_retry_or_dead_letter()
    {
        var handler = new ScriptedHandler();
        var transport = new RecordingTransport();
        await CreateProcessor(handler, transport).ProcessAsync(Delivery(), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(["ack"], transport.Actions);
    }

    [Fact]
    public async Task Failure_before_max_attempts_schedules_retry_before_acknowledgement()
    {
        var handler = new ScriptedHandler(new InvalidOperationException("settlement failed"));
        var transport = new RecordingTransport();
        await CreateProcessor(handler, transport).ProcessAsync(Delivery(attempt: 1), CancellationToken.None);

        Assert.Equal(["retry:2", "ack"], transport.Actions);
    }

    [Fact]
    public async Task Terminal_failure_dead_letters_before_acknowledgement()
    {
        var handler = new ScriptedHandler(new InvalidOperationException("settlement failed"));
        var transport = new RecordingTransport();
        await CreateProcessor(handler, transport).ProcessAsync(Delivery(attempt: 3), CancellationToken.None);

        Assert.Equal(["dlq:InvalidOperationException", "ack"], transport.Actions);
    }

    [Fact]
    public async Task Retry_then_success_acknowledges_the_second_attempt_without_dead_lettering()
    {
        var handler = new ScriptedHandler(new InvalidOperationException("temporary failure"));
        var transport = new RecordingTransport();
        var processor = CreateProcessor(handler, transport);

        await processor.ProcessAsync(Delivery(attempt: 1), CancellationToken.None);
        await processor.ProcessAsync(Delivery(attempt: 2), CancellationToken.None);

        Assert.Equal(["retry:2", "ack", "ack"], transport.Actions);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Malformed_message_dead_letters_immediately()
    {
        var handler = new ScriptedHandler(new InvalidOrderMessageException("bad json"));
        var transport = new RecordingTransport();
        await CreateProcessor(handler, transport).ProcessAsync(Delivery(), CancellationToken.None);

        Assert.Equal(["dlq:InvalidOrderMessageException", "ack"], transport.Actions);
    }

    [Fact]
    public async Task Missing_persisted_order_follows_bounded_retry_policy()
    {
        var handler = new ScriptedHandler(new PersistedOrderNotFoundException(Guid.NewGuid()));
        var transport = new RecordingTransport();
        await CreateProcessor(handler, transport).ProcessAsync(Delivery(attempt: 3), CancellationToken.None);

        Assert.Equal(["dlq:PersistedOrderNotFoundException", "ack"], transport.Actions);
    }

    [Fact]
    public async Task Failed_retry_publication_does_not_acknowledge_original_delivery()
    {
        var handler = new ScriptedHandler(new InvalidOperationException("database unavailable"));
        var transport = new RecordingTransport { RetryException = new InvalidOperationException("broker unavailable") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProcessor(handler, transport).ProcessAsync(Delivery(), CancellationToken.None));

        Assert.Equal(["retry:2"], transport.Actions);
    }

    [Fact]
    public async Task Cancellation_stops_before_retry_or_acknowledgement()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new ScriptedHandler(new OperationCanceledException(cancellation.Token));
        var transport = new RecordingTransport();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProcessor(handler, transport).ProcessAsync(Delivery(), cancellation.Token));

        Assert.Empty(transport.Actions);
    }

    [Fact]
    public void Attempt_header_defaults_to_one_and_reads_retry_value() 
    {
        Assert.Equal(1, RabbitMqOrderDeliveryTransport.GetAttempt(null));
        Assert.Equal(3, RabbitMqOrderDeliveryTransport.GetAttempt(new Dictionary<string, object?> { [RabbitMqOrderDeliveryTransport.AttemptHeader] = 3 }));
    }

    private static ReliableOrderDeliveryProcessor CreateProcessor(IOrderMessageHandler handler, IOrderMessageDeliveryTransport transport) =>
        new(handler, transport, Options.Create(new RabbitMqOptions
        {
            HostName = "localhost", UserName = "guest", Password = "guest", OrdersQueue = "orders", MaxProcessingAttempts = 3, RetryDelayMilliseconds = 1
        }), NullLogger<ReliableOrderDeliveryProcessor>.Instance);

    private static OrderDelivery Delivery(int attempt = 1) => new(7, "{\"orderId\":\"b48c0776-6b4e-4d7f-8dfd-44665e5c0d29\"}"u8.ToArray(), attempt, Guid.Parse("b48c0776-6b4e-4d7f-8dfd-44665e5c0d29"));

    private sealed class ScriptedHandler(params Exception[] exceptions) : IOrderMessageHandler
    {
        private readonly Queue<Exception> _exceptions = new(exceptions);
        public int Calls { get; private set; }
        public Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            Calls++;
            if (_exceptions.TryDequeue(out var exception)) return Task.FromException(exception);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTransport : IOrderMessageDeliveryTransport
    {
        public List<string> Actions { get; } = [];
        public Exception? RetryException { get; init; }
        public Task AcknowledgeAsync(OrderDelivery delivery, CancellationToken cancellationToken) { Actions.Add("ack"); return Task.CompletedTask; }
        public Task ScheduleRetryAsync(OrderDelivery delivery, int nextAttempt, CancellationToken cancellationToken)
        {
            Actions.Add($"retry:{nextAttempt}");
            return RetryException is null ? Task.CompletedTask : Task.FromException(RetryException);
        }
        public Task DeadLetterAsync(OrderDelivery delivery, Exception exception, CancellationToken cancellationToken) { Actions.Add($"dlq:{exception.GetType().Name}"); return Task.CompletedTask; }
    }
}
