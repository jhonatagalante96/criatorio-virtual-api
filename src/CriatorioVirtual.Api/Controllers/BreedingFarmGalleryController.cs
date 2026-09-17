using System.Security.Claims;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Birds;
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? type,
        [FromQuery] Guid? birdId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        BreedingFarmGalleryMediaType? mediaType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<BreedingFarmGalleryMediaType>(type, ignoreCase: true, out var parsedType) ||
                !Enum.IsDefined(parsedType))
            {
                return ValidationProblemResult("The type filter must be image or video.", "type");
            }

            mediaType = parsedType;
        }

        var requestedPage = page ?? 1;
        var requestedPageSize = pageSize ?? BreedingFarmGalleryLimits.DefaultPageSize;
        if (requestedPage <= 0 || requestedPageSize <= 0 || requestedPageSize > BreedingFarmGalleryLimits.MaxPageSize)
        {
            return ValidationProblemResult(
                $"Page must be positive and pageSize must be between 1 and {BreedingFarmGalleryLimits.MaxPageSize}.",
                "page");
        }

        var result = await queryExecutor.Execute<GetBreedingFarmGalleryQuery, GetBreedingFarmGalleryResult>(
            new GetBreedingFarmGalleryQuery(userId, requestedPage, requestedPageSize, mediaType, birdId),
            cancellationToken);
        if (result.Status != GetBreedingFarmGalleryStatus.Success)
        {
            return result.Status switch
            {
                GetBreedingFarmGalleryStatus.UserNotFound => AuthenticationRequired(),
                GetBreedingFarmGalleryStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("viewing the gallery"),
                GetBreedingFarmGalleryStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
                GetBreedingFarmGalleryStatus.BirdNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The bird was not found in the selected breeding farm.",
                    type: "https://httpstatuses.com/404"),
                _ => ValidationProblemResult("The gallery query is invalid.", "page")
            };
        }

        return Ok(new BreedingFarmGalleryResponse(
            result.BreedingFarmId!.Value,
            result.Page,
            result.PageSize,
            result.TotalCount,
            (long)result.Page * result.PageSize < result.TotalCount,
            BreedingFarmGalleryLimits.Current,
            result.Items.Select(ToResponse).ToArray()));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(BirdAttachmentUploadLimits.MaxRequestLength)]
    [RequestFormLimits(MultipartBodyLengthLimit = BirdAttachmentUploadLimits.MaxRequestLength)]
    [ProducesResponseType(typeof(BreedingFarmGalleryMediaResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadAsync(
        [FromForm] UploadBreedingFarmGalleryMediaRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var file = request?.File;
        if (file is null)
        {
            return ValidationProblemResult("A media file is required.", "file");
        }

        var validMetadata = PrivateObjectStorageFileValidation.TryValidateMetadata(
            file.FileName,
            file.ContentType,
            out var metadataError);
        if (!PrivateObjectStorageFileValidation.IsSupportedMediaContentType(file.ContentType) || !validMetadata)
        {
            return ValidationProblemResult(
                validMetadata ? "Only supported image and video files are accepted." : metadataError,
                "file");
        }

        var uploadRequest = request!;
        var maxFileLength = PrivateObjectStorageFileValidation.IsSupportedVideoContentType(file.ContentType)
            ? BirdAttachmentUploadLimits.MaxVideoFileLength
            : BirdAttachmentUploadLimits.MaxFileLength;
        if (file.Length <= 0 || file.Length > maxFileLength)
        {
            return ValidationProblemResult($"The media file must be between 1 and {maxFileLength} bytes.", "file");
        }

        if (request?.Caption?.Length > BirdAttachment.CaptionMaxLength)
        {
            return ValidationProblemResult(
                $"A caption cannot exceed {BirdAttachment.CaptionMaxLength} characters.",
                "caption");
        }

        await using var content = file.OpenReadStream();
        var result = await commandExecutor.Execute<UploadBirdAttachmentCommand, UploadBirdAttachmentResult>(
            new UploadBirdAttachmentCommand(
                userId,
                uploadRequest.BirdId,
                file.FileName,
                file.ContentType,
                file.Length,
                uploadRequest.Caption,
                content),
            cancellationToken);

        if (result.Status == UploadBirdAttachmentStatus.Created)
        {
            var media = result.Attachment!;
            var response = ToResponse(new BreedingFarmGalleryMediaResult(
                media.AttachmentId,
                media.BirdId,
                media.BirdName,
                media.BirdRingNumber,
                media.FileName,
                media.ContentType,
                media.Length,
                media.Caption,
                media.CreatedAtUtc,
                media.CreatedAtUtc,
                media.IsPrimary));
            return Created(ContentUrl(media.AttachmentId), response);
        }

        return result.Status switch
        {
            UploadBirdAttachmentStatus.UserNotFound => AuthenticationRequired(),
            UploadBirdAttachmentStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("uploading media"),
            UploadBirdAttachmentStatus.BreedingFarmNotFound or UploadBirdAttachmentStatus.BirdNotFound => BreedingFarmNotFound(),
            UploadBirdAttachmentStatus.BirdTransferPending => BirdTransferPending(),
            UploadBirdAttachmentStatus.InvalidData => ValidationProblemResult("The media data is invalid.", "file"),
            UploadBirdAttachmentStatus.StorageUnavailable => StorageUnavailable(),
            _ => throw new InvalidOperationException("The gallery upload result is not supported.")
        };
    }

    [HttpPut("{mediaId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BreedingFarmGalleryMediaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCaptionAsync(
        Guid mediaId,
        [FromBody] UpdateBreedingFarmGalleryCaptionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        if (request is null || request.Caption?.Length > BirdAttachment.CaptionMaxLength)
        {
            return ValidationProblemResult(
                request is null ? "A caption field is required; use null to clear it." :
                $"A caption cannot exceed {BirdAttachment.CaptionMaxLength} characters.",
                "caption");
        }

        var result = await commandExecutor.Execute<
            UpdateBreedingFarmGalleryCaptionCommand,
            UpdateBreedingFarmGalleryCaptionResult>(
            new UpdateBreedingFarmGalleryCaptionCommand(userId, mediaId, request.Caption),
            cancellationToken);
        return result.Status switch
        {
            UpdateBreedingFarmGalleryCaptionStatus.Updated => Ok(ToResponse(result.Media!)),
            UpdateBreedingFarmGalleryCaptionStatus.UserNotFound => AuthenticationRequired(),
            UpdateBreedingFarmGalleryCaptionStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("editing media"),
            UpdateBreedingFarmGalleryCaptionStatus.BreedingFarmNotFound or
                UpdateBreedingFarmGalleryCaptionStatus.MediaNotFound => MediaNotFound(),
            UpdateBreedingFarmGalleryCaptionStatus.BirdTransferPending => BirdTransferPending(),
            UpdateBreedingFarmGalleryCaptionStatus.InvalidData => ValidationProblemResult("The caption is invalid.", "caption"),
            _ => throw new InvalidOperationException("The gallery caption result is not supported.")
        };
    }

    [HttpGet("{mediaId:guid}/content", Name = "GetBreedingFarmGalleryMediaContent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetContentAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<
            GetBreedingFarmGalleryMediaContentQuery,
            GetBreedingFarmGalleryMediaContentResult>(
            new GetBreedingFarmGalleryMediaContentQuery(userId, mediaId),
            cancellationToken);
        if (result.Status == GetBreedingFarmGalleryMediaContentStatus.Success)
        {
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(result.Content!.Content, result.Content.ContentType, enableRangeProcessing: true);
        }

        return result.Status switch
        {
            GetBreedingFarmGalleryMediaContentStatus.UserNotFound => AuthenticationRequired(),
            GetBreedingFarmGalleryMediaContentStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("viewing media"),
            GetBreedingFarmGalleryMediaContentStatus.BreedingFarmNotFound or
                GetBreedingFarmGalleryMediaContentStatus.MediaNotFound => MediaNotFound(),
            GetBreedingFarmGalleryMediaContentStatus.StorageUnavailable => StorageUnavailable(),
            _ => throw new InvalidOperationException("The gallery content result is not supported.")
        };
    }

    [HttpDelete("{mediaId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DeleteAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await commandExecutor.Execute<DeleteBirdAttachmentCommand, DeleteBirdAttachmentResult>(
            new DeleteBirdAttachmentCommand(userId, null, mediaId, Confirmed: true),
            cancellationToken);
        return result.Status switch
        {
            DeleteBirdAttachmentStatus.Deleted => NoContent(),
            DeleteBirdAttachmentStatus.UserNotFound => AuthenticationRequired(),
            DeleteBirdAttachmentStatus.BreedingFarmNotSelected => BreedingFarmNotSelected("removing media"),
            DeleteBirdAttachmentStatus.BreedingFarmNotFound or
                DeleteBirdAttachmentStatus.BirdNotFound or
                DeleteBirdAttachmentStatus.AttachmentNotFound => MediaNotFound(),
            DeleteBirdAttachmentStatus.PrimaryPhotoMustBeReplaced => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Replace the bird's primary photo before removing this media.",
                type: "https://httpstatuses.com/409"),
            DeleteBirdAttachmentStatus.BirdTransferPending => BirdTransferPending(),
            DeleteBirdAttachmentStatus.StorageCleanupPending => StorageUnavailable(),
            _ => throw new InvalidOperationException("The gallery removal result is not supported.")
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private BreedingFarmGalleryMediaResponse ToResponse(BreedingFarmGalleryMediaResult media) =>
        new(
            media.MediaId,
            media.BirdId is { } birdId
                ? new BreedingFarmGalleryBirdReference(birdId, media.BirdName ?? string.Empty, media.BirdRingNumber)
                : null,
            media.FileName,
            media.ContentType,
            media.Length,
            media.Caption,
            media.CreatedAtUtc,
            media.UpdatedAtUtc,
            media.IsPrimary,
            ContentUrl(media.MediaId));

    private string ContentUrl(Guid mediaId) => Url.RouteUrl(
        "GetBreedingFarmGalleryMediaContent",
        new { mediaId }) ?? $"/api/breeding-farms/gallery/{mediaId}/content";

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
        title: "The breeding farm or bird was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult MediaNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The gallery media was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult BirdTransferPending() => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Media linked to a bird cannot be changed while its transfer is pending.",
        type: "https://httpstatuses.com/409");

    private IActionResult StorageUnavailable() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Private media storage is temporarily unavailable.",
        type: "https://httpstatuses.com/503");

    private IActionResult ValidationProblemResult(string message, string key)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { [key] = [message] })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Gallery media data is invalid.",
            Type = "https://httpstatuses.com/400"
        };
        return BadRequest(problem);
    }
}

public sealed record BreedingFarmGalleryResponse(
    Guid BreedingFarmId,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasNextPage,
    BreedingFarmGalleryContractLimits Limits,
    IReadOnlyCollection<BreedingFarmGalleryMediaResponse> Items);

public sealed record BreedingFarmGalleryBirdReference(Guid BirdId, string Name, string? RingNumber);

public sealed record BreedingFarmGalleryMediaResponse(
    Guid MediaId,
    BreedingFarmGalleryBirdReference? Bird,
    string FileName,
    string ContentType,
    long Length,
    string? Caption,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsPrimary,
    string ContentUrl);

public sealed class UploadBreedingFarmGalleryMediaRequest
{
    public IFormFile? File { get; set; }

    public Guid? BirdId { get; set; }

    public string? Caption { get; set; }
}

public sealed record UpdateBreedingFarmGalleryCaptionRequest(string? Caption);
