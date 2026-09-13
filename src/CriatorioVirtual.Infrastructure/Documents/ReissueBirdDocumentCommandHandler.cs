using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class ReissueBirdDocumentCommandHandler(
    ICommandHandler<GenerateBirdDocumentCommand, GenerateBirdDocumentResult> generationHandler,
    ReissueBirdDocumentSession session)
    : ICommandHandler<ReissueBirdDocumentCommand, ReissueBirdDocumentResult>
{
    public async Task<ReissueBirdDocumentResult> Handle(
        ReissueBirdDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (session.Status != ReissueBirdDocumentStatus.Reissued ||
            session.GenerationCommand is null)
        {
            return session.Status switch
            {
                ReissueBirdDocumentStatus.DocumentNotFound => ReissueBirdDocumentResult.DocumentNotFound(),
                _ => ReissueBirdDocumentResult.InvalidData()
            };
        }

        var result = await generationHandler.Handle(session.GenerationCommand, cancellationToken);
        return result.Status switch
        {
            GenerateBirdDocumentStatus.Generated => ReissueBirdDocumentResult.Reissued(result.Document!),
            GenerateBirdDocumentStatus.UserNotFound => ReissueBirdDocumentResult.UserNotFound(),
            GenerateBirdDocumentStatus.BreedingFarmNotSelected => ReissueBirdDocumentResult.BreedingFarmNotSelected(),
            GenerateBirdDocumentStatus.BreedingFarmNotFound => ReissueBirdDocumentResult.BreedingFarmNotFound(),
            GenerateBirdDocumentStatus.BirdNotFound => ReissueBirdDocumentResult.BirdNotFound(),
            GenerateBirdDocumentStatus.StorageUnavailable => ReissueBirdDocumentResult.StorageUnavailable(),
            _ => ReissueBirdDocumentResult.InvalidData()
        };
    }
}
