using System.Security.Claims;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CriatorioVirtual.Api.Security;

public sealed class FunctionalAccessFilter(
    IConfiguration configuration,
    IHostEnvironment environment,
    CriatorioVirtualDbContext? dbContext = null) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (environment.IsEnvironment("Testing"))
        {
            var enforceBlockingInTesting = configuration.GetValue<bool?>("Billing:EnforceFunctionalBlocking") ?? false;
            if (!enforceBlockingInTesting)
            {
                await next();
                return;
            }
        }

        if (dbContext is null)
        {
            await next();
            return;
        }

        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next();
            return;
        }

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var httpMethod = context.HttpContext.Request.Method;
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;

        if (FunctionalAccessAllowlist.IsAllowed(httpMethod, path))
        {
            await next();
            return;
        }

        if (!Guid.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await next();
            return;
        }

        var selectedBreedingFarmId = await dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => candidate.SelectedBreedingFarmId)
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (selectedBreedingFarmId is null)
        {
            await next();
            return;
        }

        var isOwner = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.BreedingFarmId == selectedBreedingFarmId.Value &&
                             candidate.UserId == userId &&
                             candidate.IsActive &&
                             candidate.Role == BreedingFarmRole.Owner,
                context.HttpContext.RequestAborted);

        if (!isOwner)
        {
            context.Result = CreateBlockedResult(context.HttpContext.TraceIdentifier);
            return;
        }

        var subscriptionStatus = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == selectedBreedingFarmId.Value)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => (SubscriptionStatus?)candidate.Status)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (!BillingAccessPolicy.CanAccessApp(subscriptionStatus))
        {
            context.Result = CreateBlockedResult(context.HttpContext.TraceIdentifier);
            return;
        }

        await next();
    }

    private static ObjectResult CreateBlockedResult(string traceIdentifier)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = "Functional access is blocked by billing.",
            Type = "https://httpstatuses.com/403",
            Extensions =
            {
                ["code"] = "functional_access_blocked",
                ["correlationId"] = traceIdentifier
            }
        };

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" }
        };
    }
}

public static class FunctionalAccessAllowlist
{
    public static bool IsAllowed(string httpMethod, string path)
    {
        var normalizedPath = path.TrimEnd('/');

        // 1. Context and Profile
        if (HttpMethods.IsGet(httpMethod) && normalizedPath.Equals("/api/me/access-context", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsGet(httpMethod) && normalizedPath.Equals("/api/auth/session", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedPath.Equals("/api/me/avatar", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedPath.StartsWith("/api/auth/passkeys", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsPost(httpMethod) && normalizedPath.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Logout
        if (HttpMethods.IsPost(httpMethod) && normalizedPath.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Onboarding and breeding farm selection
        if (HttpMethods.IsGet(httpMethod) && normalizedPath.Equals("/api/breeding-farms", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsPost(httpMethod) && normalizedPath.Equals("/api/breeding-farms", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsPut(httpMethod) && normalizedPath.Equals("/api/breeding-farms/selection", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 4. Strictly necessary billing routes for consult/contract/cancel/regularize
        if (HttpMethods.IsGet(httpMethod) && normalizedPath.Equals("/api/billing/subscription", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Allow listing payments so the client can discover the paymentId to regularize
        if (HttpMethods.IsGet(httpMethod) && normalizedPath.Equals("/api/billing/payments", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsPost(httpMethod) && normalizedPath.Equals("/api/billing/subscriptions", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsPost(httpMethod) && normalizedPath.Equals("/api/billing/subscription-checkouts", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsDelete(httpMethod) && normalizedPath.Equals("/api/billing/subscriptions", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsPost(httpMethod) &&
            normalizedPath.StartsWith("/api/billing/payments/", StringComparison.OrdinalIgnoreCase) &&
            (normalizedPath.EndsWith("/regularization", StringComparison.OrdinalIgnoreCase) ||
             normalizedPath.EndsWith("/attempts", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 5. Homologation simulation route
        if (HttpMethods.IsPost(httpMethod) &&
            normalizedPath.Equals("/api/homologation/billing/simulation", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
