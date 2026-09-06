namespace CriatorioVirtual.Domain.BreedingFarms;

public sealed class BreedingFarmUser
{
    private BreedingFarmUser()
    {
    }

    public BreedingFarmUser(
        Guid breedingFarmId,
        Guid userId,
        BreedingFarmRole role,
        DateTimeOffset createdAtUtc)
    {
        if (breedingFarmId == Guid.Empty)
        {
            throw new ArgumentException("The breeding farm identifier cannot be empty.", nameof(breedingFarmId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier cannot be empty.", nameof(userId));
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be expressed in UTC.", nameof(createdAtUtc));
        }

        BreedingFarmId = breedingFarmId;
        UserId = userId;
        Role = role;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid BreedingFarmId { get; private set; }

    public Guid UserId { get; private set; }

    public BreedingFarmRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
