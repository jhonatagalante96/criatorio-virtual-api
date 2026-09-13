using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class AccountPasskeyAuthenticationServiceTests
{
    [Fact]
    public async Task CreateRequestOptionsAsync_UsesDiscoverableIdentityRequestOptions()
    {
        var userManager = CreateUserManager();
        var signInManager = new StubSignInManager(userManager)
        {
            RequestOptionsJson = "{\"challenge\":\"challenge\"}"
        };
        var service = new AccountPasskeyAuthenticationService(signInManager, userManager);

        var result = await service.CreateRequestOptionsAsync();

        Assert.Equal(PasskeyAuthenticationStatus.Succeeded, result.Status);
        Assert.Equal("{\"challenge\":\"challenge\"}", result.OptionsJson);
        Assert.Null(signInManager.RequestedUser);
    }

    [Fact]
    public async Task AuthenticateAsync_DelegatesCredentialToIdentityAndMapsTheSessionResult()
    {
        var userManager = CreateUserManager();
        var signInManager = new StubSignInManager(userManager)
        {
            AuthenticationResult = SignInResult.Success
        };
        var service = new AccountPasskeyAuthenticationService(signInManager, userManager);

        var result = await service.AuthenticateAsync("credential-json");

        Assert.Equal(PasskeyAuthenticationStatus.Succeeded, result.Status);
        Assert.Equal("credential-json", signInManager.CredentialJson);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsGenericInvalidForFailedOrMalformedCredentials()
    {
        var userManager = CreateUserManager();
        var signInManager = new StubSignInManager(userManager)
        {
            AuthenticationResult = SignInResult.Failed
        };
        var service = new AccountPasskeyAuthenticationService(signInManager, userManager);

        var failed = await service.AuthenticateAsync("credential-json");
        var empty = await service.AuthenticateAsync(" ");

        Assert.Equal(PasskeyAuthenticationStatus.Invalid, failed.Status);
        Assert.Equal(PasskeyAuthenticationStatus.Invalid, empty.Status);
    }

    [Fact]
    public async Task AuthenticateAsync_MapsIdentityNotAllowedToNotAllowed()
    {
        var userManager = CreateUserManager();
        var signInManager = new StubSignInManager(userManager)
        {
            AuthenticationResult = SignInResult.NotAllowed
        };
        var service = new AccountPasskeyAuthenticationService(signInManager, userManager);

        var result = await service.AuthenticateAsync("credential-json");

        Assert.Equal(PasskeyAuthenticationStatus.NotAllowed, result.Status);
    }

    private static UserManager<ApplicationUser> CreateUserManager() =>
        new(
            new NoopPasskeyStore(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

    private sealed class StubSignInManager(UserManager<ApplicationUser> userManager)
        : SignInManager<ApplicationUser>(
            userManager,
            new HttpContextAccessor(),
            new UserClaimsPrincipalFactory<ApplicationUser>(
                userManager,
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions())),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            new AuthenticationSchemeProvider(
                Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())),
            new DefaultUserConfirmation<ApplicationUser>())
    {
        public string RequestOptionsJson { get; init; } = "{}";

        public ApplicationUser? RequestedUser { get; private set; }

        public string? CredentialJson { get; private set; }

        public SignInResult AuthenticationResult { get; init; } = SignInResult.Failed;

        public override Task<string> MakePasskeyRequestOptionsAsync(ApplicationUser? user)
        {
            RequestedUser = user;
            return Task.FromResult(RequestOptionsJson);
        }

        public override Task<SignInResult> PasskeySignInAsync(string credentialJson)
        {
            CredentialJson = credentialJson;
            return Task.FromResult(AuthenticationResult);
        }
    }

    private sealed class NoopPasskeyStore : IUserPasskeyStore<ApplicationUser>
    {
        public Task AddOrUpdatePasskeyAsync(
            ApplicationUser user,
            UserPasskeyInfo passkey,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(
            ApplicationUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult<IList<UserPasskeyInfo>>([]);

        public Task<ApplicationUser?> FindByPasskeyIdAsync(
            byte[] credentialId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(null);

        public Task<UserPasskeyInfo?> FindPasskeyAsync(
            ApplicationUser user,
            byte[] credentialId,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserPasskeyInfo?>(null);

        public Task RemovePasskeyAsync(
            ApplicationUser user,
            byte[] credentialId,
            CancellationToken cancellationToken) => Task.CompletedTask;

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
