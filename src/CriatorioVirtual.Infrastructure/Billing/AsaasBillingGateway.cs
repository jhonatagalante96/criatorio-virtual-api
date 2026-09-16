using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Domain.Billing;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class AsaasBillingGateway(
    HttpClient httpClient,
    IOptions<AsaasOptions> options,
    IAsaasOperationCoordinator operationCoordinator) : IBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<BillingGatewayCustomer> GetOrCreateCustomerAsync(
        BillingGatewayCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCustomerRequest(request);
        EnsureApiKeyConfigured();

        var externalReference = request.BreedingFarmId.ToString("D", CultureInfo.InvariantCulture);
        await using var operation = await operationCoordinator.AcquireAsync(
            $"customer:{externalReference}",
            cancellationToken);

        var existing = await FindCustomerCoreAsync(externalReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        cancellationToken.ThrowIfCancellationRequested();
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(
                HttpMethod.Post,
                "customers",
                new AsaasCreateCustomerRequest(
                    request.Name.Trim(),
                    request.TaxIdentifier.Trim(),
                    request.Email?.Trim(),
                    request.MobilePhone?.Trim(),
                    externalReference),
                cancellationToken);
        }
        catch (Exception exception) when (IsAmbiguousTransportFailure(exception))
        {
            return await ReconcileCustomerAfterUnknownAsync(externalReference, exception);
        }

        if (IsAmbiguousStatus(response.StatusCode))
        {
            response.Dispose();
            return await ReconcileCustomerAfterUnknownAsync(
                externalReference,
                new BillingGatewayException("Asaas returned an ambiguous response while creating a customer."));
        }

        using (response)
        {
            EnsureSuccessStatus(response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<AsaasCustomerResponse>(JsonOptions, cancellationToken);
            if (created is null || string.IsNullOrWhiteSpace(created.Id))
            {
                return await ReconcileCustomerAfterUnknownAsync(
                    externalReference,
                    new BillingGatewayException("Asaas returned an incomplete customer response."));
            }

            if (!string.IsNullOrWhiteSpace(created.ExternalReference) &&
                !string.Equals(created.ExternalReference, externalReference, StringComparison.Ordinal))
            {
                throw new BillingGatewayIdempotencyConflictException(externalReference);
            }

            return new BillingGatewayCustomer(created.Id);
        }
    }

    public async Task<BillingGatewaySubscription> GetOrCreateSubscriptionAsync(
        BillingGatewaySubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureApiKeyConfigured();

        var externalReference = request.SubscriptionId.ToString("D", CultureInfo.InvariantCulture);
        await using var operation = await operationCoordinator.AcquireAsync(
            $"subscription:{externalReference}",
            cancellationToken);

        var existing = await FindSubscriptionCoreAsync(externalReference, cancellationToken);
        if (existing is not null)
        {
            EnsureSubscriptionMatchesRequest(existing, request, externalReference);
            return existing;
        }

        cancellationToken.ThrowIfCancellationRequested();
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(
                HttpMethod.Post,
                "subscriptions",
                new AsaasCreateSubscriptionRequest(
                    request.CustomerId,
                    "CREDIT_CARD",
                    request.Amount,
                    request.FirstChargeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CycleToAsaas(request.BillingCycle),
                    request.Description,
                    externalReference,
                    request.CardToken,
                    request.RemoteIp.ToString()),
                cancellationToken);
        }
        catch (Exception exception) when (IsAmbiguousTransportFailure(exception))
        {
            return await ReconcileSubscriptionAfterUnknownAsync(externalReference, request, exception);
        }

        if (IsAmbiguousStatus(response.StatusCode))
        {
            var statusCode = response.StatusCode;
            response.Dispose();
            return await ReconcileSubscriptionAfterUnknownAsync(
                externalReference,
                request,
                new BillingGatewayException($"Asaas returned an ambiguous response while creating a subscription (HTTP {(int)statusCode})."));
        }

        using (response)
        {
            EnsureSuccessStatus(response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<AsaasSubscriptionResponse>(JsonOptions, cancellationToken);
            if (created is null || string.IsNullOrWhiteSpace(created.Id))
            {
                return await ReconcileSubscriptionAfterUnknownAsync(
                    externalReference,
                    request,
                    new BillingGatewayException("Asaas returned an incomplete subscription response."));
            }

            var subscription = ToBillingGatewaySubscription(created, externalReference);
            EnsureSubscriptionMatchesRequest(subscription, request, externalReference);
            return subscription;
        }
    }

    public async Task<BillingGatewaySubscription?> FindSubscriptionAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (subscriptionId == Guid.Empty)
        {
            throw new ArgumentException("A local subscription identifier is required.", nameof(subscriptionId));
        }

        EnsureApiKeyConfigured();
        return await FindSubscriptionCoreAsync(
            subscriptionId.ToString("D", CultureInfo.InvariantCulture),
            cancellationToken);
    }

    public async Task<BillingGatewaySubscription?> GetSubscriptionAsync(
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewaySubscriptionId);
        EnsureApiKeyConfigured();

        using var response = await SendAsync(
            HttpMethod.Get,
            $"subscriptions/{Uri.EscapeDataString(gatewaySubscriptionId.Trim())}",
            body: null,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccessStatus(response.StatusCode);
        var subscription = await response.Content.ReadFromJsonAsync<AsaasSubscriptionResponse>(JsonOptions, cancellationToken);
        return subscription is null ? null : ToBillingGatewaySubscription(subscription);
    }

    public async Task CancelSubscriptionAsync(
        Guid subscriptionId,
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (subscriptionId == Guid.Empty)
        {
            throw new ArgumentException("A local subscription identifier is required.", nameof(subscriptionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gatewaySubscriptionId);
        EnsureApiKeyConfigured();

        var externalReference = subscriptionId.ToString("D", CultureInfo.InvariantCulture);
        await using var operation = await operationCoordinator.AcquireAsync(
            $"subscription:{externalReference}",
            cancellationToken);

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(
                HttpMethod.Delete,
                $"subscriptions/{Uri.EscapeDataString(gatewaySubscriptionId.Trim())}",
                body: null,
                cancellationToken);
        }
        catch (Exception exception) when (IsAmbiguousTransportFailure(exception))
        {
            throw new BillingGatewayOperationOutcomeUnknownException(externalReference, exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            EnsureSuccessStatus(response.StatusCode);
        }
    }

    private async Task<BillingGatewayCustomer?> FindCustomerCoreAsync(
        string externalReference,
        CancellationToken cancellationToken)
    {
        var path = $"customers?externalReference={Uri.EscapeDataString(externalReference)}&limit=100&offset=0";
        using var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);
        EnsureSuccessStatus(response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AsaasPagedResponse<AsaasCustomerResponse>>(JsonOptions, cancellationToken);
        var matches = result?.Data
            .Where(customer => string.Equals(customer.ExternalReference, externalReference, StringComparison.Ordinal))
            .ToArray() ?? [];

        if (matches.Length > 1)
        {
            throw new BillingGatewayIdempotencyConflictException(externalReference);
        }

        return matches.Length == 0 || string.IsNullOrWhiteSpace(matches[0].Id)
            ? null
            : new BillingGatewayCustomer(matches[0].Id!);
    }

    private async Task<BillingGatewaySubscription?> FindSubscriptionCoreAsync(
        string externalReference,
        CancellationToken cancellationToken)
    {
        var path = $"subscriptions?externalReference={Uri.EscapeDataString(externalReference)}&limit=100&offset=0";
        using var response = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);
        EnsureSuccessStatus(response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AsaasPagedResponse<AsaasSubscriptionResponse>>(JsonOptions, cancellationToken);
        var matches = result?.Data
            .Where(subscription => string.Equals(subscription.ExternalReference, externalReference, StringComparison.Ordinal))
            .ToArray() ?? [];

        if (matches.Length > 1)
        {
            throw new BillingGatewayIdempotencyConflictException(externalReference);
        }

        return matches.Length == 0 ? null : ToBillingGatewaySubscription(matches[0], externalReference);
    }

    private async Task<BillingGatewayCustomer> ReconcileCustomerAfterUnknownAsync(
        string externalReference,
        Exception failure)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await FindCustomerCoreAsync(externalReference, timeout.Token)
                ?? throw new BillingGatewayOperationOutcomeUnknownException(externalReference, failure);
        }
        catch (BillingGatewayOperationOutcomeUnknownException)
        {
            throw;
        }
        catch (Exception reconciliationFailure)
        {
            throw new BillingGatewayOperationOutcomeUnknownException(
                externalReference,
                new AggregateException(failure, reconciliationFailure));
        }
    }

    private async Task<BillingGatewaySubscription> ReconcileSubscriptionAfterUnknownAsync(
        string externalReference,
        BillingGatewaySubscriptionRequest request,
        Exception failure)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var subscription = await FindSubscriptionCoreAsync(externalReference, timeout.Token);
            if (subscription is null)
            {
                throw new BillingGatewayOperationOutcomeUnknownException(externalReference, failure);
            }

            EnsureSubscriptionMatchesRequest(subscription, request, externalReference);
            return subscription;
        }
        catch (BillingGatewayOperationOutcomeUnknownException)
        {
            throw;
        }
        catch (Exception reconciliationFailure)
        {
            throw new BillingGatewayOperationOutcomeUnknownException(
                externalReference,
                new AggregateException(failure, reconciliationFailure));
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.TryAddWithoutValidation("access_token", options.Value.ApiKey.Trim());
        request.Headers.TryAddWithoutValidation("User-Agent", "CriatorioVirtual.Api/1.0");
        request.Headers.Accept.ParseAdd("application/json");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private void EnsureApiKeyConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            throw new BillingGatewayException(
                $"{AsaasOptions.SectionName}:ApiKey must be configured before billing gateway operations can be used.");
        }
    }

    private static void ValidateCustomerRequest(BillingGatewayCustomerRequest request)
    {
        if (request.BreedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("A breeding farm is required.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TaxIdentifier);
    }

    private static bool IsAmbiguousTransportFailure(Exception exception) =>
        exception is HttpRequestException or OperationCanceledException;

    private static bool IsAmbiguousStatus(HttpStatusCode statusCode) =>
        (int)statusCode >= 500 || statusCode == HttpStatusCode.Conflict;

    private static void EnsureSuccessStatus(HttpStatusCode statusCode)
    {
        if ((int)statusCode is < 200 or >= 300)
        {
            throw new BillingGatewayException(
                $"Asaas rejected the request with HTTP {(int)statusCode} ({statusCode}).");
        }
    }

    private static void EnsureSubscriptionMatchesRequest(
        BillingGatewaySubscription subscription,
        BillingGatewaySubscriptionRequest request,
        string externalReference)
    {
        if (!string.Equals(subscription.ExternalReference, externalReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.CustomerId, request.CustomerId, StringComparison.Ordinal) ||
            subscription.BillingCycle != request.BillingCycle ||
            subscription.Amount != request.Amount ||
            subscription.FirstChargeDate != request.FirstChargeDate)
        {
            throw new BillingGatewayIdempotencyConflictException(externalReference);
        }
    }

    private static BillingGatewaySubscription ToBillingGatewaySubscription(
        AsaasSubscriptionResponse response,
        string? expectedExternalReference = null)
    {
        if (string.IsNullOrWhiteSpace(response.Id) || string.IsNullOrWhiteSpace(response.Customer))
        {
            throw new BillingGatewayException("Asaas returned an incomplete subscription record.");
        }

        var externalReference = response.ExternalReference ?? expectedExternalReference ?? string.Empty;
        if (expectedExternalReference is not null &&
            !string.Equals(externalReference, expectedExternalReference, StringComparison.Ordinal))
        {
            throw new BillingGatewayIdempotencyConflictException(expectedExternalReference);
        }

        return new BillingGatewaySubscription(
            response.Id,
            response.Customer,
            externalReference,
            response.Status ?? string.Empty,
            CycleFromAsaas(response.Cycle),
            response.Value,
            response.NextDueDate);
    }

    private static string CycleToAsaas(BillingCycle billingCycle) => billingCycle switch
    {
        BillingCycle.Monthly => "MONTHLY",
        BillingCycle.Annual => "YEARLY",
        _ => throw new ArgumentOutOfRangeException(nameof(billingCycle), billingCycle, "The billing cycle is not supported.")
    };

    private static BillingCycle? CycleFromAsaas(string? cycle) => cycle switch
    {
        "MONTHLY" => BillingCycle.Monthly,
        "YEARLY" => BillingCycle.Annual,
        _ => null
    };

    private sealed record AsaasCreateCustomerRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("cpfCnpj")] string TaxIdentifier,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("mobilePhone")] string? MobilePhone,
        [property: JsonPropertyName("externalReference")] string ExternalReference);

    private sealed record AsaasCreateSubscriptionRequest(
        [property: JsonPropertyName("customer")] string Customer,
        [property: JsonPropertyName("billingType")] string BillingType,
        [property: JsonPropertyName("value")] decimal Value,
        [property: JsonPropertyName("nextDueDate")] string NextDueDate,
        [property: JsonPropertyName("cycle")] string Cycle,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("externalReference")] string ExternalReference,
        [property: JsonPropertyName("creditCardToken")] string CreditCardToken,
        [property: JsonPropertyName("remoteIp")] string RemoteIp);

    private sealed class AsaasPagedResponse<T>
    {
        [JsonPropertyName("data")]
        public List<T> Data { get; init; } = [];
    }

    private sealed class AsaasCustomerResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("externalReference")]
        public string? ExternalReference { get; init; }
    }

    private sealed class AsaasSubscriptionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("customer")]
        public string? Customer { get; init; }

        [JsonPropertyName("externalReference")]
        public string? ExternalReference { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("cycle")]
        public string? Cycle { get; init; }

        [JsonPropertyName("value")]
        public decimal? Value { get; init; }

        [JsonPropertyName("nextDueDate")]
        public DateOnly? NextDueDate { get; init; }
    }
}
