using CriatorioVirtual.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("species-images")]
public sealed class SpeciesDefaultImageController(
    IOptions<SpeciesDefaultImageStorageOptions> options) : ControllerBase
{
    [HttpGet("{fileName}", Name = "GetSpeciesDefaultImage")]
    [ResponseCache(Duration = 31_536_000, Location = ResponseCacheLocation.Any)]
    [Produces("image/jpeg")]
    public IActionResult Get(string fileName)
    {
        if (!SpeciesDefaultImageCatalog.TryGetContentType(fileName, out var contentType) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            return NotFound();
        }

        var rootPath = Path.GetFullPath(options.Value.SpeciesDefaultImagesRootPath);
        var imagePath = Path.GetFullPath(Path.Combine(rootPath, fileName));
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(rootPath) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!imagePath.StartsWith(rootWithSeparator, comparison) || !System.IO.File.Exists(imagePath))
        {
            return NotFound();
        }

        return PhysicalFile(imagePath, contentType, enableRangeProcessing: true);
    }
}
