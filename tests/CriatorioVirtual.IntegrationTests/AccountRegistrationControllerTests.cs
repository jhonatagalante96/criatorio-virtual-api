using CriatorioVirtual.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class AccountRegistrationControllerTests
{
    [Fact]
    public async Task Register_RejectsMissingPasswordConfirmation()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var controller = new AccountRegistrationController(serviceProvider);

        var result = await controller.RegisterAsync(
            new RegisterAccountRequest("user@example.com", "StrongPassword!123", null),
            CancellationToken.None);

        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(response.Value);
        Assert.Equal(
            "Password confirmation is required.",
            Assert.Single(problem.Errors["confirmPassword"]));
    }

    [Fact]
    public async Task Register_RejectsMismatchedPasswordConfirmation()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var controller = new AccountRegistrationController(serviceProvider);

        var result = await controller.RegisterAsync(
            new RegisterAccountRequest(
                "user@example.com",
                "StrongPassword!123",
                "DifferentPassword!123"),
            CancellationToken.None);

        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(response.Value);
        Assert.Equal(
            "Password confirmation does not match the password.",
            Assert.Single(problem.Errors["confirmPassword"]));
    }
}
