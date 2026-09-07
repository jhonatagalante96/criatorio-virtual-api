using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Species;
using SpeciesEntity = CriatorioVirtual.Domain.Species.Species;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Species;

public sealed class SearchSpeciesQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<SearchSpeciesQuery, IReadOnlyCollection<SpeciesSearchResult>>
{
    public async Task<IReadOnlyCollection<SpeciesSearchResult>> Handle(
        SearchSpeciesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedSearch = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : SpeciesEntity.NormalizeForSearch(query.Search);

        var speciesQuery = dbContext.Species
            .AsNoTracking()
            .Where(species => species.IsActive);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            speciesQuery = speciesQuery.Where(species =>
                species.NormalizedPopularName.Contains(normalizedSearch) ||
                species.NormalizedScientificName.Contains(normalizedSearch));
        }

        return await speciesQuery
            .OrderBy(species => species.PopularName)
            .ThenBy(species => species.ScientificName)
            .ThenBy(species => species.Id)
            .Select(species => new SpeciesSearchResult(
                species.Id,
                species.ScientificName,
                species.PopularName))
            .ToArrayAsync(cancellationToken);
    }
}
