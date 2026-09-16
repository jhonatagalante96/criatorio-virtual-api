namespace CriatorioVirtual.Domain.BreedingFarms;

public enum BreedingFarmVisualIdentitySource
{
    Upload = 1,
    Template = 2
}

public sealed record BreedingFarmVisualIdentityReference(
    BreedingFarmVisualIdentitySource Source,
    string Reference,
    string? FileName,
    string? ContentType,
    long? Length,
    string? TemplateModelId,
    string? TemplateVersion,
    string? TemplateConfiguration);
