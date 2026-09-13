using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;

namespace CriatorioVirtual.Application.Reports;

public sealed record GenerateBirdsReportQuery(
    Guid UserId,
    BirdStatus? Status,
    BirdSex? Sex,
    Guid? SpeciesId) : IQuery<GenerateBirdsReportResult>;

public enum GenerateBirdsReportStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public enum BirdReportClassification
{
    Matrix = 1,
    Offspring = 2
}

public sealed record GenerateBirdsReportResult(
    GenerateBirdsReportStatus Status,
    BirdsReportResult? Report)
{
    public static GenerateBirdsReportResult Succeeded(BirdsReportResult report) =>
        new(GenerateBirdsReportStatus.Success, report);

    public static GenerateBirdsReportResult UserNotFound() =>
        new(GenerateBirdsReportStatus.UserNotFound, null);

    public static GenerateBirdsReportResult BreedingFarmNotSelected() =>
        new(GenerateBirdsReportStatus.BreedingFarmNotSelected, null);

    public static GenerateBirdsReportResult BreedingFarmNotFound() =>
        new(GenerateBirdsReportStatus.BreedingFarmNotFound, null);
}

public sealed record BirdsReportResult(
    Guid BreedingFarmId,
    string BreedingFarmName,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyCollection<BirdReportGroupResult> Groups)
{
    public int TotalCount => Groups.Sum(group => group.Items.Count);
}

public sealed record BirdReportGroupResult(
    BirdReportClassification Classification,
    BirdSex Sex,
    IReadOnlyCollection<BirdReportItemResult> Items);

public sealed record BirdReportItemResult(
    Guid BirdId,
    string Name,
    string? RingNumber,
    string SpeciesScientificName,
    string SpeciesPopularName,
    BirdSex Sex,
    DateOnly? BirthDate,
    BirdStatus Status);

public sealed record RenderedBirdsReport(
    byte[] Content,
    string FileName,
    string ContentType,
    int PageCount,
    double WidthMillimeters,
    double HeightMillimeters);

public interface IBirdsReportRenderer
{
    Task<RenderedBirdsReport> RenderAsync(
        BirdsReportResult report,
        CancellationToken cancellationToken = default);
}
