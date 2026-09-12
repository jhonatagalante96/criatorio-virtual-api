using CriatorioVirtual.Domain.Transfers;
using Xunit;

namespace CriatorioVirtual.Domain.Tests.Transfers;

public sealed class ExternalTransferTests
{
    [Fact]
    public void ConstructorNormalizesRecipientAndNotes()
    {
        var completedAt = DateTimeOffset.UtcNow;
        var transfer = new ExternalTransfer(
            Guid.NewGuid(),
            completedAt,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Recebedor externo  ",
            "  Entrega confirmada  ");

        Assert.Equal("Recebedor externo", transfer.RecipientName);
        Assert.Equal("Entrega confirmada", transfer.Notes);
        Assert.Equal(completedAt, transfer.CreatedAtUtc);
        Assert.Equal(completedAt, transfer.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsBlankRecipient(string recipientName)
    {
        Assert.Throws<ArgumentException>(() => new ExternalTransfer(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            recipientName,
            null));
    }
}
