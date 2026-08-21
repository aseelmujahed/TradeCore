using Microsoft.Extensions.Options;
using TradeCore.Messaging;

namespace TradeCore.Api.Messaging;

public sealed class RabbitMqInitializationService(
    IRabbitMqConnectionService connectionService,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqInitializationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("RabbitMQ initialization is disabled.");
            return;
        }

        try
        {
            await connectionService.InitializeAsync(cancellationToken);
            logger.LogInformation("RabbitMQ connection established and orders queue declared.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "RabbitMQ initialization failed ({ExceptionType}); the API outbox publisher will retry when it has pending messages.",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
