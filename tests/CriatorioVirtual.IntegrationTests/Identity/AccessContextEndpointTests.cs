using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Identity;

public sealed class AccessContextEndpointTests
{
    private const string AccessContextRoute = "/api/me/access-context";

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(AccessContextRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_WithoutBreedingFarm_Returns200WithNullFarmAndCreateFarmOnboarding()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var email = "nofarm@example.com";
        var userId = await RegisterAndAuthenticateAsync(factory, client, email);

        using var response = await client.GetAsync(AccessContextRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        // User
        var user = root.GetProperty("user");
        Assert.Equal(userId, user.GetProperty("id").GetGuid());
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("avatarUrl").ValueKind);

        // Breeding farm
        Assert.Equal(JsonValueKind.Null, root.GetProperty("breedingFarm").ValueKind);

        // Onboarding
        var onboarding = root.GetProperty("onboarding");
        Assert.Equal("Pending", onboarding.GetProperty("status").GetString());
        Assert.Equal("CreateBreedingFarm", onboarding.GetProperty("nextStep").GetString());

        // Access: sem tratar como dívida
        var access = root.GetProperty("access");
        Assert.Equal("PendingSubscription", access.GetProperty("status").GetString());
        Assert.False(access.GetProperty("canAccessApp").GetBoolean());
        Assert.Equal(JsonValueKind.Null, access.GetProperty("blockedReason").ValueKind);
        Assert.Equal("None", access.GetProperty("requiredAction").GetString());
        Assert.Equal(JsonValueKind.Null, access.GetProperty("trialEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, access.GetProperty("gracePeriodEndsAt").ValueKind);

        // Subscription
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subscription").ValueKind);
    }

    [Fact]
    public async Task Authenticated_WithFarmsNoneSelected_ReturnsOnboardingSelectBreedingFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "unselected@example.com");

        var farmId = await CreateFarmAsync(client, "Criatório Solar");
        // Deselect farm
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == "unselected@example.com");
            user.SelectedBreedingFarmId = null;
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.GetAsync(AccessContextRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var onboarding = document.RootElement.GetProperty("onboarding");
        Assert.Equal("Pending", onboarding.GetProperty("status").GetString());
        Assert.Equal("SelectBreedingFarm", onboarding.GetProperty("nextStep").GetString());
    }

    [Fact]
    public async Task Authenticated_WithAvatar_ReturnsAvatarUrl()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var email = "avatar@example.com";
        var userId = await RegisterAndAuthenticateAsync(factory, client, email);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId);
            user.AvatarObjectKey = "avatars/test-avatar.jpg";
            user.AvatarContentType = "image/jpeg";
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.GetAsync(AccessContextRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var avatarUrl = document.RootElement.GetProperty("user").GetProperty("avatarUrl").GetString();
        Assert.NotNull(avatarUrl);
        Assert.Contains("/api/me/avatar", avatarUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial, true, null, "None")]
    [InlineData(SubscriptionStatus.Active, true, null, "None")]
    [InlineData(SubscriptionStatus.GracePeriod, true, null, "Regularize")]
    [InlineData(SubscriptionStatus.PendingSubscription, false, "SubscriptionRequired", "Subscribe")]
    [InlineData(SubscriptionStatus.Blocked, false, "PaymentOverdue", "Regularize")]
    [InlineData(SubscriptionStatus.Cancelled, false, "SubscriptionCancelled", "Resubscribe")]
    public async Task SubscriptionStatuses_MapExpectedAccessAndCanAccessApp(
        SubscriptionStatus status,
        bool expectedCanAccess,
        string? expectedBlockedReason,
        string expectedRequiredAction)
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var email = $"status-{status.ToString().ToLowerInvariant()}@example.com";
        var userId = await RegisterAndAuthenticateAsync(factory, client, email);

        var farmId = await CreateFarmAsync(client, $"Farm {status}");
        await SelectFarmAsync(client, farmId);

        await SeedSubscriptionWithStatusAsync(factory, farmId, status);

        using var response = await client.GetAsync(AccessContextRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        var farm = root.GetProperty("breedingFarm");
        Assert.Equal(farmId, farm.GetProperty("id").GetGuid());
        Assert.Equal($"Farm {status}", farm.GetProperty("name").GetString());
        Assert.Equal("Owner", farm.GetProperty("role").GetString());

        var onboarding = root.GetProperty("onboarding");
        Assert.Equal("Completed", onboarding.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, onboarding.GetProperty("nextStep").ValueKind);

        var access = root.GetProperty("access");
        Assert.Equal(status.ToString(), access.GetProperty("status").GetString());
        Assert.Equal(expectedCanAccess, access.GetProperty("canAccessApp").GetBoolean());
        if (expectedBlockedReason is null)
        {
            Assert.Equal(JsonValueKind.Null, access.GetProperty("blockedReason").ValueKind);
        }
        else
        {
            Assert.Equal(expectedBlockedReason, access.GetProperty("blockedReason").GetString());
        }
        Assert.Equal(expectedRequiredAction, access.GetProperty("requiredAction").GetString());

        var subscription = root.GetProperty("subscription");
        Assert.Equal("PRO", subscription.GetProperty("plan").GetString());
        Assert.Equal("Monthly", subscription.GetProperty("cycle").GetString());
        Assert.Equal(status.ToString(), subscription.GetProperty("status").GetString());
    }

    [Fact]
    public async Task FunctionalBlocking_DeniesFunctionalEndpointsWith403_WhenBlocked()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "blocked-functional@example.com");

        var farmId = await CreateFarmAsync(client, "Blocked Farm");
        await SelectFarmAsync(client, farmId);
        await SeedSubscriptionWithStatusAsync(factory, farmId, SubscriptionStatus.Blocked);

        // Protected functional endpoints must return 403 functional_access_blocked
        using var birdsResponse = await client.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.Forbidden, birdsResponse.StatusCode);
        using var birdsDoc = JsonDocument.Parse(await birdsResponse.Content.ReadAsStreamAsync());
        Assert.Equal("functional_access_blocked", birdsDoc.RootElement.GetProperty("code").GetString());

        // Non-allowlisted billing endpoints (e.g. GET /api/billing/payments) must also be blocked
        using var paymentsResponse = await client.GetAsync("/api/billing/payments");
        Assert.Equal(HttpStatusCode.Forbidden, paymentsResponse.StatusCode);
        using var paymentsDoc = JsonDocument.Parse(await paymentsResponse.Content.ReadAsStreamAsync());
        Assert.Equal("functional_access_blocked", paymentsDoc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FunctionalBlocking_AllowsAllowlistedRoutes_WhenBlocked()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "allowlist-test@example.com");

        var farmId = await CreateFarmAsync(client, "Allowlist Farm");
        await SelectFarmAsync(client, farmId);
        await SeedSubscriptionWithStatusAsync(factory, farmId, SubscriptionStatus.Blocked);

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);

        // 1. GET /api/me/access-context must remain 200
        using var contextResponse = await client.GetAsync(AccessContextRoute);
        Assert.Equal(HttpStatusCode.OK, contextResponse.StatusCode);

        // 2. GET /api/auth/session must remain 200
        using var sessionResponse = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        // 3. GET /api/billing/subscription must remain 200
        using var subscriptionResponse = await client.GetAsync("/api/billing/subscription");
        Assert.Equal(HttpStatusCode.OK, subscriptionResponse.StatusCode);

        // 4. POST /api/billing/subscription-checkouts must NOT be 403 functional_access_blocked
        using var checkoutResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/billing/subscription-checkouts",
            antiforgeryToken,
            new { }));
        Assert.NotEqual(HttpStatusCode.Forbidden, checkoutResponse.StatusCode);

        // 5. POST /api/billing/subscriptions must NOT be 403 functional_access_blocked
        using var createSubResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/billing/subscriptions",
            antiforgeryToken,
            new { }));
        Assert.NotEqual(HttpStatusCode.Forbidden, createSubResponse.StatusCode);

        // 6. DELETE /api/billing/subscriptions must NOT be 403 functional_access_blocked
        using var cancelSubResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Delete,
            "/api/billing/subscriptions",
            antiforgeryToken));
        Assert.NotEqual(HttpStatusCode.Forbidden, cancelSubResponse.StatusCode);

        // 7. POST /api/billing/payments/{id}/regularization must NOT be 403 functional_access_blocked
        var dummyPaymentId = Guid.NewGuid();
        using var regularizeResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/billing/payments/{dummyPaymentId}/regularization",
            antiforgeryToken));
        Assert.NotEqual(HttpStatusCode.Forbidden, regularizeResponse.StatusCode);

        // 8. POST /api/billing/payments/{id}/attempts must NOT be 403 functional_access_blocked
        using var attemptResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/billing/payments/{dummyPaymentId}/attempts",
            antiforgeryToken,
            new { }));
        Assert.NotEqual(HttpStatusCode.Forbidden, attemptResponse.StatusCode);

        // 9. POST /api/auth/logout must remain accessible (204)
        using var logoutResponse = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/logout",
            antiforgeryToken));
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task FunctionalBlocking_DeniesFunctionalEndpointsWith403_WhenNoSubscription()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "no-subscription-functional@example.com");

        var farmId = await CreateFarmAsync(client, "No Subscription Farm");
        await SelectFarmAsync(client, farmId);
        // Do NOT seed any subscription for this farm

        using var response = await client.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("functional_access_blocked", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetAccessContext_ReturnsPendingSubscription_WhenFarmHasNoSubscription()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "no-subscription-context@example.com");

        var farmId = await CreateFarmAsync(client, "Unsubscribed Farm");
        await SelectFarmAsync(client, farmId);

        using var response = await client.GetAsync(AccessContextRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        var farm = root.GetProperty("breedingFarm");
        Assert.Equal(farmId, farm.GetProperty("id").GetGuid());
        Assert.Equal("Unsubscribed Farm", farm.GetProperty("name").GetString());
        Assert.Equal("Owner", farm.GetProperty("role").GetString());

        var onboarding = root.GetProperty("onboarding");
        Assert.Equal("Completed", onboarding.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, onboarding.GetProperty("nextStep").ValueKind);

        var access = root.GetProperty("access");
        Assert.Equal("PendingSubscription", access.GetProperty("status").GetString());
        Assert.False(access.GetProperty("canAccessApp").GetBoolean());
        Assert.Equal("SubscriptionRequired", access.GetProperty("blockedReason").GetString());
        Assert.Equal("Subscribe", access.GetProperty("requiredAction").GetString());

        Assert.Equal(JsonValueKind.Null, root.GetProperty("subscription").ValueKind);
    }

    [Fact]
    public async Task GetAccessContext_IgnoresSelectedFarm_WhenMembershipIsNotOwnerOrInactive()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var userId = await RegisterAndAuthenticateAsync(factory, client, "non-owner-test@example.com");

        var farmId = await CreateFarmAsync(client, "Role Test Farm");
        await SelectFarmAsync(client, farmId);
        await SeedSubscriptionWithStatusAsync(factory, farmId, SubscriptionStatus.Active);

        // Case 1: Non-owner role (e.g. Manager) -> farm must be treated as unselected
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            await db.BreedingFarmUsers
                .Where(m => m.UserId == userId && m.BreedingFarmId == farmId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Role, BreedingFarmRole.Manager));
        }

        using (var response = await client.GetAsync(AccessContextRoute))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = document.RootElement;

            Assert.Equal(JsonValueKind.Null, root.GetProperty("breedingFarm").ValueKind);
            var onboarding = root.GetProperty("onboarding");
            Assert.Equal("Pending", onboarding.GetProperty("status").GetString());
            Assert.Equal("CreateBreedingFarm", onboarding.GetProperty("nextStep").GetString());

            var access = root.GetProperty("access");
            Assert.Equal("PendingSubscription", access.GetProperty("status").GetString());
            Assert.False(access.GetProperty("canAccessApp").GetBoolean());
            Assert.Equal(JsonValueKind.Null, access.GetProperty("blockedReason").ValueKind);
            Assert.Equal("None", access.GetProperty("requiredAction").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("subscription").ValueKind);
        }

        // Case 2: Inactive membership -> farm must be treated as unselected
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            await db.BreedingFarmUsers
                .Where(m => m.UserId == userId && m.BreedingFarmId == farmId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Role, BreedingFarmRole.Owner)
                    .SetProperty(b => b.IsActive, false));
        }

        using (var response = await client.GetAsync(AccessContextRoute))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = document.RootElement;

            Assert.Equal(JsonValueKind.Null, root.GetProperty("breedingFarm").ValueKind);
            var onboarding = root.GetProperty("onboarding");
            Assert.Equal("Pending", onboarding.GetProperty("status").GetString());
            Assert.Equal("CreateBreedingFarm", onboarding.GetProperty("nextStep").GetString());

            var access = root.GetProperty("access");
            Assert.Equal("PendingSubscription", access.GetProperty("status").GetString());
            Assert.False(access.GetProperty("canAccessApp").GetBoolean());
            Assert.Equal(JsonValueKind.Null, access.GetProperty("blockedReason").ValueKind);
            Assert.Equal("None", access.GetProperty("requiredAction").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("subscription").ValueKind);
        }
    }

    [Fact]
    public async Task FunctionalBlocking_PermitsFunctionalEndpoints_WhenTrialActiveOrGrace()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "allowed-functional@example.com");

        var farmId = await CreateFarmAsync(client, "Active Farm");
        await SelectFarmAsync(client, farmId);
        await SeedSubscriptionWithStatusAsync(factory, farmId, SubscriptionStatus.Active);

        // GET /api/birds must return 200 OK
        using var birdsResponse = await client.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.OK, birdsResponse.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_DoesNotExposeAnotherUsersFarmOrSubscription()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var userAClient = CreateClient(factory);
        using var userBClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, userAClient, "usera@example.com");
        await RegisterAndAuthenticateAsync(factory, userBClient, "userb@example.com");

        var farmAId = await CreateFarmAsync(userAClient, "Farm A");
        await SelectFarmAsync(userAClient, farmAId);
        await SeedSubscriptionWithStatusAsync(factory, farmAId, SubscriptionStatus.Active);

        var farmBId = await CreateFarmAsync(userBClient, "Farm B");
        await SelectFarmAsync(userBClient, farmBId);
        await SeedSubscriptionWithStatusAsync(factory, farmBId, SubscriptionStatus.Blocked);

        // User A context
        using var userAResponse = await userAClient.GetAsync(AccessContextRoute);
        Assert.Equal(HttpStatusCode.OK, userAResponse.StatusCode);
        using var userADoc = JsonDocument.Parse(await userAResponse.Content.ReadAsStreamAsync());
        Assert.Equal(farmAId, userADoc.RootElement.GetProperty("breedingFarm").GetProperty("id").GetGuid());
        Assert.True(userADoc.RootElement.GetProperty("access").GetProperty("canAccessApp").GetBoolean());

        // User B context
        using var userBResponse = await userBClient.GetAsync(AccessContextRoute);
        Assert.Equal(HttpStatusCode.OK, userBResponse.StatusCode);
        using var userBDoc = JsonDocument.Parse(await userBResponse.Content.ReadAsStreamAsync());
        Assert.Equal(farmBId, userBDoc.RootElement.GetProperty("breedingFarm").GetProperty("id").GetGuid());
        Assert.False(userBDoc.RootElement.GetProperty("access").GetProperty("canAccessApp").GetBoolean());
    }

    [Fact]
    public async Task BillingChange_ImmediatelyReflectsInAccessContextAndUnblocksAccess()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "dynamic-billing@example.com");

        var farmId = await CreateFarmAsync(client, "Dynamic Farm");
        await SelectFarmAsync(client, farmId);
        await SeedSubscriptionWithStatusAsync(factory, farmId, SubscriptionStatus.Blocked);

        // Initially blocked
        using var initialContext = await client.GetAsync(AccessContextRoute);
        Assert.Equal(HttpStatusCode.OK, initialContext.StatusCode);
        using var initialDoc = JsonDocument.Parse(await initialContext.Content.ReadAsStreamAsync());
        Assert.False(initialDoc.RootElement.GetProperty("access").GetProperty("canAccessApp").GetBoolean());

        using var initialBirds = await client.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.Forbidden, initialBirds.StatusCode);

        // Billing changes: unblock to Active in database
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var subscription = await dbContext.Subscriptions.SingleAsync(s => s.BreedingFarmId == farmId);
            // Simulate recovery/payment confirmation
            subscription.ConfirmPayment(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        // Context must immediately reflect Active
        using var updatedContext = await client.GetAsync(AccessContextRoute);
        Assert.Equal(HttpStatusCode.OK, updatedContext.StatusCode);
        using var updatedDoc = JsonDocument.Parse(await updatedContext.Content.ReadAsStreamAsync());
        Assert.True(updatedDoc.RootElement.GetProperty("access").GetProperty("canAccessApp").GetBoolean());
        Assert.Equal("Active", updatedDoc.RootElement.GetProperty("access").GetProperty("status").GetString());

        // Functional endpoint is now unblocked
        using var updatedBirds = await client.GetAsync("/api/birds");
        Assert.Equal(HttpStatusCode.OK, updatedBirds.StatusCode);
    }

    private static async Task SeedSubscriptionWithStatusAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        SubscriptionStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var now = DateTimeOffset.UtcNow;

        var subscription = new Subscription(
            Guid.NewGuid(),
            farmId,
            "PRO",
            BillingCycle.Monthly,
            now.AddDays(-20));

        switch (status)
        {
            case SubscriptionStatus.PendingSubscription:
                // default state
                break;
            case SubscriptionStatus.Trial:
                subscription.ConfirmRecurringSubscription("cust-trial", "sub-trial", now.AddDays(-2));
                break;
            case SubscriptionStatus.Active:
                subscription.ConfirmRecurringSubscription("cust-active", "sub-active", now.AddDays(-15));
                subscription.ConfirmPayment(now.AddDays(-8));
                break;
            case SubscriptionStatus.GracePeriod:
                subscription.ConfirmRecurringSubscription("cust-grace", "sub-grace", now.AddDays(-15));
                subscription.StartGracePeriod(subscription.TrialEndsAtUtc!.Value);
                break;
            case SubscriptionStatus.Blocked:
                subscription.ConfirmRecurringSubscription("cust-blocked", "sub-blocked", now.AddDays(-25));
                subscription.StartGracePeriod(subscription.TrialEndsAtUtc!.Value);
                subscription.TryBlockAfterGracePeriodExpiration(subscription.GracePeriodEndsAtUtc!.Value.AddMinutes(1));
                break;
            case SubscriptionStatus.Cancelled:
                subscription.ConfirmRecurringSubscription("cust-canc", "sub-canc", now.AddDays(-15));
                subscription.Cancel(now.AddDays(-1));
                break;
        }

        dbContext.Subscriptions.Add(subscription);
        await dbContext.SaveChangesAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        X509Certificate2 certificate) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services => services.AddInfrastructurePersistence(connectionString, certificate));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task<Guid> RegisterAndAuthenticateAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email)
    {
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var registration = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/register",
            antiforgeryToken,
            new { email, password = "StrongPassword!123", confirmPassword = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email);
            user.EmailConfirmed = true;
            userId = user.Id;
            await dbContext.SaveChangesAsync();
        }

        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return userId;
    }

    private static async Task<Guid> CreateFarmAsync(HttpClient client, string name)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new { name, responsibleName = "Owner Principal", contactEmail = "owner@example.com" }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("breedingFarmId").GetGuid();
    }

    private static async Task SelectFarmAsync(HttpClient client, Guid farmId)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            "/api/breeding-farms/selection",
            await GetAntiforgeryTokenAsync(client),
            new { breedingFarmId = farmId }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        request.Headers.Add("Origin", "http://localhost:3000");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.GetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName).Single();
    }

    private static HttpRequestMessage CreateBrowserRequest(
        HttpMethod method,
        string path,
        string antiforgeryToken,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        return request;
    }
}
