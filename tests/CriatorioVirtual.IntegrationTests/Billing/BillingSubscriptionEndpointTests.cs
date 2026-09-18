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
using CriatorioVirtual.Infrastructure.Billing;
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
    private const string CheckoutRoute = "/api/billing/subscription-checkouts";
    private const string PaymentRoute = "/api/billing/payments";
    private const string WebhookToken = "test-asaas-webhook-secret-0123456789-abcdef";

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
    public async Task BillingPaymentAttempt_IsTenantScopedIdempotentAndDoesNotGrantAccessBeforeWebhook()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        var ownerId = await RegisterAndAuthenticateAsync(factory, ownerClient, "billing-payment-owner@example.com");
        var farmId = await CreateFarmAsync(ownerClient, "Payment Regularization Farm");
        await SelectFarmAsync(ownerClient, farmId);

        using var purchase = await SendPurchaseAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "monthly",
            "12345678909",
            "initial-card-token");
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);

        Guid paymentId;
        string gatewayPaymentId = "pay-v0-066";
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var subscription = await dbContext.Subscriptions.SingleAsync(candidate => candidate.BreedingFarmId == farmId);
            var dueAtUtc = subscription.NextChargeDueAtUtc!.Value;
            var failedAtUtc = dueAtUtc.AddDays(1);
            subscription.StartGracePeriod(failedAtUtc);
            Assert.True(subscription.TryBlockAfterGracePeriodExpiration(failedAtUtc.AddDays(7)));
            var paymentDueAtUtc = new DateTimeOffset(
                dueAtUtc.Year,
                dueAtUtc.Month,
                dueAtUtc.Day,
                0,
                0,
                0,
                TimeSpan.Zero);
            var payment = new Payment(
                Guid.NewGuid(),
                farmId,
                subscription.Id,
                gatewayPaymentId,
                subscription.AgreedAmount!.Value,
                "BRL",
                paymentDueAtUtc,
                DateTimeOffset.UtcNow);
            dbContext.Payments.Add(payment);
            await dbContext.SaveChangesAsync();
            paymentId = payment.Id;
            gateway.SetPayment(new BillingGatewayPayment(
                gatewayPaymentId,
                subscription.GatewayCustomerId!,
                subscription.GatewaySubscriptionId!,
                payment.Amount,
                DateOnly.FromDateTime(dueAtUtc.UtcDateTime),
                "OVERDUE"));
        }

        var otherUserId = await RegisterAndAuthenticateAsync(factory, otherClient, "billing-payment-manager@example.com");
        _ = await CreateFarmAsync(otherClient, "Other Payment Farm");
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

        var idempotencyKey = Guid.NewGuid();
        using var notOwner = await SendPaymentAttemptAsync(
            otherClient,
            await GetAntiforgeryTokenAsync(otherClient),
            paymentId,
            idempotencyKey,
            "manager-card-token");
        Assert.Equal(HttpStatusCode.NotFound, notOwner.StatusCode);
        Assert.Empty(gateway.PaymentRequests);

        var antiforgeryToken = await GetAntiforgeryTokenAsync(ownerClient);
        var validGatewayPayment = await gateway.GetPaymentAsync(gatewayPaymentId);
        gateway.SetPayment(validGatewayPayment! with { CustomerId = "customer-from-another-farm" });
        using var providerMismatch = await SendPaymentAttemptAsync(
            ownerClient,
            antiforgeryToken,
            paymentId,
            Guid.NewGuid(),
            "mismatch-card-token");
        Assert.Equal(HttpStatusCode.Conflict, providerMismatch.StatusCode);
        Assert.Empty(gateway.PaymentRequests);
        gateway.SetPayment(validGatewayPayment!);

        var concurrentAttempts = await Task.WhenAll(
            SendPaymentAttemptAsync(
                ownerClient,
                antiforgeryToken,
                paymentId,
                idempotencyKey,
                "regularization-card-token"),
            SendPaymentAttemptAsync(
                ownerClient,
                antiforgeryToken,
                paymentId,
                idempotencyKey,
                "regularization-card-token"));
        using var firstAttempt = concurrentAttempts[0];
        using var replay = concurrentAttempts[1];
        Assert.True(firstAttempt.StatusCode == HttpStatusCode.Accepted,
            $"First attempt returned {(int)firstAttempt.StatusCode}: {await firstAttempt.Content.ReadAsStringAsync()}");
        Assert.True(replay.StatusCode == HttpStatusCode.Accepted,
            $"Concurrent replay returned {(int)replay.StatusCode}: {await replay.Content.ReadAsStringAsync()}");
        using (var body = JsonDocument.Parse(await firstAttempt.Content.ReadAsStreamAsync()))
        {
            Assert.Equal("awaitingConfirmation", body.RootElement.GetProperty("attemptStatus").GetString());
        }

        using var changedRequest = await SendPaymentAttemptAsync(
            ownerClient,
            antiforgeryToken,
            paymentId,
            idempotencyKey,
            "different-card-token");
        Assert.Equal(HttpStatusCode.Conflict, changedRequest.StatusCode);
        Assert.Single(gateway.PaymentRequests);
        Assert.Equal("regularization-card-token", Assert.Single(gateway.PaymentRequests).CardToken);

        await using (var preConfirmationScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = preConfirmationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            Assert.Equal(SubscriptionStatus.Blocked,
                (await dbContext.Subscriptions.SingleAsync(candidate => candidate.BreedingFarmId == farmId)).Status);
            Assert.Equal(PaymentStatus.Pending,
                (await dbContext.Payments.SingleAsync(candidate => candidate.Id == paymentId)).Status);
        }

        string expectedCustomerId;
        string expectedSubscriptionId;
        DateTimeOffset paidAtUtc;
        DateOnly dueDate;
        decimal amount;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var subscription = await dbContext.Subscriptions.SingleAsync(candidate => candidate.BreedingFarmId == farmId);
            var payment = await dbContext.Payments.SingleAsync(candidate => candidate.Id == paymentId);
            expectedCustomerId = subscription.GatewayCustomerId!;
            expectedSubscriptionId = subscription.GatewaySubscriptionId!;
            paidAtUtc = subscription.GracePeriodEndsAtUtc!.Value.AddDays(1);
            dueDate = DateOnly.FromDateTime(payment.DueAtUtc.UtcDateTime);
            amount = payment.Amount;
        }

        var paidPayload = JsonSerializer.Serialize(new
        {
            id = $"evt_regularization_{paymentId:N}",
            @event = "PAYMENT_RECEIVED",
            dateCreated = paidAtUtc.ToString("O"),
            payment = new
            {
                id = gatewayPaymentId,
                customer = expectedCustomerId,
                subscription = expectedSubscriptionId,
                value = amount,
                dueDate = dueDate.ToString("yyyy-MM-dd")
            }
        });
        using (var webhookResponse = await SendWebhookAsync(ownerClient, paidPayload))
        {
            Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);
        }

        await using (var eventScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = eventScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var eventRecord = await dbContext.AsaasWebhookEvents.SingleAsync(candidate =>
                candidate.ProviderEventId == $"evt_regularization_{paymentId:N}");
            var processor = eventScope.ServiceProvider.GetRequiredService<IAsaasWebhookEventProcessingService>();
            var outcome = await processor.ProcessAsync(eventRecord.Id, ignoreRetryDelay: true, CancellationToken.None);
            Assert.True(outcome is AsaasWebhookEventProcessingOutcome.Processed or AsaasWebhookEventProcessingOutcome.AlreadyProcessed);
        }

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(SubscriptionStatus.Active,
            (await verificationDb.Subscriptions.SingleAsync(candidate => candidate.BreedingFarmId == farmId)).Status);
        Assert.Equal(PaymentStatus.Confirmed,
            (await verificationDb.Payments.SingleAsync(candidate => candidate.Id == paymentId)).Status);
        var attempts = await verificationDb.PaymentAttempts.Where(candidate => candidate.PaymentId == paymentId).ToArrayAsync();
        Assert.Equal(2, attempts.Length);
        Assert.Contains(attempts, candidate => candidate.Status == PaymentAttemptStatus.Failed);
        var attempt = Assert.Single(attempts, candidate => candidate.Status == PaymentAttemptStatus.AwaitingConfirmation);
        Assert.DoesNotContain("regularization-card-token", attempt.RequestFingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(PaymentAttempt).GetProperties().Select(property => property.Name),
            propertyName => propertyName.Contains("CardToken", StringComparison.OrdinalIgnoreCase));
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
        _ = await CreateFarmAsync(otherClient, "Unrelated Owner Farm");
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var otherUser = await dbContext.Users.SingleAsync(candidate => candidate.Id == otherUserId);
            otherUser.SelectedBreedingFarmId = farmId;
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
                ["Billing:Asaas:WebhookToken"] = WebhookToken,
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

    [Fact]
    public async Task OwnerCreatesHostedSubscriptionCheckoutAndRetriesWithoutCreatingAnother()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "billing-checkout@example.com");

        var farmId = await CreateFarmAsync(client, "Hosted Checkout Farm");
        await SelectFarmAsync(client, farmId);
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var concurrentResponses = await Task.WhenAll(
            SendCheckoutAsync(client, antiforgeryToken, "monthly", "123.456.789-09"),
            SendCheckoutAsync(client, antiforgeryToken, "monthly", "123.456.789-09"));
        using var response = concurrentResponses[0];
        using var concurrentResponse = concurrentResponses[1];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, concurrentResponse.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        using var concurrentBody = JsonDocument.Parse(await concurrentResponse.Content.ReadAsStreamAsync());
        var subscriptionId = body.RootElement.GetProperty("subscriptionId").GetGuid();
        var checkoutId = body.RootElement.GetProperty("checkoutId").GetString();
        Assert.Equal("pendingCheckout", body.RootElement.GetProperty("status").GetString());
        Assert.StartsWith("https://sandbox.asaas.com/checkoutSession/", body.RootElement.GetProperty("checkoutUrl").GetString());
        Assert.True(body.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal(checkoutId, concurrentBody.RootElement.GetProperty("checkoutId").GetString());
        Assert.Equal(subscriptionId, concurrentBody.RootElement.GetProperty("subscriptionId").GetGuid());

        using var retry = await SendCheckoutAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "monthly",
            "12345678909");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var retryBody = JsonDocument.Parse(await retry.Content.ReadAsStreamAsync());
        Assert.Equal(checkoutId, retryBody.RootElement.GetProperty("checkoutId").GetString());
        Assert.Equal(body.RootElement.GetProperty("checkoutUrl").GetString(), retryBody.RootElement.GetProperty("checkoutUrl").GetString());
        Assert.Single(gateway.CheckoutRequests);
        Assert.Empty(gateway.SubscriptionRequests);
        Assert.Equal("12345678909", gateway.CustomerRequests.Single().TaxIdentifier);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var subscription = await verificationDb.Subscriptions.SingleAsync(candidate => candidate.Id == subscriptionId);
        Assert.Equal(farmId, subscription.BreedingFarmId);
        Assert.Equal(SubscriptionStatus.PendingSubscription, subscription.Status);
        Assert.Equal("ACTIVE", subscription.GatewayCheckoutStatus);
        Assert.NotNull(subscription.GatewayCheckoutId);
        Assert.Null(subscription.TrialStartedAtUtc);
        Assert.Equal(
            DateOnly.FromDateTime(subscription.CreatedAtUtc.UtcDateTime).AddDays(Subscription.TrialDurationDays),
            gateway.CheckoutRequests.Single().FirstChargeDate);
    }

    [Fact]
    public async Task HostedSubscriptionCheckoutRequiresOwnerOfSelectedFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);

        await RegisterAndAuthenticateAsync(factory, ownerClient, "billing-checkout-owner@example.com");
        var farmId = await CreateFarmAsync(ownerClient, "Checkout Owner Farm");
        await SelectFarmAsync(ownerClient, farmId);
        var otherUserId = await RegisterAndAuthenticateAsync(factory, otherClient, "billing-checkout-other@example.com");
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

        using var response = await SendCheckoutAsync(
            otherClient,
            await GetAntiforgeryTokenAsync(otherClient),
            "monthly",
            "12345678909");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(gateway.CustomerRequests);
        Assert.Empty(gateway.CheckoutRequests);
    }

    [Fact]
    public async Task HostedSubscriptionCheckoutRejectsInvalidCycleAndTaxIdentifierBeforeGatewayCalls()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var gateway = new RecordingBillingGateway();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, gateway);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "billing-checkout-validation@example.com");

        var farmId = await CreateFarmAsync(client, "Checkout Validation Farm");
        await SelectFarmAsync(client, farmId);

        using var invalidCycle = await SendCheckoutAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "weekly",
            "12345678909");
        using var invalidTaxIdentifier = await SendCheckoutAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "monthly",
            "not-a-cpf");

        Assert.Equal(HttpStatusCode.BadRequest, invalidCycle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTaxIdentifier.StatusCode);
        Assert.Empty(gateway.CustomerRequests);
        Assert.Empty(gateway.CheckoutRequests);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await verificationDb.Subscriptions.Where(candidate => candidate.BreedingFarmId == farmId).ToListAsync());
    }

    private static async Task<HttpResponseMessage> SendCheckoutAsync(
        HttpClient client,
        string antiforgeryToken,
        string billingCycle,
        string taxIdentifier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CheckoutRoute)
        {
            Content = JsonContent.Create(new
            {
                billingCycle,
                customerTaxIdentifier = taxIdentifier
            })
        };
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendCancellationAsync(HttpClient client, string antiforgeryToken) =>
        client.SendAsync(CreateBrowserRequest(HttpMethod.Delete, Route, antiforgeryToken));

    private static async Task<HttpResponseMessage> SendWebhookAsync(HttpClient client, string payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/asaas")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("asaas-access-token", WebhookToken);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendPaymentAttemptAsync(
        HttpClient client,
        string antiforgeryToken,
        Guid paymentId,
        Guid idempotencyKey,
        string cardToken)
    {
        var request = CreateBrowserRequest(
            HttpMethod.Post,
            $"{PaymentRoute}/{paymentId:D}/attempts",
            antiforgeryToken,
            new { cardToken });
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString("D"));
        return client.SendAsync(request);
    }

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
        private readonly ConcurrentDictionary<Guid, BillingGatewayCheckout> _checkouts = new();
        private readonly TimeSpan _delay = delay ?? TimeSpan.Zero;

        public ConcurrentQueue<BillingGatewayCustomerRequest> CustomerRequests { get; } = new();

        public ConcurrentQueue<BillingGatewaySubscriptionRequest> SubscriptionRequests { get; } = new();

        public ConcurrentQueue<BillingGatewayCheckoutRequest> CheckoutRequests { get; } = new();

        public ConcurrentQueue<(Guid SubscriptionId, string GatewaySubscriptionId)> CancellationRequests { get; } = new();

        public ConcurrentQueue<BillingGatewayPaymentRequest> PaymentRequests { get; } = new();

        private readonly ConcurrentDictionary<string, BillingGatewayPayment> _payments = new(StringComparer.Ordinal);

        public string SubscriptionStatus { get; set; } = "ACTIVE";

        public string PaymentAfterAttemptStatus { get; set; } = "PENDING";

        public void SetPayment(BillingGatewayPayment payment) => _payments[payment.Id] = payment;

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

        public Task<BillingGatewayCheckout> CreateSubscriptionCheckoutAsync(
            BillingGatewayCheckoutRequest request,
            CancellationToken cancellationToken = default)
        {
            CheckoutRequests.Enqueue(request);
            return Task.FromResult(_checkouts.GetOrAdd(request.SubscriptionId, _ => new BillingGatewayCheckout(
                $"checkout-{request.SubscriptionId:N}",
                $"https://sandbox.asaas.com/checkoutSession/show/{request.SubscriptionId:D}",
                "ACTIVE",
                request.ExpiresAtUtc,
                request.SubscriptionId.ToString("D"),
                request.CustomerId,
                request.BillingCycle,
                request.Amount,
                request.FirstChargeDate)));
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

        public Task<BillingGatewayPayment?> GetPaymentAsync(
            string gatewayPaymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_payments.GetValueOrDefault(gatewayPaymentId));

        public Task<BillingGatewayPayment> PayPaymentWithCreditCardAsync(
            BillingGatewayPaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            PaymentRequests.Enqueue(request);
            var current = _payments[request.PaymentId];
            var afterAttempt = current with { Status = PaymentAfterAttemptStatus };
            _payments[request.PaymentId] = afterAttempt;
            return Task.FromResult(afterAttempt);
        }

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
