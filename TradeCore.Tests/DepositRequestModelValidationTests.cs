using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TradeCore.Api.DTOs.Accounts;

namespace TradeCore.Tests;

public sealed class DepositRequestModelValidationTests
{
    [Theory]
    [InlineData(10_000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void Validate_WhenAmountIsProvided_UsesAspNetCoreModelValidation(decimal amount, bool expectedIsValid)
    {
        var modelState = Validate(new DepositRequest { Amount = amount });

        Assert.Equal(expectedIsValid, modelState.IsValid);
        if (!expectedIsValid)
        {
            Assert.Contains(modelState, entry =>
                entry.Key == nameof(DepositRequest.Amount) &&
                entry.Value?.Errors.Any(error => error.ErrorMessage == "Amount must be greater than 0.") == true);
        }
    }

    private static ModelStateDictionary Validate(DepositRequest request)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddControllers()
            .Services
            .BuildServiceProvider();
        var modelState = new ModelStateDictionary();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), modelState);
        var validator = services.GetRequiredService<IObjectModelValidator>();

        validator.Validate(actionContext, validationState: null, prefix: string.Empty, model: request);

        return modelState;
    }
}
