using CriatorioVirtual.Domain.Primitives;

namespace CriatorioVirtual.Domain.BreedingFarms;

public sealed class BreedingFarm : Entity
{
    private BreedingFarm()
        : base(Guid.NewGuid(), DateTimeOffset.UnixEpoch)
    {
        Address = new BreedingFarmAddress(null, null, null, null, null, null, null);
    }

    public BreedingFarm(
        Guid id,
        DateTimeOffset createdAtUtc,
        string name,
        string responsibleName,
        string contactEmail,
        string? contactPhone,
        string? officialRegistrationNumber,
        BreedingFarmAddress address)
        : base(id, createdAtUtc)
    {
        Name = Require(name, nameof(name));
        ResponsibleName = Require(responsibleName, nameof(responsibleName));
        ContactEmail = Require(contactEmail, nameof(contactEmail));
        ContactPhone = Normalize(contactPhone);
        OfficialRegistrationNumber = Normalize(officialRegistrationNumber);
        Address = address ?? throw new ArgumentNullException(nameof(address));
    }

    public string Name { get; private set; } = null!;

    public string ResponsibleName { get; private set; } = null!;

    public string ContactEmail { get; private set; } = null!;

    public string? ContactPhone { get; private set; }

    public string? OfficialRegistrationNumber { get; private set; }

    public BreedingFarmAddress Address { get; private set; } = null!;

    public void UpdateSettings(
        string name,
        string responsibleName,
        string contactEmail,
        string? contactPhone,
        string? officialRegistrationNumber,
        BreedingFarmAddress address,
        DateTimeOffset updatedAtUtc)
    {
        Name = Require(name, nameof(name));
        ResponsibleName = Require(responsibleName, nameof(responsibleName));
        ContactEmail = Require(contactEmail, nameof(contactEmail));
        ContactPhone = Normalize(contactPhone);
        OfficialRegistrationNumber = Normalize(officialRegistrationNumber);
        Address = address ?? throw new ArgumentNullException(nameof(address));
        Touch(updatedAtUtc);
    }

    private static string Require(string value, string parameterName)
    {
        var normalized = Normalize(value);
        return normalized ?? throw new ArgumentException("A non-empty value is required.", parameterName);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
