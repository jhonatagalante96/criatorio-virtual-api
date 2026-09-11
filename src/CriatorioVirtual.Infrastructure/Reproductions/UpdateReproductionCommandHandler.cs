using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Reproductions;

public sealed class UpdateReproductionCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<UpdateReproductionCommand, UpdateReproductionResult>
{
    public async Task<UpdateReproductionResult> Handle(
        UpdateReproductionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return UpdateReproductionResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return UpdateReproductionResult.BreedingFarmNotSelected();
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
            return UpdateReproductionResult.BreedingFarmNotFound();
        }

        var reproduction = await dbContext.Reproductions
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.ReproductionId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (reproduction is null)
        {
            return UpdateReproductionResult.ReproductionNotFound();
        }

        if (reproduction.Status == ReproductionStatus.Active &&
            (!command.HasMaleBirdId ||
             command.MaleBirdId is null ||
             !command.HasFemaleBirdId ||
             command.FemaleBirdId is null ||
             !command.HasStartDate ||
             command.StartDate is null))
        {
            return UpdateReproductionResult.InvalidData();
        }

        var maleBirdId = command.HasMaleBirdId && command.MaleBirdId is not null
            ? command.MaleBirdId.Value
            : reproduction.MaleBirdId;
        var femaleBirdId = command.HasFemaleBirdId && command.FemaleBirdId is not null
            ? command.FemaleBirdId.Value
            : reproduction.FemaleBirdId;
        var startDate = command.HasStartDate && command.StartDate is not null
            ? command.StartDate.Value
            : reproduction.StartDate;
        var endDate = command.HasEndDate ? command.EndDate : reproduction.EndDate;
        var notes = command.HasNotes ? command.Notes : reproduction.Notes;

        if (reproduction.Status is not ReproductionStatus.Active &&
            ((command.HasMaleBirdId && maleBirdId != reproduction.MaleBirdId) ||
             (command.HasFemaleBirdId && femaleBirdId != reproduction.FemaleBirdId) ||
             (command.HasStartDate && startDate != reproduction.StartDate) ||
             (command.HasEndDate && endDate != reproduction.EndDate)))
        {
            return UpdateReproductionResult.InvalidState();
        }

        if (reproduction.Status == ReproductionStatus.Active &&
            (maleBirdId != reproduction.MaleBirdId || femaleBirdId != reproduction.FemaleBirdId))
        {
            var birdIds = new[] { maleBirdId, femaleBirdId };
            var birds = await dbContext.Birds
                .AsNoTracking()
                .Where(bird => bird.BreedingFarmId == breedingFarmId && birdIds.Contains(bird.Id))
                .ToArrayAsync(cancellationToken);
            if (birds.Length != birdIds.Length)
            {
                return UpdateReproductionResult.BirdNotFound();
            }

            var maleBird = birds.Single(bird => bird.Id == maleBirdId);
            var femaleBird = birds.Single(bird => bird.Id == femaleBirdId);
            if (maleBird.Sex != BirdSex.Male)
            {
                return UpdateReproductionResult.MaleBirdSexInvalid();
            }

            if (femaleBird.Sex != BirdSex.Female)
            {
                return UpdateReproductionResult.FemaleBirdSexInvalid();
            }

            if (!BirdEligibility.Evaluate(maleBird.RingNumber, maleBird.Status).IsEligible ||
                !BirdEligibility.Evaluate(femaleBird.RingNumber, femaleBird.Status).IsEligible)
            {
                return UpdateReproductionResult.BirdNotEligible();
            }
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            reproduction.UpdateDetails(
                maleBirdId,
                femaleBirdId,
                startDate,
                endDate,
                notes,
                DateOnly.FromDateTime(now.UtcDateTime),
                now);
        }
        catch (InvalidOperationException)
        {
            return UpdateReproductionResult.InvalidState();
        }
        catch (ArgumentException)
        {
            return UpdateReproductionResult.InvalidData();
        }

        return UpdateReproductionResult.Updated(ToResult(reproduction));
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
