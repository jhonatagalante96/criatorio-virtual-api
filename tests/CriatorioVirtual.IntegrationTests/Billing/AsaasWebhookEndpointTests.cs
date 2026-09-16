using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Api.Controllers;
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
            {"id":"evt_7f345","event":"PAYMENT_RECEIVED","payment":{"id":"pay_258"}}
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
