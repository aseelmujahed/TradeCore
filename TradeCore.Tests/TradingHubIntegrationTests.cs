using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace TradeCore.Tests;

public sealed class TradingHubIntegrationTests : IClassFixture<TradeCoreApiFactory>
{
    private readonly TradeCoreApiFactory _factory;

    public TradingHubIntegrationTests(TradeCoreApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ApplicationStarts_WithSignalRAndExistingStocksEndpointAvailable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/stocks");

        response.EnsureSuccessStatusCode();
        var stocks = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, stocks.ValueKind);
        Assert.Equal(4, stocks.GetArrayLength());
    }

    [Fact]
    public async Task TradingHub_NegotiateEndpoint_IsMappedAndReturnsSignalRConnectionDetails()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/hubs/trading/negotiate?negotiateVersion=1", null);

        response.EnsureSuccessStatusCode();
        var negotiation = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(negotiation.GetProperty("connectionId").GetString()));
        Assert.Contains(
            negotiation.GetProperty("availableTransports").EnumerateArray(),
            transport => transport.GetProperty("transport").GetString() == "LongPolling");
    }

    [Fact]
    public async Task TradingHub_CanEstablishLongPollingSignalRConnection()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/trading"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();

        Assert.Equal(HubConnectionState.Connected, connection.State);
        await connection.StopAsync();
    }

    [Fact]
    public async Task TradingHub_NonSignalRGetRequest_ReturnsBadRequestWithoutCrashingApplication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/hubs/trading");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
