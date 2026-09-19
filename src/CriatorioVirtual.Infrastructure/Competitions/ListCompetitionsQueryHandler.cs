using CriatorioVirtual.Application.Competitions;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Competitions;

public sealed class ListCompetitionsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListCompetitionsQuery, ListCompetitionsResult>
{
    public async Task<ListCompetitionsResult> Handle(
        ListCompetitionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return ListCompetitionsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ListCompetitionsResult.BreedingFarmNotSelected();
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
            return ListCompetitionsResult.BreedingFarmNotFound();
        }

        var queryable = dbContext.BirdCompetitions
            .AsNoTracking()
            .Where(competition => competition.BreedingFarmId == breedingFarmId)
            .Join(
                dbContext.Birds.AsNoTracking(),
                competition => competition.BirdId,
                bird => bird.Id,
                (competition, bird) => new { competition, bird });

        if (query.BirdId.HasValue)
        {
            queryable = queryable.Where(x => x.competition.BirdId == query.BirdId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var categoryPattern = EscapeLikePattern(query.Category.Trim());
            queryable = queryable.Where(x =>
                x.competition.Category != null &&
                EF.Functions.ILike(x.competition.Category, categoryPattern, "\\"));
        }

        if (query.FromDate.HasValue)
        {
            queryable = queryable.Where(x =>
                x.competition.CompetitionDate != null &&
                x.competition.CompetitionDate >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            queryable = queryable.Where(x =>
                x.competition.CompetitionDate != null &&
                x.competition.CompetitionDate <= query.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchPattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
            queryable = queryable.Where(x =>
                EF.Functions.ILike(x.competition.Name, searchPattern, "\\") ||
                (x.competition.Location != null && EF.Functions.ILike(x.competition.Location, searchPattern, "\\")) ||
                (x.competition.Category != null && EF.Functions.ILike(x.competition.Category, searchPattern, "\\")) ||
                EF.Functions.ILike(x.bird.Name, searchPattern, "\\") ||
                (x.bird.RingNumber != null && EF.Functions.ILike(x.bird.RingNumber, searchPattern, "\\")));
        }

        var totalCount = await queryable.CountAsync(cancellationToken);
        var skip = (long)(query.Page - 1) * query.PageSize;

        CompetitionListProjection[] rows;
        if (skip > int.MaxValue)
        {
            rows = [];
        }
        else
        {
            rows = await queryable
                .OrderByDescending(x => x.competition.CompetitionDate != null)
                .ThenByDescending(x => x.competition.CompetitionDate)
                .ThenByDescending(x => x.competition.CreatedAtUtc)
                .ThenByDescending(x => x.competition.Id)
                .Skip((int)skip)
                .Take(query.PageSize)
                .Select(x => new CompetitionListProjection(
                    x.competition.Id,
                    x.competition.BreedingFarmId,
                    new CompetitionBirdSummaryProjection(
                        x.bird.Id,
                        x.bird.Name,
                        x.bird.RingNumber),
                    x.competition.Name,
                    x.competition.CompetitionDate,
                    x.competition.Category,
                    x.competition.Placement,
                    x.competition.Location,
                    x.competition.Notes,
                    x.competition.CreatedAtUtc,
                    x.competition.UpdatedAtUtc))
                .ToArrayAsync(cancellationToken);
        }

        return ListCompetitionsResult.Succeeded(
            breedingFarmId,
            rows.Select(ToResult).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    private static CompetitionListItemResult ToResult(CompetitionListProjection item) =>
        new(
            item.Id,
            item.BreedingFarmId,
            new CompetitionBirdSummaryResult(
                item.Bird.BirdId,
                item.Bird.Name,
                item.Bird.RingNumber),
            item.Name,
            item.CompetitionDate,
            item.Category,
            item.Placement,
            item.Location,
            item.Notes,
            item.CreatedAtUtc,
            item.UpdatedAtUtc);

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record CompetitionListProjection(
        Guid Id,
        Guid BreedingFarmId,
        CompetitionBirdSummaryProjection Bird,
        string Name,
        DateOnly? CompetitionDate,
        string? Category,
        int? Placement,
        string? Location,
        string? Notes,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record CompetitionBirdSummaryProjection(
        Guid BirdId,
        string Name,
        string? RingNumber);
}
