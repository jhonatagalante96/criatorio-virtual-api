using System.Text;
using CriatorioVirtual.Application.Reports;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Reports;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Reports;

public sealed class BirdsReportPdfRendererTests
{
    [Fact]
    public async Task RenderAsync_ProducesPortraitMultipagePdfWithAllReportSections()
    {
        var matrixMaleItems = Enumerable.Range(1, 20)
            .Select(index => CreateBirdItem($"Matrix {index:00}", BirdSex.Male, index % 2 == 0))
            .ToArray();
        var offspringFemaleItems = Enumerable.Range(1, 20)
            .Select(index => CreateBirdItem($"Offspring {index:00}", BirdSex.Female, index % 2 == 0))
            .ToArray();
        var report = new BirdsReportResult(
            Guid.NewGuid(),
            "Farm North",
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            [
                new BirdReportGroupResult(BirdReportClassification.Matrix, BirdSex.Male, matrixMaleItems),
                new BirdReportGroupResult(BirdReportClassification.Matrix, BirdSex.Female, []),
                new BirdReportGroupResult(BirdReportClassification.Matrix, BirdSex.Unknown, []),
                new BirdReportGroupResult(BirdReportClassification.Offspring, BirdSex.Male, []),
                new BirdReportGroupResult(BirdReportClassification.Offspring, BirdSex.Female, offspringFemaleItems),
                new BirdReportGroupResult(BirdReportClassification.Offspring, BirdSex.Unknown, [])
            ]);

        var rendered = await new BirdsReportPdfRenderer().RenderAsync(report);
        var pdf = Encoding.ASCII.GetString(rendered.Content);

        Assert.Equal("application/pdf", rendered.ContentType);
        Assert.Equal("birds-report.pdf", rendered.FileName);
        Assert.Equal(210, rendered.WidthMillimeters);
        Assert.Equal(297, rendered.HeightMillimeters);
        Assert.True(rendered.PageCount > 1);
        Assert.Equal("%PDF-1.4", pdf[..8]);
        Assert.Contains(ToHex("Registered birds report"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Matrices - Male"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Offspring - Female"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("No birds in this group."), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Total birds: 40"), pdf, StringComparison.Ordinal);
        Assert.Contains(ToHex("Not informed"), pdf, StringComparison.Ordinal);
    }

    private static BirdReportItemResult CreateBirdItem(string name, BirdSex sex, bool withOptionalData) =>
        new(
            Guid.NewGuid(),
            name,
            withOptionalData ? "123456" : null,
            "Turdus rufiventris",
            "Sabiá-laranjeira",
            sex,
            withOptionalData ? new DateOnly(2024, 2, 3) : null,
            BirdStatus.Active);

    private static string ToHex(string value) =>
        Convert.ToHexString(Encoding.ASCII.GetBytes(value.Normalize(NormalizationForm.FormD)
            .Where(character => character <= 127)
            .ToArray()));
}
