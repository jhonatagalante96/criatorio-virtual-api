using CriatorioVirtual.Domain.Birds;

namespace CriatorioVirtual.Domain.Reproductions;

public sealed record ReproductionParticipantSnapshot(
    Guid BirdId,
    string Name,
    BirdSex Sex,
    DateOnly? BirthDate,
    string? RingNumber,
    BirdStatus Status);
