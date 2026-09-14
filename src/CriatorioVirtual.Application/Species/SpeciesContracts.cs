using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Species;

public sealed record SearchSpeciesQuery(string? Search) : IQuery<IReadOnlyCollection<SpeciesSearchResult>>;

public sealed record SpeciesSearchResult(
    Guid SpeciesId,
    string ScientificName,
    string PopularName,
    string? DefaultImageFileName = null);
