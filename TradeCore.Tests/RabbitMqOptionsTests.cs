using Microsoft.Extensions.Configuration;
using TradeCore.Messaging;

namespace TradeCore.Tests;

public sealed class RabbitMqOptionsTests
{
    [Fact]
    public void Bind_reads_all_rabbitmq_settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Enabled"] = "true",
                ["RabbitMq:HostName"] = "rabbitmq.local",
                ["RabbitMq:Port"] = "5678",
                ["RabbitMq:UserName"] = "tradecore",
                ["RabbitMq:Password"] = "not-logged",
                ["RabbitMq:OrdersQueue"] = "tradecore-orders"
            })
            .Build();

        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>();

        Assert.NotNull(options);
        Assert.True(options.Enabled);
        Assert.Equal("rabbitmq.local", options.HostName);
        Assert.Equal(5678, options.Port);
        Assert.Equal("tradecore", options.UserName);
        Assert.Equal("not-logged", options.Password);
        Assert.Equal("tradecore-orders", options.OrdersQueue);
    }

    [Theory]
    [InlineData("", 5672, "guest", "guest", "orders")]
    [InlineData("localhost", 0, "guest", "guest", "orders")]
    [InlineData("localhost", 5672, "", "guest", "orders")]
    [InlineData("localhost", 5672, "guest", "", "orders")]
    [InlineData("localhost", 5672, "guest", "guest", "")]
    public void Validator_rejects_incomplete_enabled_configuration(
        string hostName,
        int port,
        string userName,
        string password,
        string ordersQueue)
    {
        var result = new RabbitMqOptionsValidator().Validate(null, new RabbitMqOptions
        {
            Enabled = true,
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            OrdersQueue = ordersQueue
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validator_allows_disabled_rabbitmq_without_connection_settings()
    {
        var result = new RabbitMqOptionsValidator().Validate(null, new RabbitMqOptions { Enabled = false });

        Assert.True(result.Succeeded);
    }
}
