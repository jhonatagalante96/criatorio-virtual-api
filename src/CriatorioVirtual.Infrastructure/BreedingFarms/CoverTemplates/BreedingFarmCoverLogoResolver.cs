using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;

namespace CriatorioVirtual.Infrastructure.BreedingFarms.CoverTemplates;

internal enum BreedingFarmCoverLogoResolutionStatus
{
    Success,
    Invalid,
    StorageUnavailable
}

internal sealed record BreedingFarmCoverLogoResolution(
    BreedingFarmCoverLogoResolutionStatus Status,
    string? DataUrl);

internal static class BreedingFarmCoverLogoResolver
{
    private const int MaximumLogoLength = 2 * 1024 * 1024;

    public static async Task<BreedingFarmCoverLogoResolution> ResolveAsync(
        BreedingFarm farm,
        string? logoAssetId,
        IPrivateObjectStorage storage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(storage);
        if (logoAssetId is null)
        {
            return new(BreedingFarmCoverLogoResolutionStatus.Success, null);
        }

        var identity = farm.GetVisualIdentity();
        if (logoAssetId != "current" || identity?.FileName is null || identity.ContentType is null ||
            identity.ContentType is not ("image/png" or "image/jpeg"))
        {
            return new(BreedingFarmCoverLogoResolutionStatus.Invalid, null);
        }

        try
        {
            await using var source = await storage.OpenReadAsync(farm.Id, identity.Reference, cancellationToken);
            await using var content = new MemoryStream();
            var buffer = new byte[32 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (content.Length + read > MaximumLogoLength)
                {
                    return new(BreedingFarmCoverLogoResolutionStatus.Invalid, null);
                }

                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (!content.TryGetBuffer(out var bytes) ||
                !BreedingFarmVisualIdentityImageValidation.TryValidate(
                    identity.FileName,
                    identity.ContentType,
                    bytes.AsSpan(0, (int)content.Length),
                    out _))
            {
                return new(BreedingFarmCoverLogoResolutionStatus.Invalid, null);
            }

            var dataUrl = $"data:{identity.ContentType};base64,{Convert.ToBase64String(bytes.AsSpan(0, (int)content.Length))}";
            return new(BreedingFarmCoverLogoResolutionStatus.Success, dataUrl);
        }
        catch (FileNotFoundException)
        {
            return new(BreedingFarmCoverLogoResolutionStatus.StorageUnavailable, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new(BreedingFarmCoverLogoResolutionStatus.StorageUnavailable, null);
        }
        catch (IOException)
        {
            return new(BreedingFarmCoverLogoResolutionStatus.StorageUnavailable, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new(BreedingFarmCoverLogoResolutionStatus.StorageUnavailable, null);
        }
    }
}
