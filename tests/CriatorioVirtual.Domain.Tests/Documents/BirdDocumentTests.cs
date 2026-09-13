using CriatorioVirtual.Domain.Documents;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Documents;

public sealed class BirdDocumentTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateBadge_RequiresModelAndPrintSize()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BirdDocument.CreateBadge(
                Guid.NewGuid(),
                Timestamp,
                Guid.NewGuid(),
                Guid.NewGuid(),
                (BadgeModelId)99,
                BadgePrintSize.Small,
                "documents/bird.pdf",
                "bird.pdf",
                "application/pdf",
                12,
                Timestamp,
                "[1]",
                "{}"));

        Assert.Contains("model", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateBadge_PersistsConfigurationAndSnapshotMetadata()
    {
        var document = BirdDocument.CreateBadge(
            Guid.NewGuid(),
            Timestamp,
            Guid.NewGuid(),
            Guid.NewGuid(),
            BadgeModelId.Classic,
            BadgePrintSize.Medium,
            "documents/bird.pdf",
            "bird.pdf",
            "APPLICATION/PDF",
            12,
            Timestamp,
            "[1,2]",
            "{\"name\":\"Luna\"}");

        Assert.Equal(BirdDocumentType.Badge, document.Type);
        Assert.Equal(BadgeModelId.Classic, document.ModelId);
        Assert.Equal(BadgePrintSize.Medium, document.PrintSize);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal("[1,2]", document.SelectedFieldsJson);
        Assert.Equal("{\"name\":\"Luna\"}", document.SnapshotJson);
    }

    [Fact]
    public void CreateInternalRecord_DoesNotAcceptBadgeConfiguration()
    {
        var document = BirdDocument.CreateInternalRecord(
            Guid.NewGuid(),
            Timestamp,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "documents/record.pdf",
            "record.pdf",
            "application/pdf",
            12,
            Timestamp,
            "[]",
            "{}");

        Assert.Equal(BirdDocumentType.InternalRecord, document.Type);
        Assert.Null(document.ModelId);
        Assert.Null(document.PrintSize);
    }

    [Fact]
    public void CreateBadge_RejectsUnsafeObjectKey()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BirdDocument.CreateBadge(
                Guid.NewGuid(),
                Timestamp,
                Guid.NewGuid(),
                Guid.NewGuid(),
                BadgeModelId.Classic,
                BadgePrintSize.Small,
                "../bird.pdf",
                "bird.pdf",
                "application/pdf",
                12,
                Timestamp,
                "[]",
                "{}"));

        Assert.Contains("safe relative path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateBadge_OnlyAcceptsPdfDocuments()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BirdDocument.CreateBadge(
                Guid.NewGuid(),
                Timestamp,
                Guid.NewGuid(),
                Guid.NewGuid(),
                BadgeModelId.Classic,
                BadgePrintSize.Small,
                "documents/bird.pdf",
                "bird.pdf",
                "text/plain",
                12,
                Timestamp,
                "[]",
                "{}"));

        Assert.Contains("PDF", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
