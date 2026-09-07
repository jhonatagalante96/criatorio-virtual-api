using System.Globalization;
using System.Text;
using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Species;

public sealed class Species : Entity
{
    private Species()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        ScientificName = null!;
        PopularName = null!;
        NormalizedScientificName = null!;
        NormalizedPopularName = null!;
    }

    public Species(
        Guid id,
        DateTimeOffset createdAtUtc,
        string scientificName,
        string popularName,
        bool isActive)
        : base(id, createdAtUtc)
    {
        ScientificName = RequireName(scientificName, nameof(scientificName));
        PopularName = RequireName(popularName, nameof(popularName));
        NormalizedScientificName = NormalizeForSearch(ScientificName);
        NormalizedPopularName = NormalizeForSearch(PopularName);
        IsActive = isActive;
    }

    public string ScientificName { get; private set; } = null!;

    public string PopularName { get; private set; } = null!;

    public string NormalizedScientificName { get; private set; } = null!;

    public string NormalizedPopularName { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public static string NormalizeForSearch(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    private static string RequireName(string value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A non-empty species name is required.", parameterName);
        }

        if (normalized.Length > 200)
        {
            throw new ArgumentException("A species name cannot exceed 200 characters.", parameterName);
        }

        return normalized;
    }
}
