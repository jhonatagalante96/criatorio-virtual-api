using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class SearchBirdParentOptionsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<SearchBirdParentOptionsQuery, SearchBirdParentOptionsResult>
{
    public async Task<SearchBirdParentOptionsResult> Handle(
        SearchBirdParentOptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return SearchBirdParentOptionsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return SearchBirdParentOptionsResult.BreedingFarmNotSelected();
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
            return SearchBirdParentOptionsResult.BreedingFarmNotFound();
        }

        var birds = dbContext.Birds
            .AsNoTracking()
            .Where(candidate =>
                candidate.BreedingFarmId == breedingFarmId &&
                candidate.Status == Domain.Birds.BirdStatus.Active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchPattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
            birds = birds.Where(candidate =>
                EF.Functions.ILike(candidate.Name, searchPattern, "\\") ||
                (candidate.RingNumber != null && EF.Functions.ILike(candidate.RingNumber, searchPattern, "\\")));
        }

        if (query.Sex is not null)
        {
            birds = birds.Where(candidate => candidate.Sex == query.Sex.Value);
        }

        var items = await birds
            .OrderBy(candidate => candidate.Name)
            .ThenBy(candidate => candidate.RingNumber)
            .ThenBy(candidate => candidate.Id)
            .Take(query.Limit)
            .Select(candidate => new BirdParentOptionResult(
                candidate.Id,
                candidate.Name,
                candidate.Sex,
                candidate.BirthDate,
                candidate.RingNumber))
            .ToArrayAsync(cancellationToken);

        return SearchBirdParentOptionsResult.Succeeded(breedingFarmId, items);
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
