using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
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
    public async Task CreateSubscriptionCheckout_UsesHostedRecurringCreditCardAndSevenDayFirstDueDate()
    {
        var handler = new AsaasStubHandler();
        var gateway = CreateGateway(handler);
        var request = new BillingGatewayCheckoutRequest(
            Guid.Parse("d714f0c4-1082-4e44-86b6-514d1bc17bc5"),
            "cus-existing",
            BillingCycle.Annual,
            199.90m,
            new DateOnly(2026, 10, 1),
            new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero),
            "Plano anual");

        var result = await gateway.CreateSubscriptionCheckoutAsync(request);

        Assert.Equal("checkout-1", result.Id);
        Assert.Equal("https://sandbox.asaas.com/checkoutSession/show/checkout-1", result.Url);
        Assert.Equal(request.ExpiresAtUtc, result.ExpiresAtUtc);
        Assert.Equal(request.SubscriptionId.ToString("D"), result.ExternalReference);
        var post = Assert.Single(handler.Requests, item => item.Method == HttpMethod.Post && item.Path == "/v3/checkouts");
        Assert.Equal("sandbox-test-key", post.AccessToken);
        using var payload = JsonDocument.Parse(post.Body!);
        var root = payload.RootElement;
        Assert.Equal("CREDIT_CARD", root.GetProperty("billingTypes")[0].GetString());
        Assert.Equal("RECURRENT", root.GetProperty("chargeTypes")[0].GetString());
        Assert.Equal(1440, root.GetProperty("minutesToExpire").GetInt32());
        Assert.Equal(request.SubscriptionId.ToString("D"), root.GetProperty("externalReference").GetString());
        Assert.Equal(request.CustomerId, root.GetProperty("customer").GetString());
        Assert.Equal("https://client.example.test/billing/subscription-checkout?result=success", root.GetProperty("callback").GetProperty("successUrl").GetString());
        Assert.Equal("https://client.example.test/billing/subscription-checkout?result=cancelled", root.GetProperty("callback").GetProperty("cancelUrl").GetString());
        Assert.Equal("https://client.example.test/billing/subscription-checkout?result=expired", root.GetProperty("callback").GetProperty("expiredUrl").GetString());
        var item = root.GetProperty("items")[0];
        Assert.Equal(199.90m, item.GetProperty("value").GetDecimal());
        Assert.Equal("Criatório Virtual", item.GetProperty("name").GetString());
        Assert.True(item.GetProperty("name").GetString()!.Length <= 30);
        Assert.Equal("Plano anual", item.GetProperty("description").GetString());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());
        Assert.Equal("YEARLY", root.GetProperty("subscription").GetProperty("cycle").GetString());
        Assert.Equal("2026-10-01", root.GetProperty("subscription").GetProperty("nextDueDate").GetString());
        Assert.False(root.TryGetProperty("creditCardToken", out _));
    }

    [Fact]
    public async Task CreateSubscriptionCheckout_Monthly_UsesMonthlyCycleShortNameDescriptionAndNextDueDateFormat()
    {
        var handler = new AsaasStubHandler();
        var gateway = CreateGateway(handler);
        var request = new BillingGatewayCheckoutRequest(
            Guid.Parse("a1b2c3d4-e5f6-4a5b-8c9d-0e1f2a3b4c5d"),
            "cus-monthly",
            BillingCycle.Monthly,
            19.90m,
            new DateOnly(2026, 10, 15),
            new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
            "Plano Mensal - Criatório Virtual");

        var result = await gateway.CreateSubscriptionCheckoutAsync(request);

        Assert.Equal("checkout-1", result.Id);
        Assert.Equal(BillingCycle.Monthly, result.BillingCycle);
        Assert.Equal(19.90m, result.Amount);
        Assert.Equal(new DateOnly(2026, 10, 15), result.FirstChargeDate);

        var post = Assert.Single(handler.Requests, item => item.Method == HttpMethod.Post && item.Path == "/v3/checkouts");
        using var payload = JsonDocument.Parse(post.Body!);
        var root = payload.RootElement;
        Assert.Equal("MONTHLY", root.GetProperty("subscription").GetProperty("cycle").GetString());
        Assert.Equal("2026-10-15", root.GetProperty("subscription").GetProperty("nextDueDate").GetString());
        var item = root.GetProperty("items")[0];
        Assert.Equal("Criatório Virtual", item.GetProperty("name").GetString());
        Assert.True(item.GetProperty("name").GetString()!.Length <= 30);
        Assert.Equal("Plano Mensal - Criatório Virtual", item.GetProperty("description").GetString());
        Assert.Equal(19.90m, item.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task CreateSubscriptionCheckout_WhenAsaasReturns400_ThrowsBillingGatewayExceptionWithSanitizedDetails()
    {
        var handler = new AsaasStubHandler
        {
            CheckoutPostStatusCode = HttpStatusCode.BadRequest,
            CheckoutPostResponseBody = "{\"errors\":[{\"code\":\"invalid_item_name\",\"description\":\"O campo name do item não pode ter mais de 30 caracteres.\"}]}"
        };
        var gateway = CreateGateway(handler);
        var request = new BillingGatewayCheckoutRequest(
            Guid.NewGuid(),
            "cus-error",
            BillingCycle.Monthly,
            19.90m,
            new DateOnly(2026, 10, 1),
            DateTimeOffset.UtcNow.AddHours(24),
            "Plano Mensal");

        var exception = await Assert.ThrowsAsync<BillingGatewayException>(() =>
            gateway.CreateSubscriptionCheckoutAsync(request));

        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
        Assert.Contains("[invalid_item_name] O campo name do item não pode ter mais de 30 caracteres.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSubscriptionCheckout_LogsSanitizedWarningOn4xxError()
    {
        var capturingLogger = new TestCapturingLogger<AsaasBillingGateway>();
        var handler = new AsaasStubHandler
        {
            CheckoutPostStatusCode = HttpStatusCode.BadRequest,
            CheckoutPostResponseBody = "{\"errors\":[{\"code\":\"invalid_action\",\"description\":\"CPF 123.456.789-01 inválido com chave $aact_secretToken123.\"}]}"
        };
        var gateway = CreateGateway(handler, capturingLogger);
        var request = new BillingGatewayCheckoutRequest(
            Guid.NewGuid(),
            "cus-error",
            BillingCycle.Monthly,
            19.90m,
            new DateOnly(2026, 10, 1),
            DateTimeOffset.UtcNow.AddHours(24),
            "Plano Mensal");

        await Assert.ThrowsAsync<BillingGatewayException>(() =>
            gateway.CreateSubscriptionCheckoutAsync(request));

        var log = Assert.Single(capturingLogger.Messages);
        Assert.Contains("400", log, StringComparison.Ordinal);
        Assert.Contains("[invalid_action]", log, StringComparison.Ordinal);
        Assert.DoesNotContain("123.456.789-01", log, StringComparison.Ordinal);
        Assert.DoesNotContain("$aact_secretToken123", log, StringComparison.Ordinal);
        Assert.Contains("[redacted-cpf]", log, StringComparison.Ordinal);
        Assert.Contains("[redacted-token]", log, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeAsaasErrorMessage_RedactsApiKeyTokensCpfCnpjCardAndEmails()
    {
        var rawJson = "{\"errors\":[{\"code\":\"invalid_input\",\"description\":\"Erro com CPF 123.456.789-01, CNPJ 12.345.678/0001-90, cartão 4111 2222 3333 4444, email admin@example.com, token secret-auth-token e chave $aact_mySecretKey99.\"}]}";
        var sanitized = AsaasBillingGateway.SanitizeAsaasErrorMessage(rawJson, "secret-api-key-1234");

        Assert.DoesNotContain("123.456.789-01", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("12.345.678/0001-90", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("4111 2222 3333 4444", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("admin@example.com", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-auth-token", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("$aact_mySecretKey99", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-api-key-1234", sanitized, StringComparison.Ordinal);
        Assert.Contains("[redacted-cpf]", sanitized, StringComparison.Ordinal);
        Assert.Contains("[redacted-cnpj]", sanitized, StringComparison.Ordinal);
        Assert.Contains("[redacted-card]", sanitized, StringComparison.Ordinal);
        Assert.Contains("[redacted-email]", sanitized, StringComparison.Ordinal);
        Assert.Contains("[redacted-token]", sanitized, StringComparison.Ordinal);
        Assert.Contains("token=[redacted]", sanitized, StringComparison.Ordinal);
        Assert.Contains("[invalid_input]", sanitized, StringComparison.Ordinal);
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
    public async Task PayPaymentWithCreditCard_UsesExistingPaymentAndTokenOnly()
    {
        var handler = new AsaasStubHandler();
        var gateway = CreateGateway(handler);
        var request = new BillingGatewayPaymentRequest("pay-existing", "asaas-single-use-token");

        var result = await gateway.PayPaymentWithCreditCardAsync(request);

        Assert.Equal("pay-existing", result.Id);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal("https://www.asaas.com/i/hosted-invoice", result.InvoiceUrl);
        var post = Assert.Single(handler.Requests, item => item.Method == HttpMethod.Post && item.Path == "/v3/payments/pay-existing/payWithCreditCard");
        using var payload = JsonDocument.Parse(post.Body!);
        Assert.Equal("asaas-single-use-token", payload.RootElement.GetProperty("creditCardToken").GetString());
        Assert.Equal(1, handler.PaymentPayPostCount);
        Assert.DoesNotContain("asaas-single-use-token", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaymentTimeout_ReconcilesPaymentAndDoesNotSubmitAnotherPost()
    {
        var handler = new AsaasStubHandler { TimeoutAfterPaymentConfirmation = true };
        var gateway = CreateGateway(handler);

        var result = await gateway.PayPaymentWithCreditCardAsync(
            new BillingGatewayPaymentRequest("pay-existing", "asaas-single-use-token"));

        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal(1, handler.PaymentPayPostCount);
        Assert.Contains(handler.Requests, item => item.Method == HttpMethod.Get && item.Path == "/v3/payments/pay-existing");
    }

    [Fact]
    public async Task PaymentTimeoutWithUnpaidProviderStateRequiresFreshAttemptKey()
    {
        var handler = new AsaasStubHandler { TimeoutBeforePaymentConfirmation = true };
        var gateway = CreateGateway(handler);

        await Assert.ThrowsAsync<BillingGatewayPaymentNotChargedException>(() =>
            gateway.PayPaymentWithCreditCardAsync(new BillingGatewayPaymentRequest("pay-existing", "asaas-single-use-token")));

        Assert.Equal(1, handler.PaymentPayPostCount);
        Assert.Contains(handler.Requests, item => item.Method == HttpMethod.Get && item.Path == "/v3/payments/pay-existing");
    }

    [Fact]
    public async Task PaymentAuthorizationError_IsNotReportedAsCardDecline()
    {
        var handler = new AsaasStubHandler { PaymentPostStatusCode = HttpStatusCode.Unauthorized };
        var gateway = CreateGateway(handler);

        await Assert.ThrowsAsync<BillingGatewayException>(() =>
            gateway.PayPaymentWithCreditCardAsync(new BillingGatewayPaymentRequest("pay-existing", "asaas-single-use-token")));
    }

    [Fact]
    public void SubscriptionRequest_RedactsCardTokenAndAddressFromStringRepresentation()
    {
        var request = CreateSubscriptionRequest();

        Assert.DoesNotContain(request.CardToken, request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.42", request.ToString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", request.ToString(), StringComparison.Ordinal);
    }

    private static AsaasBillingGateway CreateGateway(
        AsaasStubHandler handler,
        ILogger<AsaasBillingGateway>? logger = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(AsaasOptions.SandboxBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(60)
        };
        return new AsaasBillingGateway(
            httpClient,
            Options.Create(new AsaasOptions
            {
                ApiKey = "sandbox-test-key",
                BaseUrl = AsaasOptions.SandboxBaseUrl,
                CheckoutCallbackBaseUrl = "https://client.example.test/"
            }),
            new AsaasOperationCoordinator(),
            logger);
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
        private int _checkoutPostCount;
        private int _customerPostCount;
        private int _paymentPayPostCount;
        private string _paymentStatus = "PENDING";

        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        public bool TimeoutAfterFirstSubscriptionCreate { get; init; }

        public bool TimeoutBeforeSubscriptionCreation { get; init; }

        public bool DelaySubscriptionCreate { get; init; }

        public int SubscriptionPostCount => Volatile.Read(ref _subscriptionPostCount);

        public int CheckoutPostCount => Volatile.Read(ref _checkoutPostCount);

        public int CustomerPostCount => Volatile.Read(ref _customerPostCount);

        public int PaymentPayPostCount => Volatile.Read(ref _paymentPayPostCount);

        public bool TimeoutAfterPaymentConfirmation { get; init; }

        public bool TimeoutBeforePaymentConfirmation { get; init; }

        public HttpStatusCode PaymentPostStatusCode { get; init; } = HttpStatusCode.OK;

        public HttpStatusCode CheckoutPostStatusCode { get; init; } = HttpStatusCode.OK;

        public string? CheckoutPostResponseBody { get; init; }

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

            if (path == "/v3/payments/pay-existing" && request.Method == HttpMethod.Get)
            {
                return PaymentResponse(_paymentStatus);
            }

            if (path == "/v3/payments/pay-existing/payWithCreditCard" && request.Method == HttpMethod.Post)
            {
                _ = Interlocked.Increment(ref _paymentPayPostCount);
                if (PaymentPostStatusCode != HttpStatusCode.OK)
                {
                    return JsonResponse(PaymentPostStatusCode, "{}");
                }

                if (TimeoutBeforePaymentConfirmation)
                {
                    throw new TaskCanceledException("Simulated timeout before Asaas confirmed the payment.");
                }

                _paymentStatus = "CONFIRMED";
                if (TimeoutAfterPaymentConfirmation)
                {
                    throw new TaskCanceledException("Simulated timeout after Asaas confirmed the payment.");
                }

                return PaymentResponse(_paymentStatus);
            }

            if (path == "/v3/checkouts" && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref _checkoutPostCount);
                if (CheckoutPostStatusCode != HttpStatusCode.OK)
                {
                    return JsonResponse(
                        CheckoutPostStatusCode,
                        CheckoutPostResponseBody ?? "{\"errors\":[{\"code\":\"bad_request\",\"description\":\"Invalid checkout payload.\"}]}");
                }

                using var payload = JsonDocument.Parse(body!);
                var externalReference = payload.RootElement.GetProperty("externalReference").GetString();
                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    id = "checkout-1",
                    link = "https://sandbox.asaas.com/checkoutSession/show/checkout-1",
                    status = "ACTIVE",
                    externalReference
                }));
            }

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

        private static HttpResponseMessage PaymentResponse(string status) => JsonResponse(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                id = "pay-existing",
                customer = "cus-existing",
                subscription = "sub-existing",
                value = 19.90m,
                dueDate = "2026-10-01",
                status,
                invoiceUrl = "https://www.asaas.com/i/hosted-invoice"
            }));

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

    private sealed class TestCapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message} Exception: {exception.Message}";
            }

            Messages.Enqueue(message);
        }
    }
}
