using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Competitions;

public sealed class ListBirdCompetitionsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListBirdCompetitionsQuery, ListBirdCompetitionsResult>
{
    public async Task<ListBirdCompetitionsResult> Handle(
        ListBirdCompetitionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return ListBirdCompetitionsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ListBirdCompetitionsResult.BreedingFarmNotSelected();
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
            return ListBirdCompetitionsResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == query.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return ListBirdCompetitionsResult.BirdNotFound();
        }

        var rows = await dbContext.BirdCompetitions
            .AsNoTracking()
            .Where(competition => competition.BirdId == query.BirdId)
            .OrderByDescending(competition => competition.CompetitionDate)
            .ThenByDescending(competition => competition.CreatedAtUtc)
            .ThenByDescending(competition => competition.Id)
            .Select(competition => new BirdCompetitionProjection(
                competition.Id,
                competition.BirdId,
                competition.Name,
                competition.CompetitionDate,
                competition.Category,
                competition.Placement,
                competition.Location,
                competition.Notes,
                competition.CreatedAtUtc,
                competition.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return ListBirdCompetitionsResult.Succeeded(
            breedingFarmId,
            query.BirdId,
            rows.Select(ToResult).ToArray());
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
