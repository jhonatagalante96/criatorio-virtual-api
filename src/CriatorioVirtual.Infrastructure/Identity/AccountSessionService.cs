using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Identity;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class AccountSessionService(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) : IAccountSessionService
{
    public async Task<AccountLoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrEmpty(password);
        cancellationToken.ThrowIfCancellationRequested();

        var existingUser = await userManager.FindByEmailAsync(email.Trim());
        if (existingUser is { EmailConfirmed: false } &&
            await userManager.CheckPasswordAsync(existingUser, password))
        {
            return AccountLoginResult.EmailUnconfirmed();
        }

        var result = await signInManager.PasswordSignInAsync(
            email.Trim(),
            password,
            isPersistent: false,
            lockoutOnFailure: true);

        return result.Succeeded
            ? AccountLoginResult.Succeeded()
            : AccountLoginResult.Invalid();
    }

    public async Task<AccountSession?> GetCurrentAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null || string.IsNullOrWhiteSpace(user.Email)
            ? null
            : new AccountSession(user.Id, user.Email, user.EmailConfirmed);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return signInManager.SignOutAsync();
    }
}
