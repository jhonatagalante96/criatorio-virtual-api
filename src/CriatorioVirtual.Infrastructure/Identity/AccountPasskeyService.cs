using System.Text.Json;
using CriatorioVirtual.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ApplicationPasskeyCreationOptionsResult = CriatorioVirtual.Application.Identity.PasskeyCreationOptionsResult;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class AccountPasskeyService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IAccountPasskeyService
{
    private const int MaximumPasskeyNameLength = 100;

    public async Task<ApplicationPasskeyCreationOptionsResult> CreateRegistrationOptionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserAsync(userId);
        if (user is null)
        {
            return ApplicationPasskeyCreationOptionsResult.Failure(PasskeyOperationStatus.UserNotFound);
        }

        if (!userManager.SupportsUserPasskey)
        {
            return ApplicationPasskeyCreationOptionsResult.Failure(PasskeyOperationStatus.Unsupported);
        }

        var userName = user.UserName ?? user.Email ?? user.Id.ToString("D");
        var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString("D"),
            Name = userName,
            DisplayName = user.Email ?? userName
        });

        return ApplicationPasskeyCreationOptionsResult.Succeeded(optionsJson);
    }

    public async Task<PasskeyOperationResult> RegisterAsync(
        Guid userId,
        string? credentialJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialJson))
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.InvalidCredential);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserAsync(userId);
        if (user is null)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.UserNotFound);
        }

        if (!userManager.SupportsUserPasskey)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.Unsupported);
        }

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signInManager.PerformPasskeyAttestationAsync(credentialJson);
        }
        catch (Exception exception) when (exception is PasskeyException or JsonException or InvalidOperationException)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.InvalidCredential);
        }

        if (!attestation.Succeeded ||
            attestation.Passkey is null ||
            attestation.UserEntity is null ||
            !string.Equals(attestation.UserEntity.Id, user.Id.ToString("D"), StringComparison.Ordinal))
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.InvalidCredential);
        }

        var existingOwner = await userManager.FindByPasskeyIdAsync(attestation.Passkey.CredentialId);
        if (existingOwner is not null && existingOwner.Id != user.Id)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.Conflict);
        }

        try
        {
            var result = await userManager.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
            return result.Succeeded
                ? PasskeyOperationResult.Succeeded()
                : PasskeyOperationResult.Failure(PasskeyOperationStatus.Failed);
        }
        catch (DbUpdateException)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.Conflict);
        }
    }

    public async Task<PasskeyListResult> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserAsync(userId);
        if (user is null)
        {
            return PasskeyListResult.Failure(PasskeyOperationStatus.UserNotFound);
        }

        if (!userManager.SupportsUserPasskey)
        {
            return PasskeyListResult.Failure(PasskeyOperationStatus.Unsupported);
        }

        var passkeys = await userManager.GetPasskeysAsync(user);
        return PasskeyListResult.Succeeded(passkeys.Select(MapPasskey));
    }

    public async Task<PasskeyOperationResult> RenameAsync(
        Guid userId,
        string? credentialId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeCredentialId(credentialId, out var decodedCredentialId))
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.InvalidCredentialId);
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > MaximumPasskeyNameLength)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.InvalidName);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserAsync(userId);
        if (user is null)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.UserNotFound);
        }

        var passkey = await userManager.GetPasskeyAsync(user, decodedCredentialId);
        if (passkey is null)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.PasskeyNotFound);
        }

        passkey.Name = normalizedName;
        var result = await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        return result.Succeeded
            ? PasskeyOperationResult.Succeeded()
            : PasskeyOperationResult.Failure(PasskeyOperationStatus.Failed);
    }

    public async Task<PasskeyOperationResult> RemoveAsync(
        Guid userId,
        string? credentialId,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeCredentialId(credentialId, out var decodedCredentialId))
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.InvalidCredentialId);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await FindUserAsync(userId);
        if (user is null)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.UserNotFound);
        }

        var passkey = await userManager.GetPasskeyAsync(user, decodedCredentialId);
        if (passkey is null)
        {
            return PasskeyOperationResult.Failure(PasskeyOperationStatus.PasskeyNotFound);
        }

        var result = await userManager.RemovePasskeyAsync(user, decodedCredentialId);
        return result.Succeeded
            ? PasskeyOperationResult.Succeeded()
            : PasskeyOperationResult.Failure(PasskeyOperationStatus.Failed);
    }

    private async Task<ApplicationUser?> FindUserAsync(Guid userId) =>
        userId == Guid.Empty ? null : await userManager.FindByIdAsync(userId.ToString("D"));

    private static AccountPasskey MapPasskey(UserPasskeyInfo passkey) =>
        new(
            WebEncoders.Base64UrlEncode(passkey.CredentialId),
            passkey.Name,
            passkey.CreatedAt,
            passkey.Transports ?? Array.Empty<string>(),
            passkey.IsUserVerified,
            passkey.IsBackupEligible,
            passkey.IsBackedUp);

    private static bool TryDecodeCredentialId(string? credentialId, out byte[] decodedCredentialId)
    {
        decodedCredentialId = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            return false;
        }

        try
        {
            decodedCredentialId = WebEncoders.Base64UrlDecode(credentialId);
            return decodedCredentialId.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
