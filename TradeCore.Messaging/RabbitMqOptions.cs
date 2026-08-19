using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace TradeCore.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public bool Enabled { get; init; } = true;

    public string HostName { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 5672;

    public string UserName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string OrdersQueue { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int MaxProcessingAttempts { get; init; } = 3;

    [Range(1, int.MaxValue)]
    public int RetryDelayMilliseconds { get; init; } = 5_000;
}

public sealed class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.HostName)) failures.Add("RabbitMq:HostName is required.");
        if (options.Port is < 1 or > 65535) failures.Add("RabbitMq:Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(options.UserName)) failures.Add("RabbitMq:UserName is required.");
        if (string.IsNullOrWhiteSpace(options.Password)) failures.Add("RabbitMq:Password is required.");
        if (string.IsNullOrWhiteSpace(options.OrdersQueue)) failures.Add("RabbitMq:OrdersQueue is required.");
        if (options.MaxProcessingAttempts < 1) failures.Add("RabbitMq:MaxProcessingAttempts must be at least 1.");
        if (options.RetryDelayMilliseconds < 1) failures.Add("RabbitMq:RetryDelayMilliseconds must be at least 1.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
