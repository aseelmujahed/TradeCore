using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCore.Api.ExceptionHandling;

namespace TradeCore.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData("Insufficient account balance for this trade.")]
    [InlineData("Insufficient portfolio shares for this trade.")]
    public async Task TryHandleAsync_WhenBusinessRuleFails_ReturnsBadRequestWithExceptionMessage(string message)
    {
        var httpContext = CreateHttpContext();
        var handler = CreateHandler();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException(message),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", httpContext.Response.ContentType);
        using var response = JsonDocument.Parse(((MemoryStream)httpContext.Response.Body).ToArray());
        Assert.Equal("Invalid operation.", response.RootElement.GetProperty("title").GetString());
        Assert.Equal(StatusCodes.Status400BadRequest, response.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(message, response.RootElement.GetProperty("detail").GetString());
        Assert.Equal("/api/orders", response.RootElement.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_WhenExceptionIsUnexpected_ReturnsGenericInternalServerError()
    {
        var httpContext = CreateHttpContext();
        var handler = CreateHandler();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new Exception("Sensitive implementation detail"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        var responseBody = System.Text.Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray());
        using var response = JsonDocument.Parse(responseBody);
        Assert.Equal("An unexpected error occurred.", response.RootElement.GetProperty("title").GetString());
        Assert.Equal(StatusCodes.Status500InternalServerError, response.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An internal server error occurred.", response.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("Sensitive implementation detail", responseBody);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/orders";
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static GlobalExceptionHandler CreateHandler() => new(NullLogger<GlobalExceptionHandler>.Instance);
}
