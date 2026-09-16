using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class AsaasBillingGatewayTests
{
    private static readonly DateOnly FirstChargeDate = new(2026, 10, 1);

    [Fact]
    public async Task GetOrCreateSubscription_SendsRecurringTokenizedCardRequestAndStableReference()
    {
        var handler = new AsaasStubHandler();
        var gateway = CreateGateway(handler);
        var request = CreateSubscriptionRequest();

        var result = await gateway.GetOrCreateSubscriptionAsync(request);

        Assert.Equal("sub-1", result.Id);
        Assert.Equal(request.SubscriptionId.ToString("D"), result.ExternalReference);
        Assert.Equal(1, handler.SubscriptionPostCount);

        var post = Assert.Single(handler.Requests, item => item.Method == HttpMethod.Post && item.Path == "/v3/subscriptions");
        Assert.Equal("sandbox-test-key", post.AccessToken);
        using var payload = JsonDocument.Parse(post.Body!);
        var root = payload.RootElement;
        Assert.Equal(request.CustomerId, root.GetProperty("customer").GetString());
        Assert.Equal("CREDIT_CARD", root.GetProperty("billingType").GetString());
        Assert.Equal(19.90m, root.GetProperty("value").GetDecimal());
        Assert.Equal("2026-10-01", root.GetProperty("nextDueDate").GetString());
        Assert.Equal("YEARLY", root.GetProperty("cycle").GetString());
        Assert.Equal(request.SubscriptionId.ToString("D"), root.GetProperty("externalReference").GetString());
        Assert.Equal("asaas-card-token", root.GetProperty("creditCardToken").GetString());
        Assert.Equal("203.0.113.42", root.GetProperty("remoteIp").GetString());
        Assert.False(root.TryGetProperty("creditCard", out _));
        Assert.False(root.TryGetProperty("creditCardHolderInfo", out _));
        Assert.False(root.TryGetProperty("callback", out _));
    }

    [Fact]
    public async Task RepeatedSubscriptionRequest_ReconcilesByExternalReferenceWithoutAnotherPost()
    {
        var handler = new AsaasStubHandler();
        var gateway = CreateGateway(handler);
        var request = CreateSubscriptionRequest();

        var first = await gateway.GetOrCreateSubscriptionAsync(request);
        var second = await gateway.GetOrCreateSubscriptionAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, handler.SubscriptionPostCount);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task TimeoutAfterRemoteCreation_ReconcilesAndDoesNotCreateASecondSubscription()
    {
        var handler = new AsaasStubHandler { TimeoutAfterFirstSubscriptionCreate = true };
        var gateway = CreateGateway(handler);

        var result = await gateway.GetOrCreateSubscriptionAsync(CreateSubscriptionRequest());

        Assert.Equal("sub-1", result.Id);
        Assert.Equal(1, handler.SubscriptionPostCount);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task TimeoutWithoutAReconciledResult_ReportsUnknownOutcomeAndDoesNotAutoRetry()
    {
        var handler = new AsaasStubHandler { TimeoutBeforeSubscriptionCreation = true };
        var gateway = CreateGateway(handler);

        var exception = await Assert.ThrowsAsync<BillingGatewayOperationOutcomeUnknownException>(
            () => gateway.GetOrCreateSubscriptionAsync(CreateSubscriptionRequest()));

        Assert.Contains(CreateSubscriptionRequest().SubscriptionId.ToString("D"), exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.SubscriptionPostCount);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentRequestsForSameSubscription_CreateOnlyOneRemoteSubscription()
    {
        var handler = new AsaasStubHandler { DelaySubscriptionCreate = true };
        var gateway = CreateGateway(handler);
        var request = CreateSubscriptionRequest();

        var results = await Task.WhenAll(
            gateway.GetOrCreateSubscriptionAsync(request),
            gateway.GetOrCreateSubscriptionAsync(request));

        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(1, handler.SubscriptionPostCount);
    }

    [Fact]
    public async Task ConflictingExistingSubscription_FailsClosedWithoutCreatingAnother()
    {
        var handler = new AsaasStubHandler();
        var gateway = CreateGateway(handler);
        var request = CreateSubscriptionRequest();
        _ = await gateway.GetOrCreateSubscriptionAsync(request);

        var changedRequest = CreateSubscriptionRequest(amount: 29.90m);
        await Assert.ThrowsAsync<BillingGatewayIdempotencyConflictException>(
            () => gateway.GetOrCreateSubscriptionAsync(changedRequest));

        Assert.Equal(1, handler.SubscriptionPostCount);
    }

    [Fact]
    public async Task CancelSubscription_DeletesRemoteRecurrenceAndTreatsNotFoundAsIdempotentSuccess()
    {
        var handler = new AsaasStubHandler();
        var gateway = CreateGateway(handler);
        var request = CreateSubscriptionRequest();
        var created = await gateway.GetOrCreateSubscriptionAsync(request);

        await gateway.CancelSubscriptionAsync(request.SubscriptionId, created.Id);
        await gateway.CancelSubscriptionAsync(request.SubscriptionId, created.Id);

        var deletions = handler.Requests
            .Where(item => item.Method == HttpMethod.Delete)
            .ToArray();
        Assert.Equal(2, deletions.Length);
        Assert.All(deletions, deletion =>
        {
            Assert.Equal("/v3/subscriptions/sub-1", deletion.Path);
            Assert.Equal("sandbox-test-key", deletion.AccessToken);
            Assert.Null(deletion.Body);
        });
    }

    [Fact]
    public async Task GetOrCreateCustomer_ReusesAsaasCustomerByStableFarmReference()
    {
        var handler = new AsaasStubHandler();
        var gateway = CreateGateway(handler);
        var request = new BillingGatewayCustomerRequest(
            Guid.NewGuid(),
            "Criatório Teste",
            "12345678901",
            "owner@example.test",
            "+5511999999999");

        var first = await gateway.GetOrCreateCustomerAsync(request);
        var second = await gateway.GetOrCreateCustomerAsync(request);

        Assert.Equal("cus-1", first.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, handler.CustomerPostCount);
        var post = Assert.Single(handler.Requests, item => item.Method == HttpMethod.Post && item.Path == "/v3/customers");
        using var payload = JsonDocument.Parse(post.Body!);
        Assert.Equal(request.BreedingFarmId.ToString("D"), payload.RootElement.GetProperty("externalReference").GetString());
        Assert.Equal(request.TaxIdentifier, payload.RootElement.GetProperty("cpfCnpj").GetString());
    }

    [Fact]
    public void SubscriptionRequest_RedactsCardTokenAndAddressFromStringRepresentation()
    {
        var request = CreateSubscriptionRequest();

        Assert.DoesNotContain(request.CardToken, request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.42", request.ToString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", request.ToString(), StringComparison.Ordinal);
    }

    private static AsaasBillingGateway CreateGateway(AsaasStubHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(AsaasOptions.SandboxBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(60)
        };
        return new AsaasBillingGateway(
            httpClient,
            Options.Create(new AsaasOptions { ApiKey = "sandbox-test-key", BaseUrl = AsaasOptions.SandboxBaseUrl }),
            new AsaasOperationCoordinator());
    }

    private static BillingGatewaySubscriptionRequest CreateSubscriptionRequest(decimal amount = 19.90m) =>
        new(
            Guid.Parse("d714f0c4-1082-4e44-86b6-514d1bc17bc5"),
            "cus-existing",
            BillingCycle.Annual,
            amount,
            FirstChargeDate,
            "Plano anual Criatório Virtual",
            "asaas-card-token",
            IPAddress.Parse("203.0.113.42"));

    private sealed class AsaasStubHandler : HttpMessageHandler
    {
        private readonly object _sync = new();
        private AsaasSubscriptionStub? _subscription;
        private AsaasCustomerStub? _customer;
        private int _subscriptionPostCount;
        private int _customerPostCount;

        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        public bool TimeoutAfterFirstSubscriptionCreate { get; init; }

        public bool TimeoutBeforeSubscriptionCreation { get; init; }

        public bool DelaySubscriptionCreate { get; init; }

        public int SubscriptionPostCount => Volatile.Read(ref _subscriptionPostCount);

        public int CustomerPostCount => Volatile.Read(ref _customerPostCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(new CapturedRequest(
                request.Method,
                path,
                body,
                request.Headers.TryGetValues("access_token", out var values) ? values.Single() : null));

            if (path == "/v3/subscriptions" && request.Method == HttpMethod.Get)
            {
                return SubscriptionListResponse();
            }

            if (path == "/v3/subscriptions" && request.Method == HttpMethod.Post)
            {
                var postNumber = Interlocked.Increment(ref _subscriptionPostCount);
                if (DelaySubscriptionCreate)
                {
                    await Task.Delay(25, cancellationToken);
                }

                if (TimeoutBeforeSubscriptionCreation)
                {
                    throw new TaskCanceledException("Simulated timeout before Asaas persisted the subscription.");
                }

                using var payload = JsonDocument.Parse(body!);
                var json = payload.RootElement;
                var created = new AsaasSubscriptionStub(
                    "sub-1",
                    json.GetProperty("customer").GetString()!,
                    json.GetProperty("externalReference").GetString()!,
                    "ACTIVE",
                    json.GetProperty("cycle").GetString()!,
                    json.GetProperty("value").GetDecimal(),
                    json.GetProperty("nextDueDate").GetString()!);

                lock (_sync)
                {
                    _subscription = created;
                }

                if (TimeoutAfterFirstSubscriptionCreate && postNumber == 1)
                {
                    throw new TaskCanceledException("Simulated timeout after Asaas persisted the subscription.");
                }

                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(created));
            }

            if (request.Method == HttpMethod.Delete && path.StartsWith("/v3/subscriptions/", StringComparison.Ordinal))
            {
                lock (_sync)
                {
                    if (_subscription?.Id == path[("/v3/subscriptions/".Length)..])
                    {
                        _subscription = null;
                        return JsonResponse(HttpStatusCode.OK, "{}");
                    }
                }

                return JsonResponse(HttpStatusCode.NotFound, "{}");
            }

            if (path == "/v3/customers" && request.Method == HttpMethod.Get)
            {
                lock (_sync)
                {
                    var data = _customer is null ? [] : new[] { _customer };
                    return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new { data }));
                }
            }

            if (path == "/v3/customers" && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref _customerPostCount);
                using var payload = JsonDocument.Parse(body!);
                var externalReference = payload.RootElement.GetProperty("externalReference").GetString()!;
                var created = new AsaasCustomerStub("cus-1", externalReference);
                lock (_sync)
                {
                    _customer = created;
                }

                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(created));
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        }

        private HttpResponseMessage SubscriptionListResponse()
        {
            lock (_sync)
            {
                var data = _subscription is null ? [] : new[] { _subscription };
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new { data }));
            }
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
            new(statusCode)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path, string? Body, string? AccessToken);

    private sealed record AsaasSubscriptionStub(
        string Id,
        string Customer,
        string ExternalReference,
        string Status,
        string Cycle,
        decimal Value,
        string NextDueDate);

    private sealed record AsaasCustomerStub(string Id, string ExternalReference);
}
