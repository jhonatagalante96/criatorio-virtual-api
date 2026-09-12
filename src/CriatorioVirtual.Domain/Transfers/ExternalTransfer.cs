using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.Transfers;

public sealed class ExternalTransfer : Entity
{
    private ExternalTransfer()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        RecipientName = null!;
    }

    public ExternalTransfer(
        Guid id,
        DateTimeOffset completedAtUtc,
        Guid breedingFarmId,
        Guid birdId,
        string recipientName,
        string? notes)
        : base(id, completedAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        if (birdId == Guid.Empty)
        {
            throw new ArgumentException("The bird identifier cannot be empty.", nameof(birdId));
        }

        RecipientName = RequireRecipientName(recipientName);
        Notes = NormalizeNotes(notes);
        BreedingFarmId = breedingFarmId;
        BirdId = birdId;
    }

    public Guid BreedingFarmId { get; private set; }

    public Guid BirdId { get; private set; }

    public string RecipientName { get; private set; } = null!;

    public string? Notes { get; private set; }

    private static string RequireRecipientName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A non-empty external transfer recipient name is required.", nameof(value));
        }

        if (normalized.Length > 200)
        {
            throw new ArgumentException(
                "An external transfer recipient name cannot exceed 200 characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeNotes(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 2000)
        {
            throw new ArgumentException("External transfer notes cannot exceed 2000 characters.", nameof(value));
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
