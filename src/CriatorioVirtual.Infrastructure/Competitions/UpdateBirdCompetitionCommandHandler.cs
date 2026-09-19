using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Competitions;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Competitions;

public sealed class UpdateBirdCompetitionCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<UpdateBirdCompetitionCommand, UpdateBirdCompetitionResult>
{
    public async Task<UpdateBirdCompetitionResult> Handle(
        UpdateBirdCompetitionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (command.UserId == Guid.Empty ||
            command.BirdId == Guid.Empty ||
            command.CompetitionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Name) ||
            command.Name.Trim().Length > BirdCompetition.NameMaxLength ||
            command.Category?.Trim().Length > BirdCompetition.CategoryMaxLength ||
            command.Location?.Trim().Length > BirdCompetition.LocationMaxLength ||
            command.Notes?.Trim().Length > BirdCompetition.NotesMaxLength ||
            command.CompetitionDate > today ||
            command.Placement is <= 0)
        {
            return UpdateBirdCompetitionResult.InvalidData();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return UpdateBirdCompetitionResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return UpdateBirdCompetitionResult.BreedingFarmNotSelected();
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
            return UpdateBirdCompetitionResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == command.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return UpdateBirdCompetitionResult.BirdNotFound();
        }

        var competition = await dbContext.BirdCompetitions
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.CompetitionId &&
                    candidate.BirdId == command.BirdId,
                cancellationToken);
        if (competition is null)
        {
            return UpdateBirdCompetitionResult.CompetitionNotFound();
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            competition.UpdateDetails(
                command.Name,
                command.CompetitionDate,
                command.Category,
                command.Placement,
                command.Location,
                command.Notes,
                DateOnly.FromDateTime(now.UtcDateTime),
                now);
        }
        catch (ArgumentException)
        {
            return UpdateBirdCompetitionResult.InvalidData();
        }

        return UpdateBirdCompetitionResult.Updated(ToResult(competition));
    }

    private static BirdCompetitionResult ToResult(BirdCompetition competition) =>
        new(
            competition.Id,
            competition.BirdId,
            competition.Name,
            competition.CompetitionDate,
            competition.Category,
            competition.Placement,
            competition.Location,
            competition.Notes,
            competition.CreatedAtUtc,
            competition.UpdatedAtUtc);
}
