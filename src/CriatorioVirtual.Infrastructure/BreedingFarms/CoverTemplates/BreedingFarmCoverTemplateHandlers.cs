using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.CoverTemplates;

public sealed class GetBreedingFarmCoverTemplatesQueryHandler(IBreedingFarmCoverTemplateCatalog catalog)
    : IQueryHandler<GetBreedingFarmCoverTemplatesQuery, IReadOnlyList<BreedingFarmCoverTemplateCatalogItem>>
{
    public Task<IReadOnlyList<BreedingFarmCoverTemplateCatalogItem>> Handle(
        GetBreedingFarmCoverTemplatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<BreedingFarmCoverTemplateCatalogItem> items = catalog.GetAll()
            .Where(template => template.IsActive)
            .Select(template => new BreedingFarmCoverTemplateCatalogItem(
                template.Id,
                template.Name,
                template.Version,
                template.PreviewUrl,
                new BreedingFarmCoverCanvas(1920, 640, "3:1"),
                new BreedingFarmCoverSafeArea(0.2, 0.14, 0.6, 0.72, "60% central; elements must remain inside this area."),
                template.SupportedOptions,
                template.Defaults))
            .ToArray();
        return Task.FromResult(items);
    }
}

public sealed class GetBreedingFarmCoverTemplatePreviewQueryHandler(IBreedingFarmCoverTemplateCatalog catalog)
    : IQueryHandler<GetBreedingFarmCoverTemplatePreviewQuery, GetBreedingFarmCoverTemplatePreviewResult>
{
    public Task<GetBreedingFarmCoverTemplatePreviewResult> Handle(
        GetBreedingFarmCoverTemplatePreviewQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var template = catalog.Find(query.TemplateId);
        if (template is null)
        {
            return Task.FromResult(new GetBreedingFarmCoverTemplatePreviewResult(
                GetBreedingFarmCoverTemplatePreviewStatus.NotFound, null, null));
        }

        if (!template.IsActive)
        {
            return Task.FromResult(new GetBreedingFarmCoverTemplatePreviewResult(
                GetBreedingFarmCoverTemplatePreviewStatus.Inactive, null, null));
        }

        if (!string.Equals(template.Version, query.Version, StringComparison.Ordinal))
        {
            return Task.FromResult(new GetBreedingFarmCoverTemplatePreviewResult(
                GetBreedingFarmCoverTemplatePreviewStatus.VersionUnavailable, null, null));
        }

        return Task.FromResult(new GetBreedingFarmCoverTemplatePreviewResult(
            GetBreedingFarmCoverTemplatePreviewStatus.Available, template.PreviewImage, "image/jpeg"));
    }
}

public sealed class PreviewBreedingFarmCoverTemplateQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IBreedingFarmCoverTemplateCatalog catalog,
    IBreedingFarmCoverRenderer renderer,
    IPrivateObjectStorage storage)
    : IQueryHandler<PreviewBreedingFarmCoverTemplateQuery, PreviewBreedingFarmCoverTemplateResult>
{
    public async Task<PreviewBreedingFarmCoverTemplateResult> Handle(
        PreviewBreedingFarmCoverTemplateQuery query,
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
            return new(access.Status == BreedingFarmVisualIdentityAccessStatus.UserNotFound
                ? PreviewBreedingFarmCoverTemplateStatus.UserNotFound
                : PreviewBreedingFarmCoverTemplateStatus.BreedingFarmNotFound, null);
        }

        var farm = access.Farm!;
        var templateStatus = ResolveTemplate(catalog, query.TemplateId, query.Version, out var template);
        if (template is null || templateStatus != PreviewBreedingFarmCoverTemplateStatus.PreviewReady)
        {
            return new(templateStatus, null);
        }

        if (!BreedingFarmCoverTemplateConfiguration.TryCreate(
                template,
                query.Configuration,
                farm.Name,
                out var configuration))
        {
            return new(PreviewBreedingFarmCoverTemplateStatus.InvalidConfiguration, null);
        }

        var logo = await BreedingFarmCoverLogoResolver.ResolveAsync(
            farm,
            configuration.Values.GetValueOrDefault("logoAssetId") as string,
            storage,
            cancellationToken);
        if (logo.Status == BreedingFarmCoverLogoResolutionStatus.Invalid)
        {
            return new(PreviewBreedingFarmCoverTemplateStatus.InvalidConfiguration, null);
        }

        if (logo.Status == BreedingFarmCoverLogoResolutionStatus.StorageUnavailable)
        {
            return new(PreviewBreedingFarmCoverTemplateStatus.RenderingUnavailable, null);
        }

        try
        {
            var png = await renderer.RenderTemplatePngAsync(template, configuration.Values, logo.DataUrl, cancellationToken);
            return new(PreviewBreedingFarmCoverTemplateStatus.PreviewReady, png);
        }
        catch (InvalidOperationException)
        {
            return new(PreviewBreedingFarmCoverTemplateStatus.RenderingUnavailable, null);
        }
        catch (TimeoutException)
        {
            return new(PreviewBreedingFarmCoverTemplateStatus.RenderingUnavailable, null);
        }
    }

    internal static PreviewBreedingFarmCoverTemplateStatus ResolveTemplate(
        IBreedingFarmCoverTemplateCatalog catalog,
        string templateId,
        string version,
        out BreedingFarmCoverTemplateDefinition? template)
    {
        template = catalog.Find(templateId);
        if (template is null)
        {
            return PreviewBreedingFarmCoverTemplateStatus.TemplateNotFound;
        }

        if (!template.IsActive)
        {
            return PreviewBreedingFarmCoverTemplateStatus.TemplateInactive;
        }

        if (!string.Equals(template.Version, version, StringComparison.Ordinal))
        {
            return PreviewBreedingFarmCoverTemplateStatus.VersionUnavailable;
        }

        return PreviewBreedingFarmCoverTemplateStatus.PreviewReady;
    }
}

public sealed class ApplyBreedingFarmCoverTemplateSession(IPrivateObjectStorage storage)
    : ICommandFailureCompensator
{
    public ApplyBreedingFarmCoverTemplateStatus Status { get; private set; } =
        ApplyBreedingFarmCoverTemplateStatus.TemplateNotFound;

    public BreedingFarmCoverTemplateDefinition? Template { get; private set; }

    public string? EffectiveConfiguration { get; private set; }

    public PrivateObjectDescriptor? StoredObject { get; private set; }

    public void SetStatus(ApplyBreedingFarmCoverTemplateStatus status) => Status = status;

    public void SetRenderedObject(
        BreedingFarmCoverTemplateDefinition template,
        string effectiveConfiguration,
        PrivateObjectDescriptor storedObject)
    {
        Template = template;
        EffectiveConfiguration = effectiveConfiguration;
        StoredObject = storedObject;
        Status = ApplyBreedingFarmCoverTemplateStatus.Applied;
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

public sealed class ApplyBreedingFarmCoverTemplatePreProcessor(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage,
    IBreedingFarmCoverTemplateCatalog catalog,
    IBreedingFarmCoverRenderer renderer,
    ApplyBreedingFarmCoverTemplateSession session)
    : ICommandPreProcessor<ApplyBreedingFarmCoverTemplateCommand>
{
    public async Task Process(
        ApplyBreedingFarmCoverTemplateCommand command,
        CancellationToken cancellationToken)
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
            session.SetStatus(access.Status == BreedingFarmCoverAccessStatus.UserNotFound
                ? ApplyBreedingFarmCoverTemplateStatus.UserNotFound
                : ApplyBreedingFarmCoverTemplateStatus.BreedingFarmNotFound);
            return;
        }

        var farm = access.Farm!;
        var templateStatus = PreviewBreedingFarmCoverTemplateQueryHandler.ResolveTemplate(
            catalog,
            command.TemplateId,
            command.Version,
            out var template);
        if (template is null || templateStatus != PreviewBreedingFarmCoverTemplateStatus.PreviewReady)
        {
            session.SetStatus(templateStatus switch
            {
                PreviewBreedingFarmCoverTemplateStatus.TemplateInactive => ApplyBreedingFarmCoverTemplateStatus.TemplateInactive,
                PreviewBreedingFarmCoverTemplateStatus.VersionUnavailable => ApplyBreedingFarmCoverTemplateStatus.VersionUnavailable,
                _ => ApplyBreedingFarmCoverTemplateStatus.TemplateNotFound
            });
            return;
        }

        if (!BreedingFarmCoverTemplateConfiguration.TryCreate(
                template,
                command.Configuration,
                farm.Name,
                out var configuration))
        {
            session.SetStatus(ApplyBreedingFarmCoverTemplateStatus.InvalidConfiguration);
            return;
        }

        var logo = await BreedingFarmCoverLogoResolver.ResolveAsync(
            farm,
            configuration.Values.GetValueOrDefault("logoAssetId") as string,
            storage,
            cancellationToken);
        if (logo.Status != BreedingFarmCoverLogoResolutionStatus.Success)
        {
            session.SetStatus(logo.Status == BreedingFarmCoverLogoResolutionStatus.Invalid
                ? ApplyBreedingFarmCoverTemplateStatus.InvalidConfiguration
                : ApplyBreedingFarmCoverTemplateStatus.StorageUnavailable);
            return;
        }

        byte[] png;
        try
        {
            png = await renderer.RenderTemplatePngAsync(template, configuration.Values, logo.DataUrl, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            session.SetStatus(ApplyBreedingFarmCoverTemplateStatus.RenderingUnavailable);
            return;
        }
        catch (TimeoutException)
        {
            session.SetStatus(ApplyBreedingFarmCoverTemplateStatus.RenderingUnavailable);
            return;
        }

        if (png.Length is <= 0 or > BreedingFarmCoverUploadLimits.MaxCanonicalFileLength ||
            !BreedingFarmCoverImageValidation.TryValidate("cover.png", "image/png", png, out _, out _))
        {
            session.SetStatus(ApplyBreedingFarmCoverTemplateStatus.RenderingUnavailable);
            return;
        }

        var objectKey = $"cover/{Guid.NewGuid():N}.png";
        try
        {
            await using var content = new MemoryStream(png, writable: false);
            var storedObject = await storage.PutAsync(
                new PrivateObjectUpload(farm.Id, objectKey, "cover.png", "image/png", content),
                cancellationToken);
            session.SetRenderedObject(template, configuration.Serialized, storedObject);
            if (storedObject.BreedingFarmId != farm.Id ||
                !string.Equals(storedObject.ObjectKey, objectKey, StringComparison.Ordinal) ||
                storedObject.Length != png.Length ||
                !string.Equals(storedObject.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            {
                session.SetStatus(ApplyBreedingFarmCoverTemplateStatus.StorageUnavailable);
            }
        }
        catch (ArgumentException)
        {
            session.SetStatus(ApplyBreedingFarmCoverTemplateStatus.StorageUnavailable);
        }
        catch (IOException)
        {
            session.SetStatus(ApplyBreedingFarmCoverTemplateStatus.StorageUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            session.SetStatus(ApplyBreedingFarmCoverTemplateStatus.StorageUnavailable);
        }
    }
}

public sealed class ApplyBreedingFarmCoverTemplateCommandHandler(
    CriatorioVirtualDbContext dbContext,
    ApplyBreedingFarmCoverTemplateSession session)
    : ICommandHandler<ApplyBreedingFarmCoverTemplateCommand, ApplyBreedingFarmCoverTemplateResult>
{
    public async Task<ApplyBreedingFarmCoverTemplateResult> Handle(
        ApplyBreedingFarmCoverTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (session.Status != ApplyBreedingFarmCoverTemplateStatus.Applied ||
            session.Template is null || session.StoredObject is null || session.EffectiveConfiguration is null)
        {
            return new(session.Status, null, null, null);
        }

        var access = await BreedingFarmCoverAccess.FindCurrentOwnerFarmAsync(
            dbContext,
            command.UserId,
            command.BreedingFarmId,
            tracking: true,
            cancellationToken);
        if (access.Status != BreedingFarmCoverAccessStatus.Success)
        {
            return new(access.Status == BreedingFarmCoverAccessStatus.UserNotFound
                ? ApplyBreedingFarmCoverTemplateStatus.UserNotFound
                : ApplyBreedingFarmCoverTemplateStatus.BreedingFarmNotFound, null, null, null);
        }

        var farm = access.Farm!;
        if (session.StoredObject.BreedingFarmId != farm.Id)
        {
            return new(ApplyBreedingFarmCoverTemplateStatus.BreedingFarmNotFound, null, null, null);
        }

        var previous = farm.GetCover();
        var cleanup = previous is null ? null : new BreedingFarmCoverCleanup(farm.Id, previous.Reference);
        farm.SetCover(
            BreedingFarmCoverSource.Template,
            session.StoredObject.ObjectKey,
            "cover.png",
            "image/png",
            session.StoredObject.Length,
            DateTimeOffset.UtcNow,
            session.Template.Id,
            session.Template.Version,
            session.EffectiveConfiguration);
        return new(
            ApplyBreedingFarmCoverTemplateStatus.Applied,
            farm.Id,
            BreedingFarmCoverMapping.ToMetadata(farm),
            cleanup);
    }
}

public sealed class ApplyBreedingFarmCoverTemplatePostProcessor(
    IPrivateObjectStorage storage,
    ApplyBreedingFarmCoverTemplateSession session)
    : ICommandPostProcessor<ApplyBreedingFarmCoverTemplateCommand, ApplyBreedingFarmCoverTemplateResult>
{
    public async Task<ApplyBreedingFarmCoverTemplateResult> Process(
        ApplyBreedingFarmCoverTemplateCommand command,
        ApplyBreedingFarmCoverTemplateResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status == ApplyBreedingFarmCoverTemplateStatus.Applied)
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
            await session.CompensateAsync(cancellationToken);
        }

        return result;
    }
}
