using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Competitions;

public sealed class GetBirdCompetitionQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBirdCompetitionQuery, GetBirdCompetitionResult>
{
    public async Task<GetBirdCompetitionResult> Handle(
        GetBirdCompetitionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetBirdCompetitionResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetBirdCompetitionResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == query.UserId &&
                    membership.IsActive,
                cancellationToken);
        if (!hasActiveMembership)
        {
            return GetBirdCompetitionResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == query.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return GetBirdCompetitionResult.BirdNotFound();
        }

        var competition = await dbContext.BirdCompetitions
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == query.CompetitionId &&
                candidate.BirdId == query.BirdId)
            .Select(candidate => new BirdCompetitionProjection(
                candidate.Id,
                candidate.BirdId,
                candidate.Name,
                candidate.CompetitionDate,
                candidate.Category,
                candidate.Placement,
                candidate.Location,
                candidate.Notes,
                candidate.CreatedAtUtc,
                candidate.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        return competition is null
            ? GetBirdCompetitionResult.CompetitionNotFound()
            : GetBirdCompetitionResult.Succeeded(ToResult(competition));
    }

    private static BirdCompetitionResult ToResult(BirdCompetitionProjection competition) =>
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

    private sealed record BirdCompetitionProjection(
        Guid Id,
        Guid BirdId,
        string Name,
        DateOnly? CompetitionDate,
        string? Category,
        int? Placement,
        string? Location,
        string? Notes,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
