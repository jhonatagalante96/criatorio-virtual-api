using System.Security.Claims;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/breeding-farms/gallery")]
public sealed class BreedingFarmGalleryController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(BreedingFarmGalleryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<GetBreedingFarmGalleryQuery, GetBreedingFarmGalleryResult>(
            new GetBreedingFarmGalleryQuery(userId),
            cancellationToken);
        if (result.Status != BreedingFarmVisualIdentityAccessStatus.Success)
        {
            return AccessProblem(result.Status, "viewing the gallery");
        }

        return Ok(new BreedingFarmGalleryResponse(
            result.BreedingFarmId!.Value,
            result.Items.Select(ToResponse).ToArray(),
            BreedingFarmGalleryLimits.Current));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(BreedingFarmGalleryUploadLimits.MaxRequestLength)]
    [RequestFormLimits(MultipartBodyLengthLimit = BreedingFarmGalleryUploadLimits.MaxRequestLength)]
    [ProducesResponseType(typeof(BreedingFarmGalleryImageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadAsync(
        [FromForm] UploadBreedingFarmGalleryImageRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var file = request?.File;
        if (file is null)
        {
            return ValidationProblemResult("An image file is required.", "file");
        }

        if (file.Length <= 0 || file.Length > BreedingFarmGalleryUploadLimits.MaxFileLength)
        {
            return ValidationProblemResult(
                $"The image must be between 1 and {BreedingFarmGalleryUploadLimits.MaxFileLength} bytes.",
                "file");
        }

        if (!TryGetContentType(file.ContentType, out _))
        {
            return ValidationProblemResult("Only PNG, JPEG, and WebP images are accepted.", "file");
        }

        await using var content = file.OpenReadStream();
        var result = await commandExecutor.Execute<
            UploadBreedingFarmGalleryImageCommand,
            UploadBreedingFarmGalleryImageResult>(
            new UploadBreedingFarmGalleryImageCommand(
                userId,
                file.FileName,
                file.ContentType,
                file.Length,
                request?.Caption,
                content),
            cancellationToken);

        return result.Status switch
        {
            UploadBreedingFarmGalleryImageStatus.Created => Created(
                ContentUrl(result.Image!.ImageId),
                ToResponse(result.Image)),
            UploadBreedingFarmGalleryImageStatus.UserNotFound => AuthenticationRequired(),
            UploadBreedingFarmGalleryImageStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("uploading an image"),
            UploadBreedingFarmGalleryImageStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            UploadBreedingFarmGalleryImageStatus.LimitExceeded => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"The gallery is limited to {BreedingFarmGalleryUploadLimits.MaxImageCount} images.",
                type: "https://httpstatuses.com/409"),
            UploadBreedingFarmGalleryImageStatus.InvalidData => ValidationProblemResult(
                "The image content, dimensions, caption, or file metadata are invalid.",
                "file"),
            UploadBreedingFarmGalleryImageStatus.StorageUnavailable => StorageUnavailable(),
            _ => throw new InvalidOperationException("The gallery upload result is not supported.")
        };
    }

    [HttpPut("{imageId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BreedingFarmGalleryImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCaptionAsync(
        Guid imageId,
        [FromBody] UpdateBreedingFarmGalleryCaptionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        if (request is null)
        {
            return ValidationProblemResult("A caption field is required; use null to clear it.", "caption");
        }

        if (request.Caption?.Length > BreedingFarmGalleryLimits.Current.MaxCaptionLength)
        {
            return ValidationProblemResult(
                $"A caption cannot exceed {BreedingFarmGalleryLimits.Current.MaxCaptionLength} characters.",
                nameof(request.Caption));
        }

        var result = await commandExecutor.Execute<
            UpdateBreedingFarmGalleryCaptionCommand,
            UpdateBreedingFarmGalleryCaptionResult>(
            new UpdateBreedingFarmGalleryCaptionCommand(userId, imageId, request.Caption),
            cancellationToken);

        return result.Status switch
        {
            UpdateBreedingFarmGalleryCaptionStatus.Updated => Ok(ToResponse(result.Image!)),
            UpdateBreedingFarmGalleryCaptionStatus.UserNotFound => AuthenticationRequired(),
            UpdateBreedingFarmGalleryCaptionStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("updating a gallery image"),
            UpdateBreedingFarmGalleryCaptionStatus.BreedingFarmNotFound or
                UpdateBreedingFarmGalleryCaptionStatus.ImageNotFound => ImageNotFound(),
            UpdateBreedingFarmGalleryCaptionStatus.InvalidData => ValidationProblemResult("The caption is invalid.", "caption"),
            _ => throw new InvalidOperationException("The gallery caption result is not supported.")
        };
    }

    [HttpGet("{imageId:guid}/content", Name = "GetBreedingFarmGalleryImageContent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetContentAsync(Guid imageId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<
            GetBreedingFarmGalleryImageContentQuery,
            GetBreedingFarmGalleryImageContentResult>(
            new GetBreedingFarmGalleryImageContentQuery(userId, imageId),
            cancellationToken);
        if (result.Status == GetBreedingFarmGalleryImageContentStatus.Success)
        {
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(result.Content!.Content, result.Content.ContentType, enableRangeProcessing: true);
        }

        return result.Status switch
        {
            GetBreedingFarmGalleryImageContentStatus.UserNotFound => AuthenticationRequired(),
            GetBreedingFarmGalleryImageContentStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("viewing an image"),
            GetBreedingFarmGalleryImageContentStatus.BreedingFarmNotFound or
                GetBreedingFarmGalleryImageContentStatus.ImageNotFound => ImageNotFound(),
            GetBreedingFarmGalleryImageContentStatus.StorageUnavailable => StorageUnavailable(),
            _ => throw new InvalidOperationException("The gallery image content result is not supported.")
        };
    }

    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DeleteAsync(Guid imageId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await commandExecutor.Execute<
            DeleteBreedingFarmGalleryImageCommand,
            DeleteBreedingFarmGalleryImageResult>(
            new DeleteBreedingFarmGalleryImageCommand(userId, imageId),
            cancellationToken);
        return result.Status switch
        {
            DeleteBreedingFarmGalleryImageStatus.Deleted => NoContent(),
            DeleteBreedingFarmGalleryImageStatus.UserNotFound => AuthenticationRequired(),
            DeleteBreedingFarmGalleryImageStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("removing an image"),
            DeleteBreedingFarmGalleryImageStatus.BreedingFarmNotFound or
                DeleteBreedingFarmGalleryImageStatus.ImageNotFound => ImageNotFound(),
            DeleteBreedingFarmGalleryImageStatus.StorageCleanupPending => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The image was removed from the gallery, but private storage cleanup is pending. Retry the request.",
                type: "https://httpstatuses.com/503"),
            _ => throw new InvalidOperationException("The gallery removal result is not supported.")
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult AccessProblem(BreedingFarmVisualIdentityAccessStatus status, string operation) => status switch
    {
        BreedingFarmVisualIdentityAccessStatus.UserNotFound => AuthenticationRequired(),
        BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => BreedingFarmNotSelected(operation),
        _ => BreedingFarmNotFound()
    };

    private IActionResult AuthenticationRequired() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Authentication is required.",
        type: "https://httpstatuses.com/401");

    private IActionResult BreedingFarmNotSelected(string operation) => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: $"A breeding farm must be selected before {operation}.",
        type: "https://httpstatuses.com/409");

    private IActionResult BreedingFarmNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The breeding farm was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult ImageNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The gallery image was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult StorageUnavailable() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Private gallery storage is temporarily unavailable.",
        type: "https://httpstatuses.com/503");

    private IActionResult ValidationProblemResult(string message, string key)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { [key] = [message] })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Gallery image data is invalid.",
            Type = "https://httpstatuses.com/400"
        };
        return BadRequest(problem);
    }

    private static bool TryGetContentType(string contentType, out string normalized)
    {
        normalized = contentType.Trim().ToLowerInvariant();
        return normalized is "image/jpeg" or "image/png" or "image/webp";
    }

    private string ContentUrl(Guid imageId) => Url.RouteUrl(
        "GetBreedingFarmGalleryImageContent",
        new { imageId }) ?? $"/api/breeding-farms/gallery/{imageId}/content";

    private BreedingFarmGalleryImageResponse ToResponse(BreedingFarmGalleryImageMetadata item) =>
        new(
            item.ImageId,
            item.FileName,
            item.ContentType,
            item.Length,
            item.Width,
            item.Height,
            item.Caption,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            ContentUrl(item.ImageId));
}

public sealed record BreedingFarmGalleryResponse(
    Guid BreedingFarmId,
    IReadOnlyCollection<BreedingFarmGalleryImageResponse> Items,
    BreedingFarmGalleryLimits Limits);

public sealed record BreedingFarmGalleryImageResponse(
    Guid ImageId,
    string FileName,
    string ContentType,
    long Length,
    int Width,
    int Height,
    string? Caption,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string ContentUrl);

public sealed class UploadBreedingFarmGalleryImageRequest
{
    public IFormFile? File { get; set; }

    public string? Caption { get; set; }
}

public sealed record UpdateBreedingFarmGalleryCaptionRequest(string? Caption);
