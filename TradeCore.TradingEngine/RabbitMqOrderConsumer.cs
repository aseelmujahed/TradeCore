using Microsoft.Extensions.Options;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

public sealed class RabbitMqOrderConsumer(
    IOptions<RabbitMqOptions> options,
    ReliableOrderDeliveryProcessor deliveryProcessor,
    IRabbitMqOrderConsumerSessionFactory sessionFactory,
    ILogger<RabbitMqOrderConsumer> logger) : BackgroundService
{
    private readonly object _sessionLock = new();
    private IRabbitMqOrderConsumerSession? _session;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("RabbitMQ order consumption is disabled.");
            return;
        }

        var settings = options.Value;
        var reconnectDelay = TimeSpan.FromMilliseconds(Math.Clamp(settings.RetryDelayMilliseconds, 1_000, 30_000));
        while (!stoppingToken.IsCancellationRequested)
        {
            IRabbitMqOrderConsumerSession? session = null;
            try
            {
                session = await sessionFactory.CreateAsync(settings, ProcessDeliveryAsync, stoppingToken);
                SetSession(session);
                logger.LogInformation("Consuming submitted orders from RabbitMQ queue {Queue}.", settings.OrdersQueue);
                await session.WaitForShutdownAsync(stoppingToken);
                if (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(
                        "RabbitMQ order consumer connection to {Host}:{Port} closed; reconnecting in {ReconnectDelay}.",
                        settings.HostName, settings.Port, reconnectDelay);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "RabbitMQ order consumer connection to {Host}:{Port} failed; retrying in {ReconnectDelay}.",
                    settings.HostName, settings.Port, reconnectDelay);
            }
            finally
            {
                ClearSession(session);
                if (session is not null)
                {
                    try { await session.DisposeAsync(); }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogDebug(exception, "Failed to dispose a stale RabbitMQ order consumer session.");
                    }
                }
            }

            try { await Task.Delay(reconnectDelay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var session = GetSession();
        if (session is not null)
        {
            try { await session.CancelAsync(cancellationToken); }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(exception, "Failed to cancel the RabbitMQ order consumer during shutdown.");
            }
        }
        await base.StopAsync(cancellationToken);
    }
    private async Task ProcessDeliveryAsync(OrderDelivery orderDelivery, CancellationToken stoppingToken)
    {
        try
        {
            await deliveryProcessor.ProcessAsync(orderDelivery, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // No ACK here: a failed confirmed publish must not turn into message loss.
            logger.LogError(exception, "Delivery {DeliveryTag} was not acknowledged because failure handling did not complete.", orderDelivery.DeliveryTag);
        }
    }

    private void SetSession(IRabbitMqOrderConsumerSession session)
    {
        lock (_sessionLock) _session = session;
    }

    private void ClearSession(IRabbitMqOrderConsumerSession? session)
    {
        lock (_sessionLock)
        {
            if (ReferenceEquals(_session, session)) _session = null;
        }
    }

    private IRabbitMqOrderConsumerSession? GetSession()
    {
        lock (_sessionLock) return _session;
    }
}
