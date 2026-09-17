using System.Net;
using System.Net.Http.Json;
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

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class BillingQueryEndpointTests
{
    [Fact]
    public async Task SubscriptionAndPaymentQueriesAreOwnerScopedAndPaymentsArePaginated()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var unauthenticatedClient = CreateClient(factory);

        using var unauthenticatedSubscription = await unauthenticatedClient.GetAsync("/api/billing/subscription");
        using var unauthenticatedPayments = await unauthenticatedClient.GetAsync("/api/billing/payments");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedSubscription.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedPayments.StatusCode);

        using var ownerClient = CreateClient(factory);
        var ownerId = await RegisterAndAuthenticateAsync(factory, ownerClient, "billing-owner@example.com");
        using var withoutFarm = await ownerClient.GetAsync("/api/billing/subscription");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);

        var ownerFarmId = await CreateFarmAsync(ownerClient, "Owner farm", "billing-owner@example.com");
        await SelectFarmAsync(ownerClient, ownerFarmId);
        using var withoutSubscription = await ownerClient.GetAsync("/api/billing/subscription");
        Assert.Equal(HttpStatusCode.NotFound, withoutSubscription.StatusCode);
        using var emptyPayments = await ownerClient.GetAsync("/api/billing/payments");
        Assert.Equal(HttpStatusCode.OK, emptyPayments.StatusCode);
        using (var emptyDocument = JsonDocument.Parse(await emptyPayments.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(0, emptyDocument.RootElement.GetProperty("totalCount").GetInt32());
        }

        var ownerPaymentIds = await SeedSubscriptionAsync(factory, ownerFarmId, "owner", gracePeriod: true, 3);
        using var subscriptionResponse = await ownerClient.GetAsync("/api/billing/subscription");
        Assert.Equal(HttpStatusCode.OK, subscriptionResponse.StatusCode);
        var subscriptionJson = await subscriptionResponse.Content.ReadAsStringAsync();
        using (var subscriptionDocument = JsonDocument.Parse(subscriptionJson))
        {
            var subscription = subscriptionDocument.RootElement;
            Assert.Equal(ownerFarmId, subscription.GetProperty("breedingFarmId").GetGuid());
            Assert.Equal("small-bird", subscription.GetProperty("planCode").GetString());
            Assert.Equal("Monthly", subscription.GetProperty("billingCycle").GetString());
            Assert.Equal("GracePeriod", subscription.GetProperty("status").GetString());
            Assert.Equal(
                subscription.GetProperty("trialEndsAtUtc").GetDateTimeOffset(),
                subscription.GetProperty("firstChargeDueAtUtc").GetDateTimeOffset());
            Assert.True(subscription.GetProperty("gracePeriodDaysRemaining").GetInt32() is > 0 and <= 3);
            Assert.False(subscriptionJson.Contains("owner-customer-secret", StringComparison.Ordinal));
            Assert.False(subscriptionJson.Contains("owner-subscription-secret", StringComparison.Ordinal));
        }

        using var otherClient = CreateClient(factory);
        var otherUserId = await RegisterAndAuthenticateAsync(factory, otherClient, "billing-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Other farm", "billing-other@example.com");
        await SelectFarmAsync(otherClient, otherFarmId);
        var otherPaymentIds = await SeedSubscriptionAsync(factory, otherFarmId, "other", gracePeriod: false, 1);

        using var firstPaymentPage = await ownerClient.GetAsync("/api/billing/payments?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, firstPaymentPage.StatusCode);
        var firstPageJson = await firstPaymentPage.Content.ReadAsStringAsync();
        using (var pageDocument = JsonDocument.Parse(firstPageJson))
        {
            var root = pageDocument.RootElement;
            Assert.Equal(ownerFarmId, root.GetProperty("breedingFarmId").GetGuid());
            Assert.Equal(3, root.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, root.GetProperty("items").GetArrayLength());
            Assert.Equal(ownerPaymentIds[0], root.GetProperty("items")[0].GetProperty("paymentId").GetGuid());
            Assert.Equal(ownerPaymentIds[1], root.GetProperty("items")[1].GetProperty("paymentId").GetGuid());
            Assert.DoesNotContain(
                root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("paymentId").GetGuid()),
                paymentId => otherPaymentIds.Contains(paymentId));
            Assert.False(firstPageJson.Contains("owner-payment-secret", StringComparison.Ordinal));
        }

        using var secondPaymentPage = await ownerClient.GetAsync("/api/billing/payments?page=2&pageSize=2");
        using (var pageDocument = JsonDocument.Parse(await secondPaymentPage.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(1, pageDocument.RootElement.GetProperty("items").GetArrayLength());
            Assert.Equal(ownerPaymentIds[2], pageDocument.RootElement.GetProperty("items")[0].GetProperty("paymentId").GetGuid());
        }

        using var invalidPageSize = await ownerClient.GetAsync("/api/billing/payments?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageSize.StatusCode);

        using var otherSubscriptionResponse = await otherClient.GetAsync("/api/billing/subscription");
        using (var otherSubscriptionDocument = JsonDocument.Parse(await otherSubscriptionResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(HttpStatusCode.OK, otherSubscriptionResponse.StatusCode);
            Assert.Equal(otherFarmId, otherSubscriptionDocument.RootElement.GetProperty("breedingFarmId").GetGuid());
        }

        await SetSelectedFarmWithoutOwnerAccessAsync(factory, otherUserId, ownerFarmId);
        using var forbiddenSubscription = await otherClient.GetAsync("/api/billing/subscription");
        using var forbiddenPayments = await otherClient.GetAsync("/api/billing/payments");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenSubscription.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenPayments.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) =>
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
        using var registration = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/register",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                email,
                password = "StrongPassword!123",
                confirmPassword = "StrongPassword!123"
            }));
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

    private static async Task<Guid> CreateFarmAsync(HttpClient client, string name, string contactEmail)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new { name, responsibleName = "Owner Principal", contactEmail }));
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

    private static async Task<Guid[]> SeedSubscriptionAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        string secretPrefix,
        bool gracePeriod,
        int paymentCount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var now = DateTimeOffset.UtcNow;
        var trialStartedAtUtc = gracePeriod ? now.AddDays(-12) : now.AddDays(-2);
        var subscription = new Subscription(
            Guid.NewGuid(),
            farmId,
            "small-bird",
            BillingCycle.Monthly,
            trialStartedAtUtc);
        subscription.ConfirmRecurringSubscription(
            $"{secretPrefix}-customer-secret",
            $"{secretPrefix}-subscription-secret",
            trialStartedAtUtc);
        if (gracePeriod)
        {
            subscription.StartGracePeriod(subscription.TrialEndsAtUtc!.Value);
        }

        var dueDates = new[] { now.AddDays(5), now.AddDays(-5), now.AddDays(-20) };
        var payments = Enumerable.Range(0, paymentCount)
            .Select(index => new Payment(
                Guid.NewGuid(),
                farmId,
                subscription.Id,
                $"{secretPrefix}-payment-secret-{index}",
                119.50m,
                "BRL",
                dueDates[index],
                now.AddSeconds(index)))
            .ToArray();
        dbContext.Subscriptions.Add(subscription);
        dbContext.Payments.AddRange(payments);
        await dbContext.SaveChangesAsync();
        return payments
            .OrderByDescending(payment => payment.DueAtUtc)
            .ThenByDescending(payment => payment.Id)
            .Select(payment => payment.Id)
            .ToArray();
    }

    private static async Task SetSelectedFarmWithoutOwnerAccessAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        Guid farmId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId);
        user.SelectedBreedingFarmId = farmId;
        dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
            farmId,
            userId,
            BreedingFarmRole.Viewer,
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
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
