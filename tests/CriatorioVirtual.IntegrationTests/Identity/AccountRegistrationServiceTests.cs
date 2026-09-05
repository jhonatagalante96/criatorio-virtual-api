using CriatorioVirtual.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class AccountRegistrationServiceTests
{
    [Fact]
    public async Task RegisterAsync_CreatesAnUnconfirmedLockedOutCapableIdentity()
    {
        var userManager = new StubUserManager(IdentityResult.Success);
        var service = new AccountRegistrationService(userManager);

        var result = await service.RegisterAsync("  user@example.com ", "StrongPassword!123");

        Assert.Equal(AccountRegistrationStatus.Created, result.Status);
        Assert.NotNull(result.User);
        Assert.NotEqual(Guid.Empty, result.User.Id);
        Assert.Equal("user@example.com", result.User.Email);
        Assert.Equal("user@example.com", result.User.UserName);
        Assert.False(result.User.EmailConfirmed);
        Assert.True(result.User.LockoutEnabled);
        Assert.Equal("StrongPassword!123", userManager.Password);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsInvalidWithoutCreatingASecondIdentity()
    {
        var userManager = new StubUserManager(IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "The password is too short." }));
        var service = new AccountRegistrationService(userManager);

        var result = await service.RegisterAsync("user@example.com", "short");

        Assert.Equal(AccountRegistrationStatus.Invalid, result.Status);
        Assert.Null(result.User);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task RegisterAsync_MapsIdentityDuplicateErrorsToConflict()
    {
        var userManager = new StubUserManager(IdentityResult.Failed(
            new IdentityError { Code = nameof(IdentityErrorDescriber.DuplicateEmail), Description = "Duplicate." }));
        var service = new AccountRegistrationService(userManager);

        var result = await service.RegisterAsync("user@example.com", "StrongPassword!123");

        Assert.Equal(AccountRegistrationStatus.Duplicate, result.Status);
        Assert.Null(result.User);
    }

    private sealed class StubUserManager(IdentityResult result)
        : UserManager<ApplicationUser>(
            new NoopUserStore(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance)
    {
        private readonly IdentityResult result = result;

        public string? Password { get; private set; }

        public override Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        {
            Password = password;
            return Task.FromResult(result);
        }
    }

    private sealed class NoopUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(null);

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);
    }
}
