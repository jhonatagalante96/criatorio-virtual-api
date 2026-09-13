using System.Text.Json;
using System.Text.Json.Serialization;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class GenerateBirdDocumentPreProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    IDocumentRenderer renderer,
    IQueryHandler<GetBirdGenealogyQuery, GetBirdGenealogyResult> genealogyHandler,
    BirdDocumentGenerationSession session)
    : ICommandPreProcessor<GenerateBirdDocumentCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task Process(
        GenerateBirdDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsValidCommand(command, out var badge, out var selectedFields))
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            session.SetStatus(GenerateBirdDocumentStatus.UserNotFound);
            return;
        }

        if (user.SelectedBreedingFarmId is null)
        {
            session.SetStatus(GenerateBirdDocumentStatus.BreedingFarmNotSelected);
            return;
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            session.SetStatus(GenerateBirdDocumentStatus.BreedingFarmNotFound);
            return;
        }

        var bird = await dbContext.Birds
            .AsNoTracking()
            .Where(candidate => candidate.Id == command.BirdId && candidate.BreedingFarmId == breedingFarmId)
            .Join(
                dbContext.Species.AsNoTracking(),
                candidate => candidate.SpeciesId,
                species => species.Id,
                (candidate, species) => new { Bird = candidate, Species = species })
            .Join(
                dbContext.BreedingFarms.AsNoTracking(),
                candidate => candidate.Bird.BreedingFarmId,
                farm => farm.Id,
                (candidate, farm) => new BirdGenerationProjection(
                    candidate.Bird.Id,
                    candidate.Bird.BreedingFarmId,
                    candidate.Bird.Name,
                    candidate.Bird.RingNumber,
                    candidate.Bird.Sex,
                    candidate.Bird.BirthDate,
                    candidate.Bird.PrimaryPhotoId,
                    candidate.Species.ScientificName,
                    farm.Name,
                    farm.ResponsibleName,
                    farm.ContactEmail,
                    farm.ContactPhone,
                    farm.OfficialRegistrationNumber))
            .SingleOrDefaultAsync(cancellationToken);
        if (bird is null)
        {
            session.SetStatus(GenerateBirdDocumentStatus.BirdNotFound);
            return;
        }

        if ((command.Type is BirdDocumentType.Badge or BirdDocumentType.GenealogyCertificate or BirdDocumentType.ProvenanceDocument) &&
            bird.RingNumber is null)
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }

        var generatedAtUtc = DateTimeOffset.UtcNow;
        var renderFields = command.Type is BirdDocumentType.GenealogyCertificate or BirdDocumentType.ProvenanceDocument
            ? new[] { DocumentField.GenealogyTree }
            : selectedFields;
        var genealogy = await GetGenealogyAsync(command, renderFields, cancellationToken);
        if (genealogy.Status != GetBirdGenealogyStatus.Success)
        {
            session.SetStatus(ToGenerationStatus(genealogy.Status));
            return;
        }

        DocumentPhotoSnapshot? photo;
        try
        {
            photo = await LoadPrimaryPhotoAsync(
                bird,
                breedingFarmId,
                selectedFields,
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.StorageUnavailable);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.StorageUnavailable);
            return;
        }
        catch (IOException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.StorageUnavailable);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.StorageUnavailable);
            return;
        }

        BirdDocumentSnapshot snapshot;
        try
        {
            snapshot = new BirdDocumentSnapshot(
                bird.BirdId,
                bird.Name,
                bird.RingNumber,
                bird.Sex,
                bird.SpeciesName,
                bird.BirthDate,
                bird.BreedingFarmName,
                photo,
                genealogy.Genealogy!.Nodes
                    .Where(node => node.Position != GenealogyNode.RootPosition)
                    .Select(node => new GenealogySnapshotNode(
                        node.Position,
                        node.Name,
                        node.RingNumber,
                        node.Sex,
                        node.BirthDate))
                    .ToArray(),
                command.Type is BirdDocumentType.GenealogyCertificate or BirdDocumentType.ProvenanceDocument
                    ? new BreedingFarmDocumentSnapshot(
                        bird.ResponsibleName,
                        bird.ContactEmail,
                        bird.ContactPhone,
                        bird.OfficialRegistrationNumber)
                    : null,
                command.Type == BirdDocumentType.ProvenanceDocument ? generatedAtUtc : null);
        }
        catch (ArgumentException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }

        var renderRequest = new DocumentRenderRequest(command.Type, snapshot, badge);
        RenderedDocument rendered;
        try
        {
            rendered = await renderer.RenderAsync(renderRequest, cancellationToken);
        }
        catch (ArgumentException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }

        if (rendered.Content is null ||
            rendered.Content.Length == 0 ||
            !PrivateObjectStorageFileValidation.TryValidateMetadata(
                rendered.FileName,
                rendered.ContentType,
                out _))
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }

        var documentId = Guid.NewGuid();
        var objectKey = $"birds/{bird.BirdId:N}/documents/{documentId:N}.pdf";
        var selectedFieldsJson = JsonSerializer.Serialize(selectedFields, JsonOptions);
        var snapshotJson = SerializeSnapshot(snapshot);
        if (selectedFieldsJson.Length > 1_000_000 || snapshotJson.Length > 1_000_000)
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }

        PrivateObjectDescriptor storedObject;
        try
        {
            await using var renderedContent = new MemoryStream(rendered.Content, writable: false);
            storedObject = await storage.PutAsync(
                new PrivateObjectUpload(
                    breedingFarmId,
                    objectKey,
                    rendered.FileName,
                    rendered.ContentType,
                    renderedContent),
                cancellationToken);
        }
        catch (ArgumentException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }
        catch (IOException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.StorageUnavailable);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.StorageUnavailable);
            return;
        }

        session.SetPrepared(
            new BirdDocumentPreparation(
                documentId,
                bird.BirdId,
                breedingFarmId,
                command.Type,
                command.ModelId,
                command.PrintSize,
                selectedFields,
                selectedFieldsJson,
                snapshotJson,
                rendered.FileName,
                storedObject.ContentType,
                storedObject.Length,
                rendered.PageCount,
                rendered.WidthMillimeters,
                rendered.HeightMillimeters,
                generatedAtUtc),
            storedObject);
    }

    private async Task<GetBirdGenealogyResult> GetGenealogyAsync(
        GenerateBirdDocumentCommand command,
        IReadOnlyCollection<DocumentField> renderFields,
        CancellationToken cancellationToken)
    {
        if (!renderFields.Contains(DocumentField.GenealogyTree))
        {
            return GetBirdGenealogyResult.Succeeded(
                new BirdGenealogyResult(
                    Guid.Empty,
                    command.BirdId,
                    0,
                    false,
                    [],
                    []));
        }

        return await genealogyHandler.Handle(
            new GetBirdGenealogyQuery(
                command.UserId,
                command.BirdId,
                BirdGenealogyLimits.MaxGenerations),
            cancellationToken);
    }

    private async Task<DocumentPhotoSnapshot?> LoadPrimaryPhotoAsync(
        BirdGenerationProjection bird,
        Guid breedingFarmId,
        IReadOnlyCollection<DocumentField> renderFields,
        CancellationToken cancellationToken)
    {
        if (!renderFields.Contains(DocumentField.BirdPhoto) || bird.PrimaryPhotoId is not { } primaryPhotoId)
        {
            return null;
        }

        var attachment = await dbContext.BirdAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == primaryPhotoId &&
                    candidate.BirdId == bird.BirdId &&
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.DeletedAtUtc == null,
                cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        await using var content = await storage.OpenReadAsync(
            breedingFarmId,
            attachment.ObjectKey,
            cancellationToken);
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return new DocumentPhotoSnapshot(
            attachment.FileName,
            attachment.ContentType,
            buffer.ToArray());
    }

    private static bool IsValidCommand(
        GenerateBirdDocumentCommand command,
        out BadgeRenderConfiguration? badge,
        out IReadOnlyCollection<DocumentField> selectedFields)
    {
        badge = null;
        selectedFields = [];
        if (command.UserId == Guid.Empty ||
            command.BirdId == Guid.Empty ||
            command.Type is not (BirdDocumentType.Badge or BirdDocumentType.GenealogyCertificate or BirdDocumentType.ProvenanceDocument))
        {
            return false;
        }

        if (command.Type == BirdDocumentType.Badge)
        {
            if (command.ModelId is null ||
                command.PrintSize is null ||
                command.SelectedFields is null)
            {
                return false;
            }

            try
            {
                badge = new BadgeRenderConfiguration(
                    command.ModelId.Value,
                    command.PrintSize.Value,
                    command.SelectedFields);
            }
            catch (ArgumentException)
            {
                return false;
            }

            selectedFields = badge.SelectedFields;
            return true;
        }

        if (command.ModelId is not null ||
            command.PrintSize is not null ||
            command.SelectedFields is { Count: > 0 })
        {
            return false;
        }

        selectedFields = [];
        return true;
    }

    private static GenerateBirdDocumentStatus ToGenerationStatus(GetBirdGenealogyStatus status) =>
        status switch
        {
            GetBirdGenealogyStatus.UserNotFound => GenerateBirdDocumentStatus.UserNotFound,
            GetBirdGenealogyStatus.BreedingFarmNotSelected => GenerateBirdDocumentStatus.BreedingFarmNotSelected,
            GetBirdGenealogyStatus.BreedingFarmNotFound => GenerateBirdDocumentStatus.BreedingFarmNotFound,
            GetBirdGenealogyStatus.BirdNotFound => GenerateBirdDocumentStatus.BirdNotFound,
            _ => GenerateBirdDocumentStatus.InvalidData
        };

    private static string SerializeSnapshot(BirdDocumentSnapshot snapshot) =>
        JsonSerializer.Serialize(
            new PersistedSnapshot(
                snapshot.BirdId,
                snapshot.Name,
                snapshot.RingNumber,
                snapshot.Sex,
                snapshot.Species,
                snapshot.BirthDate,
                snapshot.BreedingFarmName,
                snapshot.Photo is null
                    ? null
                    : new PersistedPhoto(snapshot.Photo.FileName, snapshot.Photo.ContentType),
                snapshot.Genealogy,
                snapshot.BreedingFarmDetails,
                snapshot.IssuedAtUtc),
            JsonOptions);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record BirdGenerationProjection(
        Guid BirdId,
        Guid BreedingFarmId,
        string Name,
        string? RingNumber,
        BirdSex Sex,
        DateOnly? BirthDate,
        Guid? PrimaryPhotoId,
        string SpeciesName,
        string BreedingFarmName,
        string ResponsibleName,
        string ContactEmail,
        string? ContactPhone,
        string? OfficialRegistrationNumber);

    private sealed record PersistedSnapshot(
        Guid BirdId,
        string Name,
        string? RingNumber,
        BirdSex Sex,
        string Species,
        DateOnly? BirthDate,
        string BreedingFarmName,
        PersistedPhoto? Photo,
        IReadOnlyCollection<GenealogySnapshotNode> Genealogy,
        BreedingFarmDocumentSnapshot? BreedingFarmDetails,
        DateTimeOffset? IssuedAtUtc);

    private sealed record PersistedPhoto(string FileName, string ContentType);
}

public sealed class GenerateBirdDocumentCommandHandler(
    CriatorioVirtualDbContext dbContext,
    BirdDocumentGenerationSession session)
    : ICommandHandler<GenerateBirdDocumentCommand, GenerateBirdDocumentResult>
{
    public Task<GenerateBirdDocumentResult> Handle(
        GenerateBirdDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (session.Status != GenerateBirdDocumentStatus.Generated ||
            session.Preparation is null ||
            session.StoredObject is null)
        {
            return Task.FromResult(session.Status switch
            {
                GenerateBirdDocumentStatus.UserNotFound => GenerateBirdDocumentResult.UserNotFound(),
                GenerateBirdDocumentStatus.BreedingFarmNotSelected => GenerateBirdDocumentResult.BreedingFarmNotSelected(),
                GenerateBirdDocumentStatus.BreedingFarmNotFound => GenerateBirdDocumentResult.BreedingFarmNotFound(),
                GenerateBirdDocumentStatus.BirdNotFound => GenerateBirdDocumentResult.BirdNotFound(),
                GenerateBirdDocumentStatus.StorageUnavailable => GenerateBirdDocumentResult.StorageUnavailable(),
                _ => GenerateBirdDocumentResult.InvalidData()
            });
        }

        var preparation = session.Preparation;
        var document = preparation.Type switch
        {
            BirdDocumentType.Badge => BirdDocument.CreateBadge(
                preparation.DocumentId,
                preparation.GeneratedAtUtc,
                preparation.BirdId,
                preparation.BreedingFarmId,
                preparation.ModelId!.Value,
                preparation.PrintSize!.Value,
                session.StoredObject.ObjectKey,
                preparation.FileName,
                preparation.ContentType,
                preparation.Length,
                preparation.GeneratedAtUtc,
                preparation.SelectedFieldsJson,
                preparation.SnapshotJson),
            BirdDocumentType.GenealogyCertificate => BirdDocument.CreateGenealogyCertificate(
                preparation.DocumentId,
                preparation.GeneratedAtUtc,
                preparation.BirdId,
                preparation.BreedingFarmId,
                session.StoredObject.ObjectKey,
                preparation.FileName,
                preparation.ContentType,
                preparation.Length,
                preparation.GeneratedAtUtc,
                preparation.SelectedFieldsJson,
                preparation.SnapshotJson),
            BirdDocumentType.ProvenanceDocument => BirdDocument.CreateProvenanceDocument(
                preparation.DocumentId,
                preparation.GeneratedAtUtc,
                preparation.BirdId,
                preparation.BreedingFarmId,
                session.StoredObject.ObjectKey,
                preparation.FileName,
                preparation.ContentType,
                preparation.Length,
                preparation.GeneratedAtUtc,
                preparation.SelectedFieldsJson,
                preparation.SnapshotJson),
            _ => throw new InvalidOperationException("The prepared document type is invalid.")
        };

        dbContext.BirdDocuments.Add(document);
        return Task.FromResult(
            GenerateBirdDocumentResult.Generated(
                new BirdDocumentResult(
                    document.Id,
                    document.BirdId,
                    document.Type,
                    document.ModelId,
                    document.PrintSize,
                    preparation.SelectedFields,
                    document.FileName,
                    document.ContentType,
                    document.Length,
                    preparation.PageCount,
                    preparation.WidthMillimeters,
                    preparation.HeightMillimeters,
                    document.GeneratedAtUtc)));
    }
}
