using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.CoverTemplates;

internal static class BreedingFarmCoverAccess
{
    public static async Task<BreedingFarmCoverFarmLookup> FindCurrentOwnerFarmAsync(
        CriatorioVirtualDbContext dbContext,
        Guid userId,
        Guid breedingFarmId,
        bool tracking,
        CancellationToken cancellationToken,
        bool requireOwner = true)
    {
        if (userId == Guid.Empty)
        {
            return new(BreedingFarmCoverAccessStatus.UserNotFound, null);
        }

        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return new(BreedingFarmCoverAccessStatus.UserNotFound, null);
        }

        if (user.SelectedBreedingFarmId != breedingFarmId)
        {
            return new(BreedingFarmCoverAccessStatus.BreedingFarmNotFound, null);
        }

        IQueryable<BreedingFarm> farms = tracking
            ? dbContext.BreedingFarms
            : dbContext.BreedingFarms.AsNoTracking();
        var farm = await farms.SingleOrDefaultAsync(
            candidate => candidate.Id == breedingFarmId && dbContext.BreedingFarmUsers.Any(membership =>
                membership.BreedingFarmId == candidate.Id &&
                membership.UserId == userId &&
                membership.IsActive &&
                (!requireOwner || membership.Role == BreedingFarmRole.Owner)),
            cancellationToken);

        return farm is null
            ? new(BreedingFarmCoverAccessStatus.BreedingFarmNotFound, null)
            : new(BreedingFarmCoverAccessStatus.Success, farm);
    }
}

internal sealed record BreedingFarmCoverFarmLookup(BreedingFarmCoverAccessStatus Status, BreedingFarm? Farm);

internal static class BreedingFarmCoverMapping
{
    public static BreedingFarmCoverMetadata? ToMetadata(BreedingFarm farm)
    {
        var cover = farm.GetCover();
        return cover is null
            ? null
            : new(
                cover.Source,
                cover.FileName,
                cover.ContentType,
                cover.Length,
                cover.UpdatedAtUtc,
                cover.TemplateModelId,
                cover.TemplateVersion,
                cover.TemplateConfiguration);
    }
}

public sealed class GetBreedingFarmCoverQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBreedingFarmCoverQuery, GetBreedingFarmCoverResult>
{
    public async Task<GetBreedingFarmCoverResult> Handle(
        GetBreedingFarmCoverQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await BreedingFarmCoverAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            query.UserId,
            query.BreedingFarmId,
            tracking: false,
            cancellationToken,
            requireOwner: false);
        return access.Status == BreedingFarmCoverAccessStatus.Success
            ? new(access.Status, access.Farm!.Id, BreedingFarmCoverMapping.ToMetadata(access.Farm))
            : new(access.Status, null, null);
    }
}

public sealed class GetBreedingFarmCoverContentQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage)
    : IQueryHandler<GetBreedingFarmCoverContentQuery, GetBreedingFarmCoverContentResult>
{
    public async Task<GetBreedingFarmCoverContentResult> Handle(
        GetBreedingFarmCoverContentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await BreedingFarmCoverAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            query.UserId,
            query.BreedingFarmId,
            tracking: false,
            cancellationToken,
            requireOwner: false);
        if (access.Status != BreedingFarmCoverAccessStatus.Success)
        {
            return new(ToContentStatus(access.Status), null, null, null, null);
        }

        var farm = access.Farm!;
        var cover = farm.GetCover();
        if (cover is null)
        {
            return new(GetBreedingFarmCoverContentStatus.CoverNotFound, null, null, null, null);
        }

        try
        {
            var content = await storage.OpenReadAsync(farm.Id, cover.Reference, cancellationToken);
            return new(GetBreedingFarmCoverContentStatus.Success, cover.FileName, cover.ContentType, cover.Length, content);
        }
        catch (FileNotFoundException)
        {
            return StorageUnavailable();
        }
        catch (DirectoryNotFoundException)
        {
            return StorageUnavailable();
        }
        catch (IOException)
        {
            return StorageUnavailable();
        }
        catch (UnauthorizedAccessException)
        {
            return StorageUnavailable();
        }
    }

    private static GetBreedingFarmCoverContentStatus ToContentStatus(BreedingFarmCoverAccessStatus status) =>
        status == BreedingFarmCoverAccessStatus.UserNotFound
            ? GetBreedingFarmCoverContentStatus.UserNotFound
            : GetBreedingFarmCoverContentStatus.BreedingFarmNotFound;

    private static GetBreedingFarmCoverContentResult StorageUnavailable() =>
        new(GetBreedingFarmCoverContentStatus.StorageUnavailable, null, null, null, null);
}

public sealed class BreedingFarmCoverUploadSession(IPrivateObjectStorage storage) : ICommandFailureCompensator
{
    public UploadBreedingFarmCoverStatus Status { get; private set; } = UploadBreedingFarmCoverStatus.InvalidData;

    public PrivateObjectDescriptor? StoredObject { get; private set; }

    public void SetStatus(UploadBreedingFarmCoverStatus status) => Status = status;

    public void SetStoredObject(PrivateObjectDescriptor descriptor)
    {
        StoredObject = descriptor;
        Status = UploadBreedingFarmCoverStatus.Uploaded;
    }

    public async Task CompensateAsync(CancellationToken cancellationToken)
    {
        if (StoredObject is not { } storedObject)
        {
            return;
        }

        await BreedingFarmVisualIdentityUploadSession.TryDeleteAsync(
            storage,
            storedObject.BreedingFarmId,
            storedObject.ObjectKey,
            cancellationToken);
    }
}

public sealed class UploadBreedingFarmCoverPreProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    IBreedingFarmCoverRenderer renderer,
    BreedingFarmCoverUploadSession session)
    : ICommandPreProcessor<UploadBreedingFarmCoverCommand>
{
    public async Task Process(UploadBreedingFarmCoverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await BreedingFarmCoverAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            command.BreedingFarmId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmCoverAccessStatus.Success)
        {
            session.SetStatus(ToUploadStatus(access.Status));
            return;
        }

        if (command.Length is <= 0 or > BreedingFarmCoverUploadLimits.MaxFileLength || !command.Content.CanRead)
        {
            session.SetStatus(UploadBreedingFarmCoverStatus.InvalidData);
            return;
        }

        await using var input = new MemoryStream((int)command.Length);
        if (!await TryCopyContentAsync(command.Content, input, command.Length, cancellationToken) ||
            !input.TryGetBuffer(out var buffer) ||
            !BreedingFarmCoverImageValidation.TryValidate(
                command.FileName,
                command.ContentType,
                buffer.AsSpan(0, (int)input.Length),
                out var contentType,
                out _))
        {
            session.SetStatus(UploadBreedingFarmCoverStatus.InvalidData);
            return;
        }

        byte[] canonicalPng;
        try
        {
            canonicalPng = await renderer.CropUploadToPngAsync(
                buffer.AsMemory(0, (int)input.Length),
                contentType,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            session.SetStatus(UploadBreedingFarmCoverStatus.RenderingUnavailable);
            return;
        }
        catch (TimeoutException)
        {
            session.SetStatus(UploadBreedingFarmCoverStatus.RenderingUnavailable);
            return;
        }

        if (canonicalPng.Length is <= 0 or > BreedingFarmCoverUploadLimits.MaxCanonicalFileLength ||
            !BreedingFarmCoverImageValidation.TryValidate(
                "cover.png",
                "image/png",
                canonicalPng,
                out _,
                out _))
        {
            session.SetStatus(UploadBreedingFarmCoverStatus.InvalidData);
            return;
        }

        var farm = access.Farm!;
        var objectKey = $"cover/{Guid.NewGuid():N}.png";
        try
        {
            await using var content = new MemoryStream(canonicalPng, writable: false);
            var storedObject = await storage.PutAsync(
                new PrivateObjectUpload(farm.Id, objectKey, "cover.png", "image/png", content),
                cancellationToken);
            session.SetStoredObject(storedObject);
            if (storedObject.BreedingFarmId != farm.Id ||
                !string.Equals(storedObject.ObjectKey, objectKey, StringComparison.Ordinal) ||
                storedObject.Length != canonicalPng.Length ||
                !string.Equals(storedObject.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            {
                session.SetStatus(UploadBreedingFarmCoverStatus.StorageUnavailable);
            }
        }
        catch (ArgumentException)
        {
            session.SetStatus(UploadBreedingFarmCoverStatus.InvalidData);
        }
        catch (IOException)
        {
            session.SetStatus(UploadBreedingFarmCoverStatus.StorageUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            session.SetStatus(UploadBreedingFarmCoverStatus.StorageUnavailable);
        }
    }

    private static async Task<bool> TryCopyContentAsync(
        Stream source,
        MemoryStream destination,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return destination.Length > 0 && destination.Length == expectedLength;
            }

            if (destination.Length + read > BreedingFarmCoverUploadLimits.MaxFileLength)
            {
                return false;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static UploadBreedingFarmCoverStatus ToUploadStatus(BreedingFarmCoverAccessStatus status) =>
        status == BreedingFarmCoverAccessStatus.UserNotFound
            ? UploadBreedingFarmCoverStatus.UserNotFound
            : UploadBreedingFarmCoverStatus.BreedingFarmNotFound;
}

public sealed class UploadBreedingFarmCoverCommandHandler(
    CriatorioVirtualDbContext dbContext,
    BreedingFarmCoverUploadSession session)
    : ICommandHandler<UploadBreedingFarmCoverCommand, UploadBreedingFarmCoverResult>
{
    public async Task<UploadBreedingFarmCoverResult> Handle(
        UploadBreedingFarmCoverCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (session.Status != UploadBreedingFarmCoverStatus.Uploaded || session.StoredObject is not { } storedObject)
        {
            return new(session.Status, null, null, null);
        }

        var access = await BreedingFarmCoverAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            command.BreedingFarmId,
            tracking: true,
            cancellationToken);
        if (access.Status != BreedingFarmCoverAccessStatus.Success || storedObject.BreedingFarmId != command.BreedingFarmId)
        {
            return new(access.Status == BreedingFarmCoverAccessStatus.UserNotFound
                ? UploadBreedingFarmCoverStatus.UserNotFound
                : UploadBreedingFarmCoverStatus.BreedingFarmNotFound, null, null, null);
        }

        var farm = access.Farm!;
        var previous = farm.GetCover();
        var cleanup = previous is null ? null : new BreedingFarmCoverCleanup(farm.Id, previous.Reference);
        farm.SetCover(
            BreedingFarmCoverSource.Upload,
            storedObject.ObjectKey,
            "cover.png",
            "image/png",
            storedObject.Length,
            DateTimeOffset.UtcNow);
        return new(UploadBreedingFarmCoverStatus.Uploaded, farm.Id, BreedingFarmCoverMapping.ToMetadata(farm), cleanup);
    }
}

public sealed class RemoveBreedingFarmCoverCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<RemoveBreedingFarmCoverCommand, RemoveBreedingFarmCoverResult>
{
    public async Task<RemoveBreedingFarmCoverResult> Handle(
        RemoveBreedingFarmCoverCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await BreedingFarmCoverAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            command.BreedingFarmId,
            tracking: true,
            cancellationToken);
        if (access.Status != BreedingFarmCoverAccessStatus.Success)
        {
            return new(access.Status == BreedingFarmCoverAccessStatus.UserNotFound
                ? RemoveBreedingFarmCoverStatus.UserNotFound
                : RemoveBreedingFarmCoverStatus.BreedingFarmNotFound, null, null);
        }

        var farm = access.Farm!;
        var previous = farm.RemoveCover(DateTimeOffset.UtcNow);
        var cleanup = previous is null ? null : new BreedingFarmCoverCleanup(farm.Id, previous.Reference);
        return new(RemoveBreedingFarmCoverStatus.Removed, farm.Id, cleanup);
    }
}

public sealed class BreedingFarmCoverStoragePostProcessor(
    IPrivateObjectStorage storage,
    BreedingFarmCoverUploadSession uploadSession)
    : ICommandPostProcessor<UploadBreedingFarmCoverCommand, UploadBreedingFarmCoverResult>,
        ICommandPostProcessor<RemoveBreedingFarmCoverCommand, RemoveBreedingFarmCoverResult>
{
    public async Task<UploadBreedingFarmCoverResult> Process(
        UploadBreedingFarmCoverCommand command,
        UploadBreedingFarmCoverResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == UploadBreedingFarmCoverStatus.Uploaded && result.PreviousAsset is not null)
        {
            await BreedingFarmVisualIdentityUploadSession.TryDeleteAsync(
                storage,
                result.PreviousAsset.BreedingFarmId,
                result.PreviousAsset.ObjectKey,
                cancellationToken);
        }
        else if (result.Status != UploadBreedingFarmCoverStatus.Uploaded && uploadSession.StoredObject is { } storedObject)
        {
            await BreedingFarmVisualIdentityUploadSession.TryDeleteAsync(
                storage,
                storedObject.BreedingFarmId,
                storedObject.ObjectKey,
                cancellationToken);
        }

        return result;
    }

    public async Task<RemoveBreedingFarmCoverResult> Process(
        RemoveBreedingFarmCoverCommand command,
        RemoveBreedingFarmCoverResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == RemoveBreedingFarmCoverStatus.Removed && result.PreviousAsset is not null)
        {
            await BreedingFarmVisualIdentityUploadSession.TryDeleteAsync(
                storage,
                result.PreviousAsset.BreedingFarmId,
                result.PreviousAsset.ObjectKey,
                cancellationToken);
        }

        return result;
    }
}
