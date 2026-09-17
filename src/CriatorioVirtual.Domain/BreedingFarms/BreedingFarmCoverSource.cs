namespace CriatorioVirtual.Domain.BreedingFarms;

public enum BreedingFarmCoverSource
{
    Upload = 1,
    Template = 2
}

public sealed record BreedingFarmCoverReference(
    BreedingFarmCoverSource Source,
    string Reference,
    string FileName,
    string ContentType,
    long Length,
    string? TemplateModelId,
    string? TemplateVersion,
    string? TemplateConfiguration,
    DateTimeOffset UpdatedAtUtc);
