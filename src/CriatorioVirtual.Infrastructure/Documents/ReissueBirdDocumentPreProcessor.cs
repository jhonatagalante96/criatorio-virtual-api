using System.Text.Json;
using System.Text.Json.Serialization;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class ReissueBirdDocumentPreProcessor(
    CriatorioVirtualDbContext dbContext,
    ICommandPreProcessor<GenerateBirdDocumentCommand> generationPreProcessor,
    ReissueBirdDocumentSession session)
    : ICommandPreProcessor<ReissueBirdDocumentCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task Process(
        ReissueBirdDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty ||
            command.BirdId == Guid.Empty ||
            command.DocumentId == Guid.Empty)
        {
            session.SetStatus(ReissueBirdDocumentStatus.InvalidData);
            return;
        }

        var original = await dbContext.BirdDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                document =>
                    document.Id == command.DocumentId &&
                    document.BirdId == command.BirdId,
                cancellationToken);
        if (original is null)
        {
            session.SetStatus(ReissueBirdDocumentStatus.DocumentNotFound);
            return;
        }

        if (!TryCreateGenerationCommand(original, command, out var generationCommand))
        {
            session.SetStatus(ReissueBirdDocumentStatus.InvalidData);
            return;
        }

        session.SetReady(generationCommand);
        await generationPreProcessor.Process(generationCommand, cancellationToken);
    }

    private static bool TryCreateGenerationCommand(
        BirdDocument original,
        ReissueBirdDocumentCommand command,
        out GenerateBirdDocumentCommand generationCommand)
    {
        generationCommand = null!;
        var hasConfigurationOverride = command.ModelId is not null ||
            command.PrintSize is not null ||
            command.SelectedFields is not null;

        if (original.Type == BirdDocumentType.Badge)
        {
            if (hasConfigurationOverride &&
                (command.ModelId is null ||
                 command.PrintSize is null ||
                 command.SelectedFields is null))
            {
                return false;
            }

            try
            {
                var modelId = command.ModelId ?? original.ModelId;
                var printSize = command.PrintSize ?? original.PrintSize;
                var selectedFields = command.SelectedFields ??
                    JsonSerializer.Deserialize<DocumentField[]>(original.SelectedFieldsJson, JsonOptions);
                if (modelId is null || printSize is null || selectedFields is null)
                {
                    return false;
                }

                var badge = new BadgeRenderConfiguration(modelId.Value, printSize.Value, selectedFields);
                generationCommand = new GenerateBirdDocumentCommand(
                    command.UserId,
                    command.BirdId,
                    original.Type,
                    badge.ModelId,
                    badge.PrintSize,
                    badge.SelectedFields);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if (original.Type is not (BirdDocumentType.GenealogyCertificate or BirdDocumentType.ProvenanceDocument) ||
            hasConfigurationOverride)
        {
            return false;
        }

        generationCommand = new GenerateBirdDocumentCommand(
            command.UserId,
            command.BirdId,
            original.Type,
            null,
            null,
            null);
        return true;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
