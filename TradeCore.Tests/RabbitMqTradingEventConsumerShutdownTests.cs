using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradeCore.Api.Messaging;
using TradeCore.Messaging;

namespace TradeCore.Tests;

[Collection(nameof(RealRabbitMqCollection))]
public sealed class RabbitMqTradingEventConsumerShutdownTests(RabbitMqIntegrationFixture fixture)
{
    [Fact]
    public async Task StopAsync_is_idempotent_while_consumer_startup_is_publishing_its_state()
    {
        // StartAsync launches ExecuteAsync in the background. Repeating the immediate, concurrent
        // shutdown makes StopAsync race with connection/channel/tag publication as it does in test cleanup.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var services = new ServiceCollection().BuildServiceProvider();
            using var consumer = CreateConsumer(services);

            await consumer.StartAsync(CancellationToken.None);
            var stops = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() => consumer.StopAsync(CancellationToken.None)));

            await Task.WhenAll(stops);
        }
    }

    private RabbitMqTradingEventConsumer CreateConsumer(IServiceProvider services) =>
        new(
            Options.Create(new RabbitMqOptions
            {
                Enabled = true,
                HostName = fixture.Settings.HostName,
                Port = fixture.Settings.Port,
                UserName = fixture.Settings.UserName,
                Password = fixture.Settings.Password,
                TradeExecutedQueue = $"{fixture.Settings.TradeExecutedQueue}-shutdown",
                StockPriceUpdatedQueue = $"{fixture.Settings.StockPriceUpdatedQueue}-shutdown",
                RetryDelayMilliseconds = 100,
                MaxProcessingAttempts = 3
            }),
            services.GetRequiredService<IServiceScopeFactory>(),
            new TradingEventDeduplicator(),
            NullLogger<RabbitMqTradingEventConsumer>.Instance);
}
