using CriatorioVirtual.Application.Documents;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class ReissueBirdDocumentSession
{
    public ReissueBirdDocumentStatus Status { get; private set; } = ReissueBirdDocumentStatus.InvalidData;

    public GenerateBirdDocumentCommand? GenerationCommand { get; private set; }

    public void SetStatus(ReissueBirdDocumentStatus status)
    {
        Status = status;
        GenerationCommand = null;
    }

    public void SetReady(GenerateBirdDocumentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Status = ReissueBirdDocumentStatus.Reissued;
        GenerationCommand = command;
    }
}
