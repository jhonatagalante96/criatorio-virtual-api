using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Competitions;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Competitions;

public sealed class CreateBirdCompetitionCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<CreateBirdCompetitionCommand, CreateBirdCompetitionResult>
{
    public async Task<CreateBirdCompetitionResult> Handle(
        CreateBirdCompetitionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (command.UserId == Guid.Empty ||
            command.BirdId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Name) ||
            command.Name.Trim().Length > BirdCompetition.NameMaxLength ||
            command.Category?.Trim().Length > BirdCompetition.CategoryMaxLength ||
            command.Location?.Trim().Length > BirdCompetition.LocationMaxLength ||
            command.Notes?.Trim().Length > BirdCompetition.NotesMaxLength ||
            command.CompetitionDate > today ||
            command.Placement is <= 0)
        {
            return CreateBirdCompetitionResult.InvalidData();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return CreateBirdCompetitionResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return CreateBirdCompetitionResult.BreedingFarmNotSelected();
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
            return CreateBirdCompetitionResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == command.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return CreateBirdCompetitionResult.BirdNotFound();
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            var competition = new BirdCompetition(
                Guid.NewGuid(),
                now,
                breedingFarmId,
                command.BirdId,
                command.Name,
                command.CompetitionDate,
                command.Category,
                command.Placement,
                command.Location,
                command.Notes,
                DateOnly.FromDateTime(now.UtcDateTime));

            dbContext.BirdCompetitions.Add(competition);
            return CreateBirdCompetitionResult.Created(ToResult(competition));
        }
        catch (ArgumentException)
        {
            return CreateBirdCompetitionResult.InvalidData();
        }
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
