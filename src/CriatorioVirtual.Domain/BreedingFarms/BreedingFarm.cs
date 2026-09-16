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

    public string? VisualIdentityReference { get; private set; }

    public BreedingFarmVisualIdentitySource? VisualIdentitySource { get; private set; }

    public string? VisualIdentityFileName { get; private set; }

    public string? VisualIdentityContentType { get; private set; }

    public long? VisualIdentityLength { get; private set; }

    public string? VisualIdentityTemplateModelId { get; private set; }

    public string? VisualIdentityTemplateVersion { get; private set; }

    public string? VisualIdentityTemplateConfiguration { get; private set; }

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

    public BreedingFarmVisualIdentityReference? SetVisualIdentity(
        BreedingFarmVisualIdentitySource source,
        string reference,
        string? fileName,
        string? contentType,
        long? length,
        DateTimeOffset updatedAtUtc,
        string? templateModelId = null,
        string? templateVersion = null,
        string? templateConfiguration = null)
    {
        var normalizedReference = Require(reference, nameof(reference));
        if (normalizedReference.Length > 500)
        {
            throw new ArgumentException("A visual identity reference cannot exceed 500 characters.", nameof(reference));
        }

        switch (source)
        {
            case BreedingFarmVisualIdentitySource.Upload:
                fileName = Require(fileName ?? string.Empty, nameof(fileName));
                contentType = Require(contentType ?? string.Empty, nameof(contentType)).ToLowerInvariant();
                if (fileName.Length > 255 || contentType.Length > 100 || length is null or <= 0 ||
                    templateModelId is not null || templateVersion is not null || templateConfiguration is not null)
                {
                    throw new ArgumentException("Uploaded visual identity metadata is invalid.");
                }

                break;
            case BreedingFarmVisualIdentitySource.Template:
                fileName = Require(fileName ?? string.Empty, nameof(fileName));
                contentType = Require(contentType ?? string.Empty, nameof(contentType)).ToLowerInvariant();
                templateModelId = Require(templateModelId ?? string.Empty, nameof(templateModelId));
                templateVersion = Require(templateVersion ?? string.Empty, nameof(templateVersion));
                templateConfiguration = Require(templateConfiguration ?? string.Empty, nameof(templateConfiguration));
                if (fileName.Length > 255 || contentType != "image/png" || length is null or <= 0 ||
                    length > 10 * 1024 * 1024 || templateModelId.Length > 100 ||
                    templateVersion.Length > 32 || templateConfiguration.Length > 1000)
                {
                    throw new ArgumentException("Template visual identity metadata is invalid.");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, "The visual identity source is not supported.");
        }

        var previous = GetVisualIdentity();
        VisualIdentityReference = normalizedReference;
        VisualIdentitySource = source;
        VisualIdentityFileName = fileName;
        VisualIdentityContentType = contentType;
        VisualIdentityLength = length;
        VisualIdentityTemplateModelId = templateModelId;
        VisualIdentityTemplateVersion = templateVersion;
        VisualIdentityTemplateConfiguration = templateConfiguration;
        Touch(updatedAtUtc);
        return previous;
    }

    public BreedingFarmVisualIdentityReference? RemoveVisualIdentity(DateTimeOffset updatedAtUtc)
    {
        var previous = GetVisualIdentity();
        if (previous is null)
        {
            return null;
        }

        VisualIdentityReference = null;
        VisualIdentitySource = null;
        VisualIdentityFileName = null;
        VisualIdentityContentType = null;
        VisualIdentityLength = null;
        VisualIdentityTemplateModelId = null;
        VisualIdentityTemplateVersion = null;
        VisualIdentityTemplateConfiguration = null;
        Touch(updatedAtUtc);
        return previous;
    }

    public BreedingFarmVisualIdentityReference? GetVisualIdentity() =>
        VisualIdentitySource is null || VisualIdentityReference is null
            ? null
            : new BreedingFarmVisualIdentityReference(
                VisualIdentitySource.Value,
                VisualIdentityReference,
                VisualIdentityFileName,
                VisualIdentityContentType,
                VisualIdentityLength,
                VisualIdentityTemplateModelId,
                VisualIdentityTemplateVersion,
                VisualIdentityTemplateConfiguration);

    private static string Require(string value, string parameterName)
    {
        var normalized = Normalize(value);
        return normalized ?? throw new ArgumentException("A non-empty value is required.", parameterName);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
