using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Competitions;

public sealed class DeleteBirdCompetitionCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<DeleteBirdCompetitionCommand, DeleteBirdCompetitionResult>
{
    public async Task<DeleteBirdCompetitionResult> Handle(
        DeleteBirdCompetitionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty ||
            command.BirdId == Guid.Empty ||
            command.CompetitionId == Guid.Empty)
        {
            return DeleteBirdCompetitionResult.InvalidData();
        }

        if (!command.Confirmed)
        {
            return DeleteBirdCompetitionResult.ConfirmationRequired();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return DeleteBirdCompetitionResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return DeleteBirdCompetitionResult.BreedingFarmNotSelected();
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
            return DeleteBirdCompetitionResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == command.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return DeleteBirdCompetitionResult.BirdNotFound();
        }

        var competition = await dbContext.BirdCompetitions
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.CompetitionId &&
                    candidate.BirdId == command.BirdId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (competition is null)
        {
            return DeleteBirdCompetitionResult.CompetitionNotFound();
        }

        dbContext.BirdCompetitions.Remove(competition);
        return DeleteBirdCompetitionResult.Deleted();
    }
}
