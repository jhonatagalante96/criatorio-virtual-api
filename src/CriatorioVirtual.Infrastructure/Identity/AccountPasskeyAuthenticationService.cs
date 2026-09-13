using System.Text.Json;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Identity;
using ApplicationPasskeyRequestOptionsResult = CriatorioVirtual.Application.Identity.PasskeyRequestOptionsResult;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class AccountPasskeyAuthenticationService(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : IAccountPasskeyAuthenticationService
{
    public async Task<ApplicationPasskeyRequestOptionsResult> CreateRequestOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!userManager.SupportsUserPasskey)
        {
            return ApplicationPasskeyRequestOptionsResult.Failure(PasskeyAuthenticationStatus.Unsupported);
        }

        var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(null);
        return ApplicationPasskeyRequestOptionsResult.Succeeded(optionsJson);
    }

    public async Task<PasskeyAuthenticationResult> AuthenticateAsync(
        string? credentialJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialJson))
        {
            return PasskeyAuthenticationResult.Failure(PasskeyAuthenticationStatus.Invalid);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!userManager.SupportsUserPasskey)
        {
            return PasskeyAuthenticationResult.Failure(PasskeyAuthenticationStatus.Unsupported);
        }

        try
        {
            var result = await signInManager.PasskeySignInAsync(credentialJson);
            if (result.Succeeded)
            {
                return PasskeyAuthenticationResult.Succeeded();
            }

            return result.IsNotAllowed
                ? PasskeyAuthenticationResult.Failure(PasskeyAuthenticationStatus.NotAllowed)
                : PasskeyAuthenticationResult.Failure(PasskeyAuthenticationStatus.Invalid);
        }
        catch (Exception exception) when (exception is PasskeyException or JsonException or InvalidOperationException)
        {
            return PasskeyAuthenticationResult.Failure(PasskeyAuthenticationStatus.Invalid);
        }
    }
}
