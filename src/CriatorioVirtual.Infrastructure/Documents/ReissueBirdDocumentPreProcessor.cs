using System.Text.Json;
using System.Text.Json.Serialization;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
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
        if (!TryReadVisualIdentityOverride(original.SnapshotJson, out var visualIdentityOverride))
        {
            return false;
        }

        var hasConfigurationOverride = command.ModelId is not null ||
            command.PrintSize is not null ||
            command.SelectedFields is not null ||
            command.CertificateModelId is not null;

        if (original.Type == BirdDocumentType.Badge)
        {
            if (hasConfigurationOverride &&
                (command.ModelId is null ||
                 command.PrintSize is null ||
                 command.SelectedFields is null ||
                 command.CertificateModelId is not null))
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
                    badge.SelectedFields,
                    null,
                    null,
                    visualIdentityOverride);
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

        if (original.Type == BirdDocumentType.GenealogyCertificate)
        {
            if (command.ModelId is not null ||
                command.PrintSize is not null ||
                command.SelectedFields is not null)
            {
                return false;
            }

            try
            {
                var certificate = new GenealogyCertificateRenderConfiguration(
                    command.CertificateModelId ??
                    original.CertificateModelId ??
                    GenealogyCertificateRenderConfiguration.DefaultModelId);
                generationCommand = new GenerateBirdDocumentCommand(
                    command.UserId,
                    command.BirdId,
                    original.Type,
                    null,
                    null,
                    null,
                    certificate.ModelId,
                    null,
                    visualIdentityOverride);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        if (original.Type != BirdDocumentType.ProvenanceDocument || hasConfigurationOverride)
        {
            return false;
        }

        generationCommand = new GenerateBirdDocumentCommand(
            command.UserId,
            command.BirdId,
            original.Type,
            null,
            null,
            null,
            null,
            null,
            visualIdentityOverride);
        return true;
    }

    private static bool TryReadVisualIdentityOverride(
        string snapshotJson,
        out BreedingFarmVisualIdentitySnapshotOverride visualIdentityOverride)
    {
        visualIdentityOverride = null!;
        try
        {
            using var snapshot = JsonDocument.Parse(snapshotJson);
            if (!snapshot.RootElement.TryGetProperty("breedingFarmDetails", out var farmDetails) ||
                farmDetails.ValueKind != JsonValueKind.Object ||
                !farmDetails.TryGetProperty("visualIdentity", out var visualIdentity) ||
                visualIdentity.ValueKind == JsonValueKind.Null)
            {
                visualIdentityOverride = new BreedingFarmVisualIdentitySnapshotOverride(null);
                return true;
            }

            if (visualIdentity.ValueKind != JsonValueKind.Object ||
                !visualIdentity.TryGetProperty("source", out var sourceElement) ||
                !visualIdentity.TryGetProperty("objectKey", out var objectKeyElement) ||
                !visualIdentity.TryGetProperty("fileName", out var fileNameElement) ||
                !visualIdentity.TryGetProperty("contentType", out var contentTypeElement) ||
                !visualIdentity.TryGetProperty("length", out var lengthElement) ||
                !Enum.TryParse<BreedingFarmVisualIdentitySource>(sourceElement.GetString(), true, out var source) ||
                !Enum.IsDefined(source) ||
                string.IsNullOrWhiteSpace(objectKeyElement.GetString()) ||
                string.IsNullOrWhiteSpace(fileNameElement.GetString()) ||
                string.IsNullOrWhiteSpace(contentTypeElement.GetString()) ||
                !lengthElement.TryGetInt64(out var length) ||
                length <= 0)
            {
                return false;
            }

            visualIdentityOverride = new BreedingFarmVisualIdentitySnapshotOverride(
                new BreedingFarmVisualIdentityDocumentReference(
                    source,
                    objectKeyElement.GetString()!,
                    fileNameElement.GetString()!,
                    contentTypeElement.GetString()!,
                    length));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
