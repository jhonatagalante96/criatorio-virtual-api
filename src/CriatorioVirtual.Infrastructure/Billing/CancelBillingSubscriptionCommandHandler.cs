using CriatorioVirtual.Application.Billing;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class CancelBillingSubscriptionCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<CancelBillingSubscriptionCommand, CancelBillingSubscriptionResult>
{
    public async Task<CancelBillingSubscriptionResult> Handle(
        CancelBillingSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return CancelBillingSubscriptionResult.Failed(CancelBillingSubscriptionStatus.UserNotFound);
        }

        if (user.SelectedBreedingFarmId is not { } breedingFarmId)
        {
            return CancelBillingSubscriptionResult.Failed(CancelBillingSubscriptionStatus.BreedingFarmNotSelected);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.breeding_farms WHERE \"Id\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);
        var farmExists = await dbContext.BreedingFarms
            .AnyAsync(candidate => candidate.Id == breedingFarmId, cancellationToken);
        if (!farmExists)
        {
            return CancelBillingSubscriptionResult.Failed(CancelBillingSubscriptionStatus.BreedingFarmNotFound);
        }

        var isOwner = await dbContext.BreedingFarmUsers.AnyAsync(
            membership => membership.BreedingFarmId == breedingFarmId &&
                          membership.UserId == command.UserId &&
                          membership.Role == BreedingFarmRole.Owner &&
                          membership.IsActive,
            cancellationToken);
        if (!isOwner)
        {
            return CancelBillingSubscriptionResult.Failed(CancelBillingSubscriptionStatus.NotFarmOwner);
        }

        var subscription = await dbContext.Subscriptions
            .Where(candidate => candidate.BreedingFarmId == breedingFarmId)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription is null)
        {
            return CancelBillingSubscriptionResult.Failed(CancelBillingSubscriptionStatus.SubscriptionNotFound);
        }

        if (subscription.Status == SubscriptionStatus.Cancelled)
        {
            return CancelBillingSubscriptionResult.Succeeded(breedingFarmId, subscription.Id);
        }

        if (subscription.Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.GracePeriod) ||
            string.IsNullOrWhiteSpace(subscription.GatewaySubscriptionId))
        {
            return CancelBillingSubscriptionResult.Failed(CancelBillingSubscriptionStatus.SubscriptionNotCancelable);
        }

        return CancelBillingSubscriptionResult.RequestCancellation(
            breedingFarmId,
            subscription.Id,
            subscription.GatewaySubscriptionId);
    }
}
