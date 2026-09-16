using System.Text.Json;
using System.Text.Json.Serialization;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class GenerateBirdDocumentPreProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    ISpeciesDefaultImageReader speciesDefaultImageReader,
    IDocumentRenderer renderer,
    IQueryHandler<GetBirdGenealogyQuery, GetBirdGenealogyResult> genealogyHandler,
    BirdDocumentGenerationSession session)
    : ICommandPreProcessor<GenerateBirdDocumentCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly IReadOnlySet<string> BadgeGenealogyPhotoPositions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "father",
            "mother",
            "father.father",
            "father.mother",
            "mother.father",
            "mother.mother"
        };
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public async Task Process(
        GenerateBirdDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsValidCommand(command, out var badge, out var certificate, out var selectedFields))
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
                    candidate.Bird.DefaultImageFileName,
                    candidate.Bird.DefaultImageContentType,
                    candidate.Species.Id,
                    candidate.Species.DefaultImageFileName,
                    candidate.Species.DefaultImageContentType,
                    candidate.Species.ScientificName,
                    farm.Name,
                    farm.ResponsibleName,
                    farm.ContactEmail,
                    farm.ContactPhone,
                    farm.OfficialRegistrationNumber,
                    farm.Address.Street,
                    farm.Address.Number,
                    farm.Address.Complement,
                    farm.Address.Neighborhood,
                    farm.Address.City,
                    farm.Address.State,
                    farm.Address.PostalCode,
                    farm.VisualIdentityReference,
                    farm.VisualIdentitySource,
                    farm.VisualIdentityFileName,
                    farm.VisualIdentityContentType,
                    farm.VisualIdentityLength))
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
        var documentId = Guid.NewGuid();
        var renderFields = command.Type is BirdDocumentType.GenealogyCertificate or BirdDocumentType.ProvenanceDocument
            ? new[] { DocumentField.GenealogyTree }
            : selectedFields;
        var genealogy = await GetGenealogyAsync(command, renderFields, cancellationToken);
        if (genealogy.Status != GetBirdGenealogyStatus.Success)
        {
            session.SetStatus(ToGenerationStatus(genealogy.Status));
            return;
        }

        var documentGenealogyNodes = CreateDocumentGenealogyNodes(genealogy.Genealogy!);
        IReadOnlyDictionary<string, DocumentPhotoSnapshot?> genealogyPhotos;
        DocumentPhotoSnapshot? photo;
        BreedingFarmVisualIdentityDocumentSnapshot? visualIdentity;
        try
        {
            genealogyPhotos = command.Type == BirdDocumentType.Badge &&
                renderFields.Contains(DocumentField.GenealogyTree)
                ? await LoadGenealogyPhotosAsync(documentGenealogyNodes, breedingFarmId, cancellationToken)
                : new Dictionary<string, DocumentPhotoSnapshot?>(StringComparer.Ordinal);
            photo = await LoadBirdPhotoAsync(
                bird,
                breedingFarmId,
                command.Type is BirdDocumentType.GenealogyCertificate or BirdDocumentType.ProvenanceDocument
                    ? [DocumentField.BirdPhoto]
                    : selectedFields,
                cancellationToken);
            visualIdentity = await LoadVisualIdentityAsync(
                bird,
                breedingFarmId,
                documentId,
                command.VisualIdentityOverride,
                cancellationToken);
        }
        catch (InvalidDataException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
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
                documentGenealogyNodes
                    .Select(node => CreateGenealogySnapshotNode(node, genealogyPhotos))
                    .ToArray(),
                new BreedingFarmDocumentSnapshot(
                    bird.ResponsibleName,
                    bird.ContactEmail,
                    bird.ContactPhone,
                    bird.OfficialRegistrationNumber,
                    new BreedingFarmAddressDocumentSnapshot(
                        bird.AddressStreet,
                        bird.AddressNumber,
                        bird.AddressComplement,
                        bird.AddressNeighborhood,
                        bird.AddressCity,
                        bird.AddressState,
                        bird.AddressPostalCode),
                    visualIdentity),
                command.Type is BirdDocumentType.GenealogyCertificate or BirdDocumentType.ProvenanceDocument
                    ? generatedAtUtc
                    : null,
                command.Type == BirdDocumentType.GenealogyCertificate
                    ? CreateInternalDocumentIdentifier(documentId)
                    : null);
        }
        catch (ArgumentException)
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }

        var renderRequest = new DocumentRenderRequest(
            command.Type,
            snapshot,
            badge,
            certificate,
            command.PhotoFocus);
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

        var objectKey = $"birds/{bird.BirdId:N}/documents/{documentId:N}.pdf";
        var selectedFieldsJson = JsonSerializer.Serialize(selectedFields, JsonOptions);
        var snapshotJson = SerializeSnapshot(snapshot);
        if (selectedFieldsJson.Length > 1_000_000 || snapshotJson.Length > 1_000_000)
        {
            session.SetStatus(GenerateBirdDocumentStatus.InvalidData);
            return;
        }

        PrivateObjectDescriptor? storedIdentityObject = null;
        PrivateObjectDescriptor? storedObject = null;
        var prepared = false;
        try
        {
            if (snapshot.BreedingFarmDetails?.VisualIdentity is { } visualIdentitySnapshot)
            {
                await using var visualIdentityContent = new MemoryStream(
                    visualIdentitySnapshot.Content.ToArray(),
                    writable: false);
                storedIdentityObject = await storage.PutAsync(
                    new PrivateObjectUpload(
                        breedingFarmId,
                        visualIdentitySnapshot.Reference.ObjectKey,
                        visualIdentitySnapshot.Reference.FileName,
                        visualIdentitySnapshot.Reference.ContentType,
                        visualIdentityContent),
                    cancellationToken);
            }

            await using var renderedContent = new MemoryStream(rendered.Content, writable: false);
            storedObject = await storage.PutAsync(
                new PrivateObjectUpload(
                    breedingFarmId,
                    objectKey,
                    rendered.FileName,
                    rendered.ContentType,
                    renderedContent),
                cancellationToken);

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
                    generatedAtUtc,
                    certificate?.ModelId),
                storedObject,
                storedIdentityObject);
            prepared = true;
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
        finally
        {
            if (!prepared)
            {
                await DeleteStoredObjectAsync(storedIdentityObject, CancellationToken.None);
                await DeleteStoredObjectAsync(storedObject, CancellationToken.None);
            }
        }
    }

    private async Task<BreedingFarmVisualIdentityDocumentSnapshot?> LoadVisualIdentityAsync(
        BirdGenerationProjection bird,
        Guid breedingFarmId,
        Guid documentId,
        BreedingFarmVisualIdentitySnapshotOverride? snapshotOverride,
        CancellationToken cancellationToken)
    {
        VisualIdentitySourceReference? source;
        if (snapshotOverride is not null)
        {
            source = snapshotOverride.Reference is { } reference
                ? new VisualIdentitySourceReference(
                    reference.Source,
                    reference.ObjectKey,
                    reference.FileName,
                    reference.ContentType,
                    reference.Length)
                : null;
        }
        else if (bird.VisualIdentitySource is { } currentSource)
        {
            source = new VisualIdentitySourceReference(
                currentSource,
                bird.VisualIdentityReference ?? string.Empty,
                bird.VisualIdentityFileName,
                bird.VisualIdentityContentType,
                bird.VisualIdentityLength);
        }
        else
        {
            source = null;
        }

        if (source is null)
        {
            return null;
        }

        if (!Enum.IsDefined(source.Source) ||
            string.IsNullOrWhiteSpace(source.ObjectKey) ||
            source.Length is < 0 or > BreedingFarmVisualIdentityUploadLimits.MaxFileLength)
        {
            throw new InvalidDataException("The stored visual identity reference is invalid.");
        }

        await using var storedContent = await storage.OpenReadAsync(
            breedingFarmId,
            source.ObjectKey,
            cancellationToken);
        var content = await ReadVisualIdentityContentAsync(storedContent, cancellationToken);
        if (source.Length is { } expectedLength && expectedLength != content.LongLength)
        {
            throw new InvalidDataException("The stored visual identity length does not match its metadata.");
        }

        var (fileName, contentType) = ResolveVisualIdentityMetadata(source, content);
        if (!BreedingFarmVisualIdentityImageValidation.TryValidate(
                fileName,
                contentType,
                content,
                out _))
        {
            throw new InvalidDataException("The stored visual identity image is invalid.");
        }

        var extension = contentType == "image/png" ? ".png" : ".jpg";
        var documentObjectKey = $"birds/{bird.BirdId:N}/documents/{documentId:N}.identity{extension}";
        var documentReference = new BreedingFarmVisualIdentityDocumentReference(
            source.Source,
            documentObjectKey,
            fileName,
            contentType,
            content.LongLength);
        return new BreedingFarmVisualIdentityDocumentSnapshot(documentReference, content);
    }

    private static async Task<byte[]> ReadVisualIdentityContentAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (content.Length + bytesRead > BreedingFarmVisualIdentityUploadLimits.MaxFileLength)
            {
                throw new InvalidDataException("The stored visual identity exceeds the supported file size.");
            }

            await content.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return content.ToArray();
    }

    private static (string FileName, string ContentType) ResolveVisualIdentityMetadata(
        VisualIdentitySourceReference source,
        byte[] content)
    {
        if (!string.IsNullOrWhiteSpace(source.FileName) &&
            !string.IsNullOrWhiteSpace(source.ContentType))
        {
            return (source.FileName, source.ContentType.Trim().ToLowerInvariant());
        }

        if (source.FileName is not null || source.ContentType is not null)
        {
            throw new InvalidDataException("The stored visual identity metadata is incomplete.");
        }

        if (content.AsSpan().StartsWith(PngSignature))
        {
            return ("visual-identity.png", "image/png");
        }

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xD8)
        {
            return ("visual-identity.jpg", "image/jpeg");
        }

        throw new InvalidDataException("The stored template identity is not a supported image.");
    }

    private async Task DeleteStoredObjectAsync(
        PrivateObjectDescriptor? storedObject,
        CancellationToken cancellationToken)
    {
        if (storedObject is null)
        {
            return;
        }

        try
        {
            await storage.DeleteAsync(
                storedObject.BreedingFarmId,
                storedObject.ObjectKey,
                cancellationToken);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record VisualIdentitySourceReference(
        BreedingFarmVisualIdentitySource Source,
        string ObjectKey,
        string? FileName,
        string? ContentType,
        long? Length);

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
                command.Type == BirdDocumentType.GenealogyCertificate
                    ? 4
                    : BirdGenealogyLimits.MaxGenerations),
            cancellationToken);
    }

    private async Task<DocumentPhotoSnapshot?> LoadBirdPhotoAsync(
        BirdGenerationProjection bird,
        Guid breedingFarmId,
        IReadOnlyCollection<DocumentField> renderFields,
        CancellationToken cancellationToken)
    {
        if (!renderFields.Contains(DocumentField.BirdPhoto))
        {
            return null;
        }

        if (bird.PrimaryPhotoId is { } primaryPhotoId)
        {
            var attachment = await dbContext.BirdAttachments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.Id == primaryPhotoId &&
                        candidate.BirdId == bird.BirdId &&
                    candidate.BreedingFarmId == breedingFarmId &&
                    candidate.DeletedAtUtc == null,
                    cancellationToken);
            return await LoadBirdPhotoAsync(
                breedingFarmId,
                attachment is null
                    ? null
                    : new BirdPhotoReference(
                        attachment.ObjectKey,
                        attachment.FileName,
                        attachment.ContentType),
                bird.DefaultImageFileName,
                bird.DefaultImageContentType,
                bird.SpeciesId,
                bird.SpeciesDefaultImageFileName,
                bird.SpeciesDefaultImageContentType,
                toleratePrimaryPhotoReadFailure: false,
                cancellationToken: cancellationToken);
        }

        return await LoadBirdPhotoAsync(
            breedingFarmId,
            primaryPhoto: null,
            bird.DefaultImageFileName,
            bird.DefaultImageContentType,
            bird.SpeciesId,
            bird.SpeciesDefaultImageFileName,
            bird.SpeciesDefaultImageContentType,
            toleratePrimaryPhotoReadFailure: false,
            cancellationToken: cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, DocumentPhotoSnapshot?>> LoadGenealogyPhotosAsync(
        IReadOnlyCollection<BirdGenealogyNodeResult> genealogyNodes,
        Guid breedingFarmId,
        CancellationToken cancellationToken)
    {
        var targetNodes = genealogyNodes
            .Where(node =>
                BadgeGenealogyPhotoPositions.Contains(node.Position) &&
                node.BirdId is not null)
            .ToArray();
        var birdIds = targetNodes
            .Select(node => node.BirdId!.Value)
            .Distinct()
            .ToArray();
        if (birdIds.Length == 0)
        {
            return new Dictionary<string, DocumentPhotoSnapshot?>(StringComparer.Ordinal);
        }

        var birds = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId && birdIds.Contains(bird.Id))
            .Join(
                dbContext.Species.AsNoTracking(),
                bird => bird.SpeciesId,
                species => species.Id,
                (bird, species) => new GenealogyPhotoBirdProjection(
                    bird.Id,
                    bird.PrimaryPhotoId,
                    bird.DefaultImageFileName,
                    bird.DefaultImageContentType,
                    bird.SpeciesId,
                    species.DefaultImageFileName,
                    species.DefaultImageContentType))
            .ToArrayAsync(cancellationToken);

        var primaryPhotoIds = birds
            .Where(bird => bird.PrimaryPhotoId is not null)
            .Select(bird => bird.PrimaryPhotoId!.Value)
            .Distinct()
            .ToArray();
        var primaryPhotos = primaryPhotoIds.Length == 0
            ? []
            : await dbContext.BirdAttachments
                .AsNoTracking()
                .Where(attachment =>
                    attachment.BreedingFarmId == breedingFarmId &&
                    primaryPhotoIds.Contains(attachment.Id) &&
                    attachment.DeletedAtUtc == null)
                .Select(attachment => new BirdPhotoAttachmentProjection(
                    attachment.Id,
                    attachment.ObjectKey,
                    attachment.FileName,
                    attachment.ContentType))
                .ToArrayAsync(cancellationToken);
        var primaryPhotoById = primaryPhotos.ToDictionary(photo => photo.AttachmentId);
        var photoByBirdId = new Dictionary<Guid, DocumentPhotoSnapshot?>();

        foreach (var bird in birds)
        {
            var primaryPhoto = bird.PrimaryPhotoId is { } primaryPhotoId &&
                primaryPhotoById.TryGetValue(primaryPhotoId, out var attachment)
                    ? new BirdPhotoReference(
                        attachment.ObjectKey,
                        attachment.FileName,
                        attachment.ContentType)
                    : null;
            photoByBirdId[bird.BirdId] = await LoadBirdPhotoAsync(
                breedingFarmId,
                primaryPhoto,
                bird.DefaultImageFileName,
                bird.DefaultImageContentType,
                bird.SpeciesId,
                bird.SpeciesDefaultImageFileName,
                bird.SpeciesDefaultImageContentType,
                toleratePrimaryPhotoReadFailure: true,
                cancellationToken: cancellationToken);
        }

        return targetNodes.ToDictionary(
            node => node.Position,
            node => photoByBirdId.GetValueOrDefault(node.BirdId!.Value),
            StringComparer.Ordinal);
    }

    private static IReadOnlyCollection<BirdGenealogyNodeResult> CreateDocumentGenealogyNodes(
        BirdGenealogyResult genealogy)
    {
        if (genealogy.Nodes.Count == 0)
        {
            return [];
        }

        var nodesByKey = genealogy.Nodes.ToDictionary(node => node.NodeKey, StringComparer.Ordinal);
        var edgesByChild = genealogy.Edges
            .GroupBy(edge => edge.ChildNodeKey)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var documentNodes = new List<BirdGenealogyNodeResult>();
        var pending = new Queue<(string NodeKey, string Position, int Generation)>();
        pending.Enqueue((
            nodesByKey.Values.Single(node => node.Position == GenealogyNode.RootPosition).NodeKey,
            string.Empty,
            0));

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current.Generation >= genealogy.MaxGenerations ||
                !edgesByChild.TryGetValue(current.NodeKey, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                if (!nodesByKey.TryGetValue(edge.ParentNodeKey, out var node))
                {
                    continue;
                }

                var position = string.IsNullOrEmpty(current.Position)
                    ? edge.Position
                    : $"{current.Position}.{edge.Position}";
                documentNodes.Add(node with { Position = position });
                pending.Enqueue((edge.ParentNodeKey, position, current.Generation + 1));
            }
        }

        return documentNodes;
    }

    private async Task<DocumentPhotoSnapshot?> LoadBirdPhotoAsync(
        Guid breedingFarmId,
        BirdPhotoReference? primaryPhoto,
        string? birdDefaultImageFileName,
        string? birdDefaultImageContentType,
        Guid speciesId,
        string? speciesDefaultImageFileName,
        string? speciesDefaultImageContentType,
        bool toleratePrimaryPhotoReadFailure,
        CancellationToken cancellationToken)
    {
        if (primaryPhoto is not null)
        {
            try
            {
                await using var content = await storage.OpenReadAsync(
                    breedingFarmId,
                    primaryPhoto.ObjectKey,
                    cancellationToken);
                await using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                return new DocumentPhotoSnapshot(
                    primaryPhoto.FileName,
                    primaryPhoto.ContentType,
                    buffer.ToArray());
            }
            catch (FileNotFoundException) when (toleratePrimaryPhotoReadFailure)
            {
            }
            catch (DirectoryNotFoundException) when (toleratePrimaryPhotoReadFailure)
            {
            }
        }

        var defaultImage = await speciesDefaultImageReader.ReadAsync(
            birdDefaultImageFileName ?? speciesDefaultImageFileName,
            birdDefaultImageContentType ?? speciesDefaultImageContentType,
            cancellationToken);
        if (defaultImage is not null ||
            !SpeciesDefaultImageCatalog.TryGetMetadata(
                speciesId,
                out var speciesFileName,
                out var speciesContentType))
        {
            return defaultImage;
        }

        return await speciesDefaultImageReader.ReadAsync(
            speciesFileName,
            speciesContentType,
            cancellationToken);
    }

    private static GenealogySnapshotNode CreateGenealogySnapshotNode(
        BirdGenealogyNodeResult node,
        IReadOnlyDictionary<string, DocumentPhotoSnapshot?> photos) =>
        new(
            node.Position,
            node.Name,
            node.RingNumber,
            node.Sex,
            node.BirthDate,
            photos.TryGetValue(node.Position, out var photo) ? photo : null);

    private static bool IsValidCommand(
        GenerateBirdDocumentCommand command,
        out BadgeRenderConfiguration? badge,
        out GenealogyCertificateRenderConfiguration? certificate,
        out IReadOnlyCollection<DocumentField> selectedFields)
    {
        badge = null;
        certificate = null;
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
                command.CertificateModelId is not null ||
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

        if (command.Type == BirdDocumentType.GenealogyCertificate)
        {
            if (command.ModelId is not null ||
                command.PrintSize is not null ||
                command.SelectedFields is { Count: > 0 })
            {
                return false;
            }

            try
            {
                certificate = new GenealogyCertificateRenderConfiguration(
                    command.CertificateModelId ?? GenealogyCertificateRenderConfiguration.DefaultModelId);
            }
            catch (ArgumentException)
            {
                return false;
            }

            selectedFields = [];
            return true;
        }

        if (command.ModelId is not null ||
            command.CertificateModelId is not null ||
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
                snapshot.Genealogy
                    .Select(node => new PersistedGenealogySnapshotNode(
                        node.Position,
                        node.Name,
                        node.RingNumber,
                        node.Sex,
                        node.BirthDate))
                    .ToArray(),
                snapshot.BreedingFarmDetails is null
                    ? null
                    : new PersistedBreedingFarmDocumentSnapshot(
                        snapshot.BreedingFarmDetails.ResponsibleName,
                        snapshot.BreedingFarmDetails.ContactEmail,
                        snapshot.BreedingFarmDetails.ContactPhone,
                        snapshot.BreedingFarmDetails.OfficialRegistrationNumber,
                        snapshot.BreedingFarmDetails.Address,
                        snapshot.BreedingFarmDetails.VisualIdentity?.Reference),
                snapshot.IssuedAtUtc,
                snapshot.InternalDocumentIdentifier),
            JsonOptions);

    private static string CreateInternalDocumentIdentifier(Guid documentId) =>
        $"CV-GEN-{documentId:N}".ToUpperInvariant();

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
        string? DefaultImageFileName,
        string? DefaultImageContentType,
        Guid SpeciesId,
        string? SpeciesDefaultImageFileName,
        string? SpeciesDefaultImageContentType,
        string SpeciesName,
        string BreedingFarmName,
        string ResponsibleName,
        string ContactEmail,
        string? ContactPhone,
        string? OfficialRegistrationNumber,
        string? AddressStreet,
        string? AddressNumber,
        string? AddressComplement,
        string? AddressNeighborhood,
        string? AddressCity,
        string? AddressState,
        string? AddressPostalCode,
        string? VisualIdentityReference,
        BreedingFarmVisualIdentitySource? VisualIdentitySource,
        string? VisualIdentityFileName,
        string? VisualIdentityContentType,
        long? VisualIdentityLength);

    private sealed record GenealogyPhotoBirdProjection(
        Guid BirdId,
        Guid? PrimaryPhotoId,
        string? DefaultImageFileName,
        string? DefaultImageContentType,
        Guid SpeciesId,
        string? SpeciesDefaultImageFileName,
        string? SpeciesDefaultImageContentType);

    private sealed record BirdPhotoAttachmentProjection(
        Guid AttachmentId,
        string ObjectKey,
        string FileName,
        string ContentType);

    private sealed record BirdPhotoReference(
        string ObjectKey,
        string FileName,
        string ContentType);

    private sealed record PersistedSnapshot(
        Guid BirdId,
        string Name,
        string? RingNumber,
        BirdSex Sex,
        string Species,
        DateOnly? BirthDate,
        string BreedingFarmName,
        PersistedPhoto? Photo,
        IReadOnlyCollection<PersistedGenealogySnapshotNode> Genealogy,
        PersistedBreedingFarmDocumentSnapshot? BreedingFarmDetails,
        DateTimeOffset? IssuedAtUtc,
        string? InternalDocumentIdentifier);

    private sealed record PersistedBreedingFarmDocumentSnapshot(
        string ResponsibleName,
        string ContactEmail,
        string? ContactPhone,
        string? OfficialRegistrationNumber,
        BreedingFarmAddressDocumentSnapshot? Address,
        BreedingFarmVisualIdentityDocumentReference? VisualIdentity);

    private sealed record PersistedPhoto(string FileName, string ContentType);

    private sealed record PersistedGenealogySnapshotNode(
        string Position,
        string? Name,
        string? RingNumber,
        BirdSex? Sex,
        DateOnly? BirthDate);
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
                preparation.CertificateModelId!.Value,
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
                    document.GeneratedAtUtc,
                    document.CertificateModelId)));
    }
}
