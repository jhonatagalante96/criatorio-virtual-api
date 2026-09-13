using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class AccountPasskeyServiceTests
{
    [Fact]
    public async Task RegisterAsync_AddsPasskeyThroughIdentityAndRejectsCredentialOwnedByAnotherUser()
    {
        var owner = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "owner@example.com",
            Email = "owner@example.com"
        };
        var otherUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "other@example.com",
            Email = "other@example.com"
        };
        var credentialId = new byte[] { 11, 12, 13 };
        var ownerPasskey = CreatePasskey(credentialId, "Owner device");
        var store = new InMemoryPasskeyStore(owner, otherUser);
        var userManager = CreateUserManager(store);
        var signInManager = new StubSignInManager(
            userManager,
            PasskeyAttestationResult.Success(
                ownerPasskey,
                new PasskeyUserEntity
                {
                    Id = owner.Id.ToString("D"),
                    Name = owner.UserName!,
                    DisplayName = owner.Email!
                }));
        var service = new AccountPasskeyService(userManager, signInManager);

        var registered = await service.RegisterAsync(owner.Id, "valid-credential");

        Assert.Equal(PasskeyOperationStatus.Succeeded, registered.Status);
        Assert.Single(await store.GetPasskeysAsync(owner, CancellationToken.None));

        var conflictingPasskey = CreatePasskey(credentialId, "Other device");
        signInManager.AttestationResult = PasskeyAttestationResult.Success(
            conflictingPasskey,
            new PasskeyUserEntity
            {
                Id = otherUser.Id.ToString("D"),
                Name = otherUser.UserName!,
                DisplayName = otherUser.Email!
            });

        var conflict = await service.RegisterAsync(otherUser.Id, "valid-credential");

        Assert.Equal(PasskeyOperationStatus.Conflict, conflict.Status);
        var ownerPasskeys = await store.GetPasskeysAsync(owner, CancellationToken.None);
        Assert.Equal("Owner device", Assert.Single(ownerPasskeys).Name);
    }

    private static UserManager<ApplicationUser> CreateUserManager(InMemoryPasskeyStore store) =>
        new(
            store,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

    private static UserPasskeyInfo CreatePasskey(byte[] credentialId, string name) =>
        new(
            credentialId,
            [1, 2, 3],
            DateTimeOffset.UtcNow,
            0,
            ["internal"],
            true,
            true,
            false,
            [4, 5, 6],
            [7, 8, 9])
        {
            Name = name
        };

    private sealed class StubSignInManager(
        UserManager<ApplicationUser> userManager,
        PasskeyAttestationResult attestationResult)
        : SignInManager<ApplicationUser>(
            userManager,
            new HttpContextAccessor(),
            new UserClaimsPrincipalFactory<ApplicationUser>(
                userManager,
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions())),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            new AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())),
            new DefaultUserConfirmation<ApplicationUser>())
    {
        public PasskeyAttestationResult AttestationResult { get; set; } = attestationResult;

        public override Task<PasskeyAttestationResult> PerformPasskeyAttestationAsync(string credentialJson) =>
            Task.FromResult(AttestationResult);
    }

    private sealed class InMemoryPasskeyStore(params ApplicationUser[] users)
        : IUserPasskeyStore<ApplicationUser>
    {
        private readonly Dictionary<Guid, ApplicationUser> users = users.ToDictionary(user => user.Id);
        private readonly Dictionary<Guid, List<UserPasskeyInfo>> passkeys = users.ToDictionary(
            user => user.Id,
            _ => new List<UserPasskeyInfo>());

        public Task AddOrUpdatePasskeyAsync(
            ApplicationUser user,
            UserPasskeyInfo passkey,
            CancellationToken cancellationToken)
        {
            var existing = passkeys[user.Id].FindIndex(candidate =>
                candidate.CredentialId.SequenceEqual(passkey.CredentialId));
            if (existing >= 0)
            {
                passkeys[user.Id][existing] = passkey;
            }
            else
            {
                passkeys[user.Id].Add(passkey);
            }

            return Task.CompletedTask;
        }

        public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(
            ApplicationUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult<IList<UserPasskeyInfo>>(passkeys[user.Id].ToList());

        public Task<ApplicationUser?> FindByPasskeyIdAsync(
            byte[] credentialId,
            CancellationToken cancellationToken) =>
            Task.FromResult(users.Values.FirstOrDefault(user => passkeys[user.Id].Any(passkey =>
                passkey.CredentialId.SequenceEqual(credentialId))));

        public Task<UserPasskeyInfo?> FindPasskeyAsync(
            ApplicationUser user,
            byte[] credentialId,
            CancellationToken cancellationToken) =>
            Task.FromResult(passkeys[user.Id].FirstOrDefault(passkey =>
                passkey.CredentialId.SequenceEqual(credentialId)));

        public Task RemovePasskeyAsync(
            ApplicationUser user,
            byte[] credentialId,
            CancellationToken cancellationToken)
        {
            passkeys[user.Id].RemoveAll(passkey => passkey.CredentialId.SequenceEqual(credentialId));
            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.TryParse(userId, out var id) && users.TryGetValue(id, out var user)
                ? user
                : null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(users.Values.FirstOrDefault(user => user.NormalizedUserName == normalizedUserName));

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString("D"));

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(
            ApplicationUser user,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(
            ApplicationUser user,
            string? userName,
            CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
    }
}
