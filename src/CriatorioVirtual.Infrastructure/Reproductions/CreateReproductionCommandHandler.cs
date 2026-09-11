using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Reproductions;

public sealed class CreateReproductionCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<CreateReproductionCommand, CreateReproductionResult>
{
    public async Task<CreateReproductionResult> Handle(
        CreateReproductionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return CreateReproductionResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return CreateReproductionResult.BreedingFarmNotSelected();
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
            return CreateReproductionResult.BreedingFarmNotFound();
        }

        if (command.MaleBirdId == command.FemaleBirdId)
        {
            return CreateReproductionResult.InvalidData();
        }

        var birdIds = new[] { command.MaleBirdId, command.FemaleBirdId };
        var birds = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId && birdIds.Contains(bird.Id))
            .ToArrayAsync(cancellationToken);
        if (birds.Length != birdIds.Length)
        {
            return CreateReproductionResult.BirdNotFound();
        }

        var maleBird = birds.Single(bird => bird.Id == command.MaleBirdId);
        var femaleBird = birds.Single(bird => bird.Id == command.FemaleBirdId);
        if (maleBird.Sex != BirdSex.Male)
        {
            return CreateReproductionResult.MaleBirdSexInvalid();
        }

        if (femaleBird.Sex != BirdSex.Female)
        {
            return CreateReproductionResult.FemaleBirdSexInvalid();
        }

        if (!BirdEligibility.Evaluate(maleBird.RingNumber, maleBird.Status).IsEligible ||
            !BirdEligibility.Evaluate(femaleBird.RingNumber, femaleBird.Status).IsEligible)
        {
            return CreateReproductionResult.BirdNotEligible();
        }

        var now = DateTimeOffset.UtcNow;
        var reproduction = new Reproduction(
            Guid.NewGuid(),
            now,
            breedingFarmId,
            maleBird.Id,
            femaleBird.Id,
            command.StartDate,
            command.EndDate,
            command.Notes,
            DateOnly.FromDateTime(now.UtcDateTime));

        dbContext.Reproductions.Add(reproduction);
        return CreateReproductionResult.Created(ToResult(reproduction));
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
