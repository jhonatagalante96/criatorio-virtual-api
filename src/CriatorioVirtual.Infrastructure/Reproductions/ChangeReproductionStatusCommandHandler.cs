using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Reproductions;

public sealed class ChangeReproductionStatusCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<ChangeReproductionStatusCommand, ChangeReproductionStatusResult>
{
    public async Task<ChangeReproductionStatusResult> Handle(
        ChangeReproductionStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return ChangeReproductionStatusResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ChangeReproductionStatusResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            return ChangeReproductionStatusResult.BreedingFarmNotFound();
        }

        var reproduction = await dbContext.Reproductions
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.ReproductionId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (reproduction is null)
        {
            return ChangeReproductionStatusResult.ReproductionNotFound();
        }

        if (!command.Confirmed)
        {
            return ChangeReproductionStatusResult.ConfirmationRequired();
        }

        if (command.Status is not (ReproductionStatus.Finished or ReproductionStatus.Cancelled))
        {
            return ChangeReproductionStatusResult.InvalidStatus();
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            if (command.Status == ReproductionStatus.Finished)
            {
                if (command.EndDate is null)
                {
                    return ChangeReproductionStatusResult.InvalidData();
                }

                reproduction.Finish(
                    command.EndDate.Value,
                    DateOnly.FromDateTime(now.UtcDateTime),
                    now);
            }
            else
            {
                if (command.EndDate is not null)
                {
                    return ChangeReproductionStatusResult.InvalidData();
                }

                reproduction.Cancel(now);
            }
        }
        catch (InvalidOperationException)
        {
            return ChangeReproductionStatusResult.InvalidState();
        }
        catch (ArgumentException)
        {
            return ChangeReproductionStatusResult.InvalidData();
        }

        return ChangeReproductionStatusResult.Updated(ToResult(reproduction));
    }

    private static ReproductionResult ToResult(Reproduction reproduction) =>
        new(
            reproduction.Id,
            reproduction.BreedingFarmId,
            reproduction.MaleBirdId,
            reproduction.FemaleBirdId,
            reproduction.StartDate,
            reproduction.EndDate,
            reproduction.Notes,
            reproduction.Status,
            reproduction.CreatedAtUtc,
            reproduction.UpdatedAtUtc);
}
