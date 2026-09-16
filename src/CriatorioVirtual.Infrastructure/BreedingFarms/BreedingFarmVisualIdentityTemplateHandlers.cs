using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class GetBreedingFarmVisualIdentityTemplatesQueryHandler(IVisualIdentityTemplateCatalog catalog)
    : IQueryHandler<GetBreedingFarmVisualIdentityTemplatesQuery, IReadOnlyList<BreedingFarmVisualIdentityTemplateCatalogItem>>
{
    public Task<IReadOnlyList<BreedingFarmVisualIdentityTemplateCatalogItem>> Handle(
        GetBreedingFarmVisualIdentityTemplatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var items = catalog.GetAll()
            .Where(candidate => candidate.IsActive)
            .Select(candidate => new BreedingFarmVisualIdentityTemplateCatalogItem(
                candidate.Id,
                candidate.Name,
                candidate.Version,
                candidate.PreviewUrl,
                "1:1",
                candidate.Options))
            .ToArray();
        return Task.FromResult<IReadOnlyList<BreedingFarmVisualIdentityTemplateCatalogItem>>(items);
    }
}

public sealed class GetBreedingFarmVisualIdentityTemplatePreviewQueryHandler(
    IVisualIdentityTemplateCatalog catalog,
    IVisualIdentityTemplateImageRenderer renderer)
    : IQueryHandler<GetBreedingFarmVisualIdentityTemplatePreviewQuery, GetBreedingFarmVisualIdentityTemplatePreviewResult>
{
    public async Task<GetBreedingFarmVisualIdentityTemplatePreviewResult> Handle(
        GetBreedingFarmVisualIdentityTemplatePreviewQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var (status, template) = ResolveTemplate(catalog, query.TemplateId, query.Version);
        var previewStatus = status switch
        {
            PreviewBreedingFarmVisualIdentityTemplateStatus.TemplateInactive =>
                GetBreedingFarmVisualIdentityTemplatePreviewStatus.Inactive,
            PreviewBreedingFarmVisualIdentityTemplateStatus.VersionUnavailable =>
                GetBreedingFarmVisualIdentityTemplatePreviewStatus.VersionUnavailable,
            _ when template is null => GetBreedingFarmVisualIdentityTemplatePreviewStatus.NotFound,
            _ => GetBreedingFarmVisualIdentityTemplatePreviewStatus.Available
        };
        if (previewStatus != GetBreedingFarmVisualIdentityTemplatePreviewStatus.Available)
        {
            return new(previewStatus, null);
        }

        try
        {
            var availableTemplate = template!;
            var preview = await renderer.RenderPngAsync(
                availableTemplate,
                availableTemplate.DefaultConfiguration,
                cancellationToken);
            return new(GetBreedingFarmVisualIdentityTemplatePreviewStatus.Available, preview);
        }
        catch (InvalidOperationException)
        {
            return new(GetBreedingFarmVisualIdentityTemplatePreviewStatus.RenderingUnavailable, null);
        }
        catch (TimeoutException)
        {
            return new(GetBreedingFarmVisualIdentityTemplatePreviewStatus.RenderingUnavailable, null);
        }
    }

    internal static (PreviewBreedingFarmVisualIdentityTemplateStatus Status, VisualIdentityTemplateDefinition? Template)
        ResolveTemplate(IVisualIdentityTemplateCatalog catalog, string templateId, string version)
    {
        var template = catalog.GetAll().SingleOrDefault(candidate =>
            string.Equals(candidate.Id, templateId, StringComparison.Ordinal));
        if (template is null)
        {
            return (PreviewBreedingFarmVisualIdentityTemplateStatus.TemplateNotFound, null);
        }

        if (!template.IsActive)
        {
            return (PreviewBreedingFarmVisualIdentityTemplateStatus.TemplateInactive, template);
        }

        if (!string.Equals(template.Version, version, StringComparison.Ordinal))
        {
            return (PreviewBreedingFarmVisualIdentityTemplateStatus.VersionUnavailable, template);
        }

        return (PreviewBreedingFarmVisualIdentityTemplateStatus.PreviewReady, template);
    }
}

public sealed class PreviewBreedingFarmVisualIdentityTemplateQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IVisualIdentityTemplateCatalog catalog,
    IVisualIdentityTemplateImageRenderer renderer)
    : IQueryHandler<PreviewBreedingFarmVisualIdentityTemplateQuery, PreviewBreedingFarmVisualIdentityTemplateResult>
{
    public async Task<PreviewBreedingFarmVisualIdentityTemplateResult> Handle(
        PreviewBreedingFarmVisualIdentityTemplateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            query.UserId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToPreviewStatus(access.Status), null);
        }

        var (templateStatus, template) = GetBreedingFarmVisualIdentityTemplatePreviewQueryHandler.ResolveTemplate(
            catalog,
            query.TemplateId,
            query.Version);
        if (template is null || templateStatus != PreviewBreedingFarmVisualIdentityTemplateStatus.PreviewReady)
        {
            return new(templateStatus, null);
        }

        if (!BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveConfiguration(
                template,
                query.Configuration,
                access.Farm!.Name,
                out var configuration,
                out _))
        {
            return new(PreviewBreedingFarmVisualIdentityTemplateStatus.InvalidConfiguration, null);
        }

        try
        {
            var content = await renderer.RenderPngAsync(template, configuration, cancellationToken);
            return new(PreviewBreedingFarmVisualIdentityTemplateStatus.PreviewReady, content);
        }
        catch (InvalidOperationException)
        {
            return new(PreviewBreedingFarmVisualIdentityTemplateStatus.RenderingUnavailable, null);
        }
        catch (TimeoutException)
        {
            return new(PreviewBreedingFarmVisualIdentityTemplateStatus.RenderingUnavailable, null);
        }
    }

    private static PreviewBreedingFarmVisualIdentityTemplateStatus ToPreviewStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound =>
                PreviewBreedingFarmVisualIdentityTemplateStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected =>
                PreviewBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound =>
                PreviewBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotFound,
            _ => PreviewBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotFound
        };
}

public sealed class ApplyBreedingFarmVisualIdentityTemplateSession(IPrivateObjectStorage storage)
    : ICommandFailureCompensator
{
    public ApplyBreedingFarmVisualIdentityTemplateStatus Status { get; private set; } =
        ApplyBreedingFarmVisualIdentityTemplateStatus.TemplateNotFound;

    public VisualIdentityTemplateDefinition? Template { get; private set; }

    public string? EffectiveConfiguration { get; private set; }

    public PrivateObjectDescriptor? StoredObject { get; private set; }

    public void SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus status) => Status = status;

    public void SetRenderedObject(
        VisualIdentityTemplateDefinition template,
        string effectiveConfiguration,
        PrivateObjectDescriptor storedObject)
    {
        Template = template;
        EffectiveConfiguration = effectiveConfiguration;
        StoredObject = storedObject;
        Status = ApplyBreedingFarmVisualIdentityTemplateStatus.Applied;
    }

    public Task CompensateAsync(CancellationToken cancellationToken) => DeleteNewAssetAsync(cancellationToken);

    public async Task DeleteNewAssetAsync(CancellationToken cancellationToken)
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

public sealed class ApplyBreedingFarmVisualIdentityTemplatePreProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    IVisualIdentityTemplateCatalog catalog,
    IVisualIdentityTemplateImageRenderer renderer,
    ApplyBreedingFarmVisualIdentityTemplateSession session)
    : ICommandPreProcessor<ApplyBreedingFarmVisualIdentityTemplateCommand>
{
    public async Task Process(
        ApplyBreedingFarmVisualIdentityTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            tracking: false,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            session.SetStatus(ToApplyStatus(access.Status));
            return;
        }

        var (templateStatus, template) = GetBreedingFarmVisualIdentityTemplatePreviewQueryHandler.ResolveTemplate(
            catalog,
            command.TemplateId,
            command.Version);
        if (templateStatus != PreviewBreedingFarmVisualIdentityTemplateStatus.PreviewReady || template is null)
        {
            session.SetStatus(templateStatus switch
            {
                PreviewBreedingFarmVisualIdentityTemplateStatus.TemplateInactive =>
                    ApplyBreedingFarmVisualIdentityTemplateStatus.TemplateInactive,
                PreviewBreedingFarmVisualIdentityTemplateStatus.VersionUnavailable =>
                    ApplyBreedingFarmVisualIdentityTemplateStatus.VersionUnavailable,
                _ => ApplyBreedingFarmVisualIdentityTemplateStatus.TemplateNotFound
            });
            return;
        }

        if (!BreedingFarmVisualIdentityTemplateConfiguration.TryGetEffectiveConfiguration(
                template,
                command.Configuration,
                access.Farm!.Name,
                out var configuration,
                out var effectiveConfiguration))
        {
            session.SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus.InvalidConfiguration);
            return;
        }

        byte[] png;
        try
        {
            png = await renderer.RenderPngAsync(template, configuration, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            session.SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus.RenderingUnavailable);
            return;
        }
        catch (TimeoutException)
        {
            session.SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus.RenderingUnavailable);
            return;
        }

        if (png.LongLength <= 0 || png.LongLength > BreedingFarmVisualIdentityUploadLimits.MaxFileLength)
        {
            session.SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus.StorageUnavailable);
            return;
        }

        var objectKey = $"visual-identity/{Guid.NewGuid():N}.png";
        try
        {
            await using var content = new MemoryStream(png, writable: false);
            var storedObject = await storage.PutAsync(
                new PrivateObjectUpload(
                    access.Farm!.Id,
                    objectKey,
                    "identity-template.png",
                    "image/png",
                    content),
                cancellationToken);
            session.SetRenderedObject(template, effectiveConfiguration, storedObject);
            if (storedObject.BreedingFarmId != access.Farm.Id ||
                !string.Equals(storedObject.ObjectKey, objectKey, StringComparison.Ordinal) ||
                storedObject.Length != png.Length ||
                !string.Equals(storedObject.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            {
                session.SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus.StorageUnavailable);
            }
        }
        catch (ArgumentException)
        {
            session.SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus.StorageUnavailable);
        }
        catch (IOException)
        {
            session.SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus.StorageUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            session.SetStatus(ApplyBreedingFarmVisualIdentityTemplateStatus.StorageUnavailable);
        }
    }

    private static ApplyBreedingFarmVisualIdentityTemplateStatus ToApplyStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound =>
                ApplyBreedingFarmVisualIdentityTemplateStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected =>
                ApplyBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound =>
                ApplyBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotFound,
            _ => ApplyBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotFound
        };
}

public sealed class ApplyBreedingFarmVisualIdentityTemplateCommandHandler(
    CriatorioVirtualDbContext dbContext,
    ApplyBreedingFarmVisualIdentityTemplateSession session)
    : ICommandHandler<ApplyBreedingFarmVisualIdentityTemplateCommand, ApplyBreedingFarmVisualIdentityTemplateResult>
{
    public async Task<ApplyBreedingFarmVisualIdentityTemplateResult> Handle(
        ApplyBreedingFarmVisualIdentityTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (session.Status != ApplyBreedingFarmVisualIdentityTemplateStatus.Applied ||
            session.Template is null ||
            session.StoredObject is null ||
            session.EffectiveConfiguration is null)
        {
            return new(session.Status, null, null, null);
        }

        var access = await BreedingFarmVisualIdentityAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            tracking: true,
            cancellationToken);
        if (access.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return new(ToApplyStatus(access.Status), null, null, null);
        }

        var farm = access.Farm!;
        if (session.StoredObject.BreedingFarmId != farm.Id)
        {
            return new(ApplyBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotFound, null, null, null);
        }

        var previous = farm.GetVisualIdentity();
        var previousAsset = previous is not null && previous.ContentType is not null
            ? new BreedingFarmVisualIdentityCleanup(farm.Id, previous.Reference)
            : null;
        farm.SetVisualIdentity(
            BreedingFarmVisualIdentitySource.Template,
            session.StoredObject.ObjectKey,
            "identity-template.png",
            "image/png",
            session.StoredObject.Length,
            DateTimeOffset.UtcNow,
            session.Template.Id,
            session.Template.Version,
            session.EffectiveConfiguration);

        return new(
            ApplyBreedingFarmVisualIdentityTemplateStatus.Applied,
            farm.Id,
            BreedingFarmVisualIdentityMapping.ToMetadata(farm),
            previousAsset);
    }

    private static ApplyBreedingFarmVisualIdentityTemplateStatus ToApplyStatus(
        BreedingFarmVisualIdentityAccessStatus status) => status switch
        {
            BreedingFarmVisualIdentityAccessStatus.UserNotFound =>
                ApplyBreedingFarmVisualIdentityTemplateStatus.UserNotFound,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected =>
                ApplyBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotSelected,
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound =>
                ApplyBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotFound,
            _ => ApplyBreedingFarmVisualIdentityTemplateStatus.BreedingFarmNotFound
        };
}

public sealed class ApplyBreedingFarmVisualIdentityTemplatePostProcessor(
    IPrivateObjectStorage storage,
    ApplyBreedingFarmVisualIdentityTemplateSession session)
    : ICommandPostProcessor<ApplyBreedingFarmVisualIdentityTemplateCommand, ApplyBreedingFarmVisualIdentityTemplateResult>
{
    public async Task<ApplyBreedingFarmVisualIdentityTemplateResult> Process(
        ApplyBreedingFarmVisualIdentityTemplateCommand command,
        ApplyBreedingFarmVisualIdentityTemplateResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == ApplyBreedingFarmVisualIdentityTemplateStatus.Applied)
        {
            if (result.PreviousAsset is not null)
            {
                await BreedingFarmVisualIdentityUploadSession.TryDeleteAsync(
                    storage,
                    result.PreviousAsset.BreedingFarmId,
                    result.PreviousAsset.ObjectKey,
                    cancellationToken);
            }
        }
        else
        {
            await session.DeleteNewAssetAsync(cancellationToken);
        }

        return result;
    }
}
