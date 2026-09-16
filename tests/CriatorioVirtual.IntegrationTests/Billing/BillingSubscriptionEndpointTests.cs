using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class BillingSubscriptionEndpointTests
{
    private const string Route = "/api/billing/subscriptions";

    [Fact]
    public async Task OwnerCanPurchaseConfiguredMonthlyAndAnnualSubscriptionsAndRetryIdempotently()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "billing-purchase@example.com");

        var monthlyFarmId = await CreateFarmAsync(client, "Monthly Farm");
        await SelectFarmAsync(client, monthlyFarmId);
        gateway.SubscriptionStatus = "PENDING";
        using var pendingResponse = await SendPurchaseAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "monthly",
            "123.456.789-09",
            "monthly-card-token");

        Assert.Equal(HttpStatusCode.Accepted, pendingResponse.StatusCode);
        using var pendingBody = JsonDocument.Parse(await pendingResponse.Content.ReadAsStreamAsync());
        Assert.Equal(19.90m, pendingBody.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("monthly", pendingBody.RootElement.GetProperty("billingCycle").GetString());
        Assert.Equal("pendingConfirmation", pendingBody.RootElement.GetProperty("purchaseOutcome").GetString());
        Assert.Equal(SubscriptionStatus.PendingSubscription.ToString(), pendingBody.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, pendingBody.RootElement.GetProperty("trialStartedAtUtc").ValueKind);

        var monthlySubscriptionId = pendingBody.RootElement.GetProperty("subscriptionId").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var subscription = await dbContext.Subscriptions.SingleAsync(candidate => candidate.Id == monthlySubscriptionId);
            Assert.Equal(monthlyFarmId, subscription.BreedingFarmId);
            Assert.Equal(19.90m, subscription.AgreedAmount);
            Assert.Equal(SubscriptionStatus.PendingSubscription, subscription.Status);
        }

        gateway.SubscriptionStatus = "ACTIVE";
        using var monthlyResponse = await SendPurchaseAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "monthly",
            "12345678909",
            "monthly-retry-card-token");
        Assert.Equal(HttpStatusCode.OK, monthlyResponse.StatusCode);
        using var monthlyBody = JsonDocument.Parse(await monthlyResponse.Content.ReadAsStreamAsync());
        Assert.Equal(monthlySubscriptionId, monthlyBody.RootElement.GetProperty("subscriptionId").GetGuid());
        Assert.Equal(19.90m, monthlyBody.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("BRL", monthlyBody.RootElement.GetProperty("currencyCode").GetString());
        Assert.Equal("monthly", monthlyBody.RootElement.GetProperty("billingCycle").GetString());
        Assert.Equal("trialStarted", monthlyBody.RootElement.GetProperty("purchaseOutcome").GetString());
        Assert.Equal(
            monthlyBody.RootElement.GetProperty("trialStartedAtUtc").GetDateTimeOffset().AddDays(7),
            monthlyBody.RootElement.GetProperty("firstChargeDueAtUtc").GetDateTimeOffset());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var subscription = await dbContext.Subscriptions.SingleAsync(candidate => candidate.Id == monthlySubscriptionId);
            Assert.Equal(monthlyFarmId, subscription.BreedingFarmId);
            Assert.Equal(19.90m, subscription.AgreedAmount);
            Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        }

        var monthlyGatewayRequest = gateway.SubscriptionRequests.First();
        Assert.Equal(19.90m, monthlyGatewayRequest.Amount);
        Assert.Equal(BillingCycle.Monthly, monthlyGatewayRequest.BillingCycle);
        Assert.Equal("monthly-card-token", monthlyGatewayRequest.CardToken);
        Assert.Equal(2, gateway.SubscriptionRequests.Count);
        Assert.Single(gateway.SubscriptionRequests.Select(request => request.SubscriptionId).Distinct());
        Assert.Equal("12345678909", gateway.CustomerRequests.First().TaxIdentifier);

        using var retryResponse = await SendPurchaseAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "monthly",
            "12345678909",
            "replacement-token-is-not-used");
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using (var retryBody = JsonDocument.Parse(await retryResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal("alreadyExists", retryBody.RootElement.GetProperty("purchaseOutcome").GetString());
            Assert.Equal(monthlySubscriptionId, retryBody.RootElement.GetProperty("subscriptionId").GetGuid());
            Assert.Equal(19.90m, retryBody.RootElement.GetProperty("amount").GetDecimal());
        }

        var annualFarmId = await CreateFarmAsync(client, "Annual Farm");
        await SelectFarmAsync(client, annualFarmId);
        using var annualResponse = await SendPurchaseAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "annual",
            "12.ABC.345/0001-90",
            "annual-card-token");
        Assert.Equal(HttpStatusCode.OK, annualResponse.StatusCode);
        using var annualBody = JsonDocument.Parse(await annualResponse.Content.ReadAsStreamAsync());
        Assert.Equal(199.90m, annualBody.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("annual", annualBody.RootElement.GetProperty("billingCycle").GetString());
        Assert.Equal(199.90m, gateway.SubscriptionRequests.Last().Amount);
        Assert.Equal(BillingCycle.Annual, gateway.SubscriptionRequests.Last().BillingCycle);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            Assert.Equal(2, await dbContext.Subscriptions.CountAsync());
            var persistedPropertyNames = typeof(Subscription).GetProperties().Select(property => property.Name).ToArray();
            Assert.DoesNotContain(persistedPropertyNames, name => name.Contains("Card", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(persistedPropertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task SubscriptionPurchaseRequiresOwnerOfSelectedFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);

        var token = await GetAntiforgeryTokenAsync(ownerClient);
        using var unauthenticated = await SendPurchaseAsync(ownerClient, token, "monthly", "12345678909", "card-token");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, ownerClient, "billing-owner@example.com");
        var farmId = await CreateFarmAsync(ownerClient, "Owner Farm");
        await SelectFarmAsync(ownerClient, farmId);

        using var invalidCycleResponse = await SendPurchaseAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "weekly",
            "12345678909",
            "card-token");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCycleResponse.StatusCode);
        using var invalidTaxIdentifierResponse = await SendPurchaseAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "monthly",
            "not-a-tax-identifier",
            "card-token");
        Assert.Equal(HttpStatusCode.BadRequest, invalidTaxIdentifierResponse.StatusCode);

        var otherUserId = await RegisterAndAuthenticateAsync(factory, otherClient, "billing-other@example.com");
        using var noFarmSelected = await SendPurchaseAsync(
            otherClient,
            await GetAntiforgeryTokenAsync(otherClient),
            "monthly",
            "12345678909",
            "card-token");
        Assert.Equal(HttpStatusCode.Conflict, noFarmSelected.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var otherUser = await dbContext.Users.SingleAsync(candidate => candidate.Id == otherUserId);
            otherUser.SelectedBreedingFarmId = farmId;
            dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
                farmId,
                otherUserId,
                BreedingFarmRole.Manager,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var managerResponse = await SendPurchaseAsync(
            otherClient,
            await GetAntiforgeryTokenAsync(otherClient),
            "monthly",
            "12345678909",
            "card-token");
        Assert.Equal(HttpStatusCode.NotFound, managerResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var membership = await dbContext.BreedingFarmUsers.SingleAsync(candidate =>
                candidate.BreedingFarmId == farmId && candidate.UserId == otherUserId);
            var entry = dbContext.Entry(membership);
            entry.Property(candidate => candidate.Role).CurrentValue = BreedingFarmRole.Owner;
            entry.Property(candidate => candidate.IsActive).CurrentValue = false;
            await dbContext.SaveChangesAsync();
        }

        using var inactiveOwnerResponse = await SendPurchaseAsync(
            otherClient,
            await GetAntiforgeryTokenAsync(otherClient),
            "monthly",
            "12345678909",
            "card-token");
        Assert.Equal(HttpStatusCode.NotFound, inactiveOwnerResponse.StatusCode);
        Assert.Empty(gateway.SubscriptionRequests);
        Assert.Empty(gateway.CustomerRequests);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await verificationDb.Subscriptions.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentPurchasesShareOnePendingSubscriptionAndGatewayReference()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway(delay: TimeSpan.FromMilliseconds(100));
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "billing-concurrent@example.com");
        var farmId = await CreateFarmAsync(client, "Concurrent Farm");
        await SelectFarmAsync(client, farmId);
        var token = await GetAntiforgeryTokenAsync(client);

        var results = await Task.WhenAll(
            SendPurchaseAsync(client, token, "monthly", "12345678909", "first-card-token"),
            SendPurchaseAsync(client, token, "monthly", "12345678909", "second-card-token"));
        using var firstResponse = results[0];
        using var secondResponse = results[1];

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var firstBody = JsonDocument.Parse(await firstResponse.Content.ReadAsStreamAsync());
        using var secondBody = JsonDocument.Parse(await secondResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            firstBody.RootElement.GetProperty("subscriptionId").GetGuid(),
            secondBody.RootElement.GetProperty("subscriptionId").GetGuid());
        Assert.Equal(2, gateway.SubscriptionRequests.Count);
        Assert.Single(gateway.SubscriptionRequests.Select(request => request.SubscriptionId).Distinct());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Single(await dbContext.Subscriptions.Where(candidate => candidate.BreedingFarmId == farmId).ToListAsync());
    }

    [Fact]
    public async Task CancelSubscription_PreservesIdentityAndHistoryAndAllowsNewContract()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var userId = await RegisterAndAuthenticateAsync(factory, client, "billing-cancel@example.com");
        var farmId = await CreateFarmAsync(client, "Cancellation Farm");
        await SelectFarmAsync(client, farmId);

        using var purchase = await SendPurchaseAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "monthly",
            "12345678909",
            "first-card-token");
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
        using var purchaseBody = JsonDocument.Parse(await purchase.Content.ReadAsStreamAsync());
        var firstSubscriptionId = purchaseBody.RootElement.GetProperty("subscriptionId").GetGuid();

        using var cancellation = await SendCancellationAsync(client, await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, cancellation.StatusCode);
        Assert.Equal((firstSubscriptionId, $"gateway-{firstSubscriptionId:N}"), Assert.Single(gateway.CancellationRequests));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var subscription = await dbContext.Subscriptions.SingleAsync(candidate => candidate.Id == firstSubscriptionId);
            Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
            Assert.Null(subscription.NextChargeDueAtUtc);
            Assert.NotNull(subscription.TrialStartedAtUtc);
            Assert.NotNull(subscription.TrialEndsAtUtc);
            Assert.True(await dbContext.Users.AnyAsync(candidate => candidate.Id == userId));
            Assert.True(await dbContext.BreedingFarms.AnyAsync(candidate => candidate.Id == farmId));
            Assert.True(await dbContext.BreedingFarmUsers.AnyAsync(candidate =>
                candidate.BreedingFarmId == farmId && candidate.UserId == userId && candidate.IsActive));
        }

        using var retryCancellation = await SendCancellationAsync(client, await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, retryCancellation.StatusCode);
        Assert.Single(gateway.CancellationRequests);

        using var renewal = await SendPurchaseAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "monthly",
            "12345678909",
            "renewed-card-token");
        Assert.Equal(HttpStatusCode.OK, renewal.StatusCode);
        using var renewalBody = JsonDocument.Parse(await renewal.Content.ReadAsStreamAsync());
        var renewedSubscriptionId = renewalBody.RootElement.GetProperty("subscriptionId").GetGuid();
        Assert.NotEqual(firstSubscriptionId, renewedSubscriptionId);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(2, await verificationDb.Subscriptions.CountAsync(candidate => candidate.BreedingFarmId == farmId));
        Assert.Equal(
            SubscriptionStatus.Cancelled,
            (await verificationDb.Subscriptions.SingleAsync(candidate => candidate.Id == firstSubscriptionId)).Status);
        Assert.Equal(
            SubscriptionStatus.Trial,
            (await verificationDb.Subscriptions.SingleAsync(candidate => candidate.Id == renewedSubscriptionId)).Status);
    }

    [Fact]
    public async Task CancelSubscription_RequiresOwnerAndRejectsPendingPurchase()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway { SubscriptionStatus = "PENDING" };
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);

        using var unauthenticated = await SendCancellationAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var ownerId = await RegisterAndAuthenticateAsync(factory, ownerClient, "billing-cancel-owner@example.com");
        using var noFarm = await SendCancellationAsync(ownerClient, await GetAntiforgeryTokenAsync(ownerClient));
        Assert.Equal(HttpStatusCode.Conflict, noFarm.StatusCode);

        var farmId = await CreateFarmAsync(ownerClient, "Pending Cancellation Farm");
        await SelectFarmAsync(ownerClient, farmId);
        using var purchase = await SendPurchaseAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "monthly",
            "12345678909",
            "pending-card-token");
        Assert.Equal(HttpStatusCode.Accepted, purchase.StatusCode);

        var otherUserId = await RegisterAndAuthenticateAsync(factory, otherClient, "billing-cancel-manager@example.com");
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var otherUser = await dbContext.Users.SingleAsync(candidate => candidate.Id == otherUserId);
            otherUser.SelectedBreedingFarmId = farmId;
            dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
                farmId,
                otherUserId,
                BreedingFarmRole.Manager,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var notOwner = await SendCancellationAsync(otherClient, await GetAntiforgeryTokenAsync(otherClient));
        Assert.Equal(HttpStatusCode.NotFound, notOwner.StatusCode);
        Assert.Empty(gateway.CancellationRequests);

        using var pending = await SendCancellationAsync(ownerClient, await GetAntiforgeryTokenAsync(ownerClient));
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        Assert.Empty(gateway.CancellationRequests);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(SubscriptionStatus.PendingSubscription, (await verificationDb.Subscriptions.SingleAsync()).Status);
        Assert.True(await verificationDb.Users.AnyAsync(candidate => candidate.Id == ownerId));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        X509Certificate2 certificate,
        RecordingBillingGateway gateway) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CriatorioVirtual"] = connectionString,
                ["Billing:Plans:Standard:MonthlyAmount"] = "19.90",
                ["Billing:Plans:Standard:AnnualAmount"] = "199.90",
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddInfrastructurePersistence(connectionString, certificate);
                services.RemoveAll<IBillingGateway>();
                services.AddSingleton<IBillingGateway>(gateway);
                services.AddSingleton<IStartupFilter, LoopbackRemoteIpStartupFilter>();
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task<HttpResponseMessage> SendPurchaseAsync(
        HttpClient client,
        string antiforgeryToken,
        string billingCycle,
        string taxIdentifier,
        string cardToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new
            {
                billingCycle,
                customerTaxIdentifier = taxIdentifier,
                cardToken
            })
        };
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendCancellationAsync(HttpClient client, string antiforgeryToken) =>
        client.SendAsync(CreateBrowserRequest(HttpMethod.Delete, Route, antiforgeryToken));

    private static async Task<Guid> RegisterAndAuthenticateAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email)
    {
        using var registration = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/register",
            await GetAntiforgeryTokenAsync(client),
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
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("breedingFarmId").GetGuid();
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

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private sealed class RecordingBillingGateway(TimeSpan? delay = null) : IBillingGateway
    {
        private readonly ConcurrentDictionary<Guid, BillingGatewaySubscription> _subscriptions = new();
        private readonly TimeSpan _delay = delay ?? TimeSpan.Zero;

        public ConcurrentQueue<BillingGatewayCustomerRequest> CustomerRequests { get; } = new();

        public ConcurrentQueue<BillingGatewaySubscriptionRequest> SubscriptionRequests { get; } = new();

        public ConcurrentQueue<(Guid SubscriptionId, string GatewaySubscriptionId)> CancellationRequests { get; } = new();

        public string SubscriptionStatus { get; set; } = "ACTIVE";

        public Task<BillingGatewayCustomer> GetOrCreateCustomerAsync(
            BillingGatewayCustomerRequest request,
            CancellationToken cancellationToken = default)
        {
            CustomerRequests.Enqueue(request);
            return Task.FromResult(new BillingGatewayCustomer($"customer-{request.BreedingFarmId:N}"));
        }

        public async Task<BillingGatewaySubscription> GetOrCreateSubscriptionAsync(
            BillingGatewaySubscriptionRequest request,
            CancellationToken cancellationToken = default)
        {
            SubscriptionRequests.Enqueue(request);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            var subscription = _subscriptions.GetOrAdd(request.SubscriptionId, _ => new BillingGatewaySubscription(
                $"gateway-{request.SubscriptionId:N}",
                request.CustomerId,
                request.SubscriptionId.ToString("D"),
                SubscriptionStatus,
                request.BillingCycle,
                request.Amount,
                request.FirstChargeDate));
            return subscription with { Status = SubscriptionStatus };
        }

        public Task<BillingGatewaySubscription?> FindSubscriptionAsync(
            Guid subscriptionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_subscriptions.GetValueOrDefault(subscriptionId));

        public Task<BillingGatewaySubscription?> GetSubscriptionAsync(
            string gatewaySubscriptionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_subscriptions.Values.SingleOrDefault(
                subscription => subscription.Id == gatewaySubscriptionId));

        public Task CancelSubscriptionAsync(
            Guid subscriptionId,
            string gatewaySubscriptionId,
            CancellationToken cancellationToken = default)
        {
            CancellationRequests.Enqueue((subscriptionId, gatewaySubscriptionId));
            _subscriptions.TryRemove(subscriptionId, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class LoopbackRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextRequest) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                await nextRequest();
            });
            next(app);
        };
    }

}
