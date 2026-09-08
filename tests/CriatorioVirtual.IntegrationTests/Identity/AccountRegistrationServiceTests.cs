using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Application.Identity;
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
        var emailSender = new StubEmailSender();
        var service = new AccountRegistrationService(userManager, new StubEmailLinkBuilder(), emailSender);

        var result = await service.RegisterAsync("  user@example.com ", "StrongPassword!123");

        Assert.Equal(AccountRegistrationStatus.Created, result.Status);
        Assert.NotNull(result.User);
        Assert.NotEqual(Guid.Empty, result.User.Id);
        Assert.Equal("user@example.com", result.User.Email);
        Assert.Equal("user@example.com", result.User.UserName);
        Assert.False(result.User.EmailConfirmed);
        Assert.True(result.User.LockoutEnabled);
        Assert.Equal("StrongPassword!123", userManager.Password);
        Assert.Equal("user@example.com", emailSender.Message?.Recipient);
        Assert.Equal(AuthenticationEmailKind.Confirmation, emailSender.Message?.Kind);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsInvalidWithoutCreatingASecondIdentity()
    {
        var userManager = new StubUserManager(IdentityResult.Failed(
            new IdentityError { Code = "PasswordTooShort", Description = "The password is too short." }));
        var service = new AccountRegistrationService(
            userManager,
            new StubEmailLinkBuilder(),
            new StubEmailSender());

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
        var service = new AccountRegistrationService(
            userManager,
            new StubEmailLinkBuilder(),
            new StubEmailSender());

        var result = await service.RegisterAsync("user@example.com", "StrongPassword!123");

        Assert.Equal(AccountRegistrationStatus.Duplicate, result.Status);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsRecoverableFailureWhenConfirmationDeliveryFails()
    {
        var userManager = new StubUserManager(IdentityResult.Success);
        var service = new AccountRegistrationService(
            userManager,
            new StubEmailLinkBuilder(),
            new StubEmailSender(shouldFail: true));

        var result = await service.RegisterAsync("user@example.com", "StrongPassword!123");

        Assert.Equal(AccountRegistrationStatus.EmailDeliveryFailed, result.Status);
        Assert.Null(result.User);
        Assert.Empty(result.Errors);
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

        public override Task<string> GenerateEmailConfirmationTokenAsync(ApplicationUser user) =>
            Task.FromResult("confirmation-token");
    }

    private sealed class StubEmailLinkBuilder : IAuthenticationEmailLinkBuilder
    {
        public Uri Build(AuthenticationEmailKind kind, Guid userId, string token) =>
            new($"http://localhost:3000/auth/{kind.ToString().ToLowerInvariant()}?userId={userId:D}&token={Uri.EscapeDataString(token)}");
    }

    private sealed class StubEmailSender(bool shouldFail = false) : IAuthenticationEmailSender
    {
        private readonly bool shouldFail = shouldFail;

        public AuthenticationEmailMessage? Message { get; private set; }

        public Task<AuthenticationEmailDeliveryResult> SendAsync(
            AuthenticationEmailMessage message,
            CancellationToken cancellationToken = default)
        {
            Message = message;
            return Task.FromResult(shouldFail
                ? AuthenticationEmailDeliveryResult.Failed()
                : AuthenticationEmailDeliveryResult.Delivered());
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
