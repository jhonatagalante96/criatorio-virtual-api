using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Api.Controllers;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class AsaasWebhookEndpointTests
{
    private const string Route = "/api/webhooks/asaas";
    private const string WebhookToken = "test-asaas-webhook-secret-0123456789-abcdef";
    private static readonly DateTimeOffset TrialStartedAtUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WebhookIsPublicAuthenticatedDurableAndIdempotent_AndRejectsInvalidRequestsWithoutEffects()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var logProvider = new CapturingLoggerProvider();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, logProvider);
        await MigrateAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        const string payload = """
            {"id":"evt_7f345","event":"PAYMENT_RECEIVED","dateCreated":"2026-09-08 12:00:00","payment":{"id":"pay_258","customer":"cus_missing","subscription":"sub_missing","value":19.90,"dueDate":"2026-09-08"}}
            """;

        using (var response = await SendWebhookAsync(client, payload, WebhookToken))
        {
            Assert.True(
                response.StatusCode == HttpStatusCode.Accepted,
                $"Expected 202 but received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        using (var response = await SendWebhookAsync(client, payload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var concurrentRetries = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => SendWebhookAsync(client, payload, WebhookToken)));
        foreach (var response in concurrentRetries)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var receivedEvent = await dbContext.AsaasWebhookEvents.SingleAsync();
            Assert.Equal("evt_7f345", receivedEvent.ProviderEventId);
            Assert.Equal("PAYMENT_RECEIVED", receivedEvent.EventType);
            using var persistedPayload = JsonDocument.Parse(receivedEvent.Payload);
            Assert.Equal("pay_258", persistedPayload.RootElement.GetProperty("payment").GetProperty("id").GetString());
            Assert.Empty(dbContext.Subscriptions);
            Assert.Empty(dbContext.Payments);
        }

        using (var response = await SendWebhookAsync(client, payload, "attacker-supplied-token"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var response = await SendWebhookAsync(client, "{not-json", WebhookToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var response = await SendWebhookAsync(client, "{\"event\":\"PAYMENT_RECEIVED\"}", WebhookToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var response = await SendWebhookAsync(
                   client,
                   new string('x', AsaasWebhookController.MaximumPayloadSizeBytes + 1),
                   WebhookToken))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Post, Route)
               {
                   Content = new StringContent("{}", Encoding.UTF8, "text/plain")
               })
        {
            request.Headers.Add("asaas-access-token", WebhookToken);
            using var response = await client.SendAsync(request);
            Assert.True(
                response.StatusCode == HttpStatusCode.UnsupportedMediaType,
                $"Expected 415 but received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        Assert.DoesNotContain(WebhookToken, string.Join(Environment.NewLine, logProvider.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain("attacker-supplied-token", string.Join(Environment.NewLine, logProvider.Messages), StringComparison.Ordinal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            Assert.Equal(1, dbContext.AsaasWebhookEvents.Count());
            Assert.Empty(dbContext.Subscriptions);
            Assert.Empty(dbContext.Payments);
        }
    }

    [Fact]
    public async Task WebhookProcessesBillingEventsAtomicallyAndIgnoresMismatchesAndStaleEvents()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, new CapturingLoggerProvider());
        await MigrateAsync(factory);

        var farmId = Guid.NewGuid();
        var pendingFarmId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var pendingSubscriptionId = Guid.NewGuid();
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            dbContext.BreedingFarms.AddRange(
                CreateFarm(farmId, "Primary farm"),
                CreateFarm(pendingFarmId, "Pending farm"));

            var subscription = new Subscription(
                subscriptionId,
                farmId,
                "standard",
                BillingCycle.Monthly,
                TrialStartedAtUtc,
                19.90m);
            subscription.ConfirmRecurringSubscription("cus_primary", "sub_primary", TrialStartedAtUtc);
            dbContext.Subscriptions.Add(subscription);
            dbContext.Subscriptions.Add(new Subscription(
                pendingSubscriptionId,
                pendingFarmId,
                "standard",
                BillingCycle.Monthly,
                TrialStartedAtUtc,
                19.90m));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var paymentCreatedPayload = JsonSerializer.Serialize(new
        {
            id = "evt_payment_created",
            @event = "PAYMENT_CREATED",
            dateCreated = "2026-09-08 12:01:00",
            payment = new
            {
                id = "pay_primary",
                customer = "cus_primary",
                subscription = "sub_primary",
                value = 19.90m,
                dueDate = "2026-09-08"
            }
        });
        var duplicateDeliveries = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => SendWebhookAsync(client, paymentCreatedPayload, WebhookToken)));
        foreach (var response in duplicateDeliveries)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }
        }

        var mismatchPayload = JsonSerializer.Serialize(new
        {
            id = "evt_customer_mismatch",
            @event = "PAYMENT_RECEIVED",
            dateCreated = "2026-09-08 12:02:00",
            payment = new
            {
                id = "pay_cross_tenant",
                customer = "cus_pending",
                subscription = "sub_primary",
                value = 19.90m,
                dueDate = "2026-09-08"
            }
        });
        using (var response = await SendWebhookAsync(client, mismatchPayload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var paidPayload = JsonSerializer.Serialize(new
        {
            id = "evt_payment_received",
            @event = "PAYMENT_RECEIVED",
            dateCreated = "2026-09-08 12:03:00",
            payment = new
            {
                id = "pay_primary",
                customer = "cus_primary",
                subscription = "sub_primary",
                value = 19.90m,
                dueDate = "2026-09-08"
            }
        });
        using (var response = await SendWebhookAsync(client, paidPayload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var staleFailurePayload = JsonSerializer.Serialize(new
        {
            id = "evt_old_overdue",
            @event = "PAYMENT_OVERDUE",
            dateCreated = "2026-09-08 12:02:00",
            payment = new
            {
                id = "pay_primary",
                customer = "cus_primary",
                subscription = "sub_primary",
                value = 19.90m,
                dueDate = "2026-09-08"
            }
        });
        using (var response = await SendWebhookAsync(client, staleFailurePayload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var inactivatedPayload = JsonSerializer.Serialize(new
        {
            id = "evt_subscription_inactivated",
            @event = "SUBSCRIPTION_INACTIVATED",
            dateCreated = "2026-09-09 12:00:00",
            subscription = new
            {
                id = "sub_primary",
                customer = "cus_primary",
                externalReference = subscriptionId.ToString("D"),
                status = "INACTIVE"
            }
        });
        using (var response = await SendWebhookAsync(client, inactivatedPayload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var latePaymentPayload = JsonSerializer.Serialize(new
        {
            id = "evt_payment_after_cancellation",
            @event = "PAYMENT_RECEIVED",
            dateCreated = "2026-09-10 12:00:00",
            payment = new
            {
                id = "pay_after_cancellation",
                customer = "cus_primary",
                subscription = "sub_primary",
                value = 19.90m,
                dueDate = "2026-09-08"
            }
        });
        using (var response = await SendWebhookAsync(client, latePaymentPayload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var checkoutPaidPayload = JsonSerializer.Serialize(new
        {
            id = "evt_checkout_paid",
            @event = "CHECKOUT_PAID",
            dateCreated = "2026-09-02 11:00:00",
            checkout = new
            {
                id = "checkout_pending",
                status = "PAID",
                customer = "cus_pending",
                callback = new { successUrl = "https://example.com/success" }
            }
        });
        using (var response = await SendWebhookAsync(client, checkoutPaidPayload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        await using (var preConfirmationScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = preConfirmationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            Assert.Equal(
                SubscriptionStatus.PendingSubscription,
                (await dbContext.Subscriptions.SingleAsync(candidate => candidate.Id == pendingSubscriptionId)).Status);
        }

        var subscriptionCreatedPayload = JsonSerializer.Serialize(new
        {
            id = "evt_second_subscription_created",
            @event = "SUBSCRIPTION_CREATED",
            dateCreated = "2026-09-02 12:00:00",
            subscription = new
            {
                id = "sub_pending",
                customer = "cus_pending",
                externalReference = pendingSubscriptionId.ToString("D"),
                status = "ACTIVE",
                billingType = "CREDIT_CARD"
            }
        });
        using (var response = await SendWebhookAsync(client, subscriptionCreatedPayload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var overduePayload = JsonSerializer.Serialize(new
        {
            id = "evt_pending_subscription_overdue",
            @event = "PAYMENT_OVERDUE",
            dateCreated = "2026-09-09 12:01:00",
            payment = new
            {
                id = "pay_pending",
                customer = "cus_pending",
                subscription = "sub_pending",
                value = 19.90m,
                dueDate = "2026-09-09"
            }
        });
        using (var response = await SendWebhookAsync(client, overduePayload, WebhookToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var paidSubscription = await verificationDb.Subscriptions.SingleAsync(candidate => candidate.Id == subscriptionId);
        Assert.Equal(SubscriptionStatus.Cancelled, paidSubscription.Status);
        var resolvedPayment = await verificationDb.Payments.SingleAsync(candidate => candidate.GatewayPaymentId == "pay_primary");
        Assert.Equal(PaymentStatus.Confirmed, resolvedPayment.Status);
        Assert.Equal(2, await verificationDb.Payments.CountAsync(candidate => candidate.BreedingFarmId == farmId));
        Assert.Equal(
            PaymentStatus.Confirmed,
            (await verificationDb.Payments.SingleAsync(candidate => candidate.GatewayPaymentId == "pay_after_cancellation")).Status);
        Assert.Equal(1, await verificationDb.Payments.CountAsync(candidate => candidate.BreedingFarmId == pendingFarmId));
        var gracePeriodSubscription = await verificationDb.Subscriptions.SingleAsync(candidate => candidate.Id == pendingSubscriptionId);
        Assert.Equal(SubscriptionStatus.GracePeriod, gracePeriodSubscription.Status);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 12, 1, 0, TimeSpan.Zero), gracePeriodSubscription.GracePeriodStartedAtUtc);
        Assert.Equal(
            gracePeriodSubscription.GracePeriodStartedAtUtc!.Value.AddDays(Subscription.GracePeriodDurationDays),
            gracePeriodSubscription.GracePeriodEndsAtUtc);
        Assert.Equal(PaymentStatus.Failed, (await verificationDb.Payments.SingleAsync(candidate => candidate.GatewayPaymentId == "pay_pending")).Status);
        Assert.Equal(9, await verificationDb.AsaasWebhookEvents.CountAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        X509Certificate2 certificate,
        CapturingLoggerProvider logProvider) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:CriatorioVirtual"] = connectionString,
                    ["Billing:Asaas:WebhookToken"] = WebhookToken,
                    ["Logging:EventLog:LogLevel:Default"] = "None"
                }));
            builder.ConfigureLogging(logging => logging.AddProvider(logProvider));
            builder.ConfigureServices(services =>
            {
                services.AddInfrastructurePersistence(connectionString, certificate);
            });
        });

    private static BreedingFarm CreateFarm(Guid id, string name) => new(
        id,
        TrialStartedAtUtc,
        name,
        "Owner",
        $"{name.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}@example.com",
        null,
        null,
        new BreedingFarmAddress(null, null, null, null, null, null, null));

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        string payload,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("asaas-access-token", token);
        return await client.SendAsync(request);
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
