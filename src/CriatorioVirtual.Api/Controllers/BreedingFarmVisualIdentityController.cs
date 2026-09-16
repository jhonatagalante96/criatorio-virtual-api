using System.Security.Claims;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/breeding-farms/visual-identity")]
public sealed class BreedingFarmVisualIdentityController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "GetBreedingFarmVisualIdentity")]
    [ProducesResponseType(typeof(BreedingFarmVisualIdentityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<GetBreedingFarmVisualIdentityQuery, GetBreedingFarmVisualIdentityResult>(
            new GetBreedingFarmVisualIdentityQuery(userId),
            cancellationToken);
        return result.Status switch
        {
            BreedingFarmVisualIdentityAccessStatus.Success => Ok(ToResponse(result)),
            BreedingFarmVisualIdentityAccessStatus.UserNotFound => AuthenticationRequired(),
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected => BreedingFarmNotSelected(),
            BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            _ => throw new InvalidOperationException("The visual identity query result is not supported.")
        };
    }

    [HttpGet("content", Name = "GetBreedingFarmVisualIdentityContent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetContentAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<GetBreedingFarmVisualIdentityContentQuery, GetBreedingFarmVisualIdentityContentResult>(
            new GetBreedingFarmVisualIdentityContentQuery(userId),
            cancellationToken);
        switch (result.Status)
        {
            case GetBreedingFarmVisualIdentityContentStatus.Success:
                Response.Headers["Cache-Control"] = "private, no-store";
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                return File(result.Content!, result.ContentType!, enableRangeProcessing: true);
            case GetBreedingFarmVisualIdentityContentStatus.UserNotFound:
                return AuthenticationRequired();
            case GetBreedingFarmVisualIdentityContentStatus.BreedingFarmNotSelected:
                return BreedingFarmNotSelected();
            case GetBreedingFarmVisualIdentityContentStatus.BreedingFarmNotFound:
            case GetBreedingFarmVisualIdentityContentStatus.IdentityNotFound:
                return BreedingFarmNotFound();
            case GetBreedingFarmVisualIdentityContentStatus.StorageUnavailable:
                return Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Private visual identity storage is temporarily unavailable.",
                    type: "https://httpstatuses.com/503");
            default:
                throw new InvalidOperationException("The visual identity content result is not supported.");
        }
    }

    [HttpPut(Name = "UploadBreedingFarmVisualIdentity")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(BreedingFarmVisualIdentityUploadLimits.MaxRequestLength)]
    [RequestFormLimits(MultipartBodyLengthLimit = BreedingFarmVisualIdentityUploadLimits.MaxRequestLength)]
    [ProducesResponseType(typeof(BreedingFarmVisualIdentityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadAsync(
        [FromForm] UploadBreedingFarmVisualIdentityRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var file = request?.File;
        if (file is null)
        {
            return InvalidFile("A visual identity image is required.");
        }

        if (file.Length <= 0)
        {
            return InvalidFile("The visual identity image cannot be empty.");
        }

        if (file.Length > BreedingFarmVisualIdentityUploadLimits.MaxFileLength)
        {
            return InvalidFile(
                $"The visual identity image cannot exceed {BreedingFarmVisualIdentityUploadLimits.MaxFileLength} bytes.");
        }

        if (!IsSupportedImageMetadata(file.FileName, file.ContentType))
        {
            return InvalidFile("Only PNG and JPEG images with matching file extensions are accepted.");
        }

        await using var content = file.OpenReadStream();
        var result = await commandExecutor.Execute<UploadBreedingFarmVisualIdentityCommand, UploadBreedingFarmVisualIdentityResult>(
            new UploadBreedingFarmVisualIdentityCommand(
                userId,
                file.FileName,
                file.ContentType,
                file.Length,
                content),
            cancellationToken);

        return result.Status switch
        {
            UploadBreedingFarmVisualIdentityStatus.Updated => Ok(ToResponse(result)),
            UploadBreedingFarmVisualIdentityStatus.UserNotFound => AuthenticationRequired(),
            UploadBreedingFarmVisualIdentityStatus.BreedingFarmNotSelected => BreedingFarmNotSelected(),
            UploadBreedingFarmVisualIdentityStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            UploadBreedingFarmVisualIdentityStatus.InvalidData => InvalidFile("The visual identity image is invalid."),
            UploadBreedingFarmVisualIdentityStatus.StorageUnavailable => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Private visual identity storage is temporarily unavailable.",
                type: "https://httpstatuses.com/503"),
            _ => throw new InvalidOperationException("The visual identity upload result is not supported.")
        };
    }

    [HttpDelete(Name = "RemoveBreedingFarmVisualIdentity")]
    [ProducesResponseType(typeof(BreedingFarmVisualIdentityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await commandExecutor.Execute<RemoveBreedingFarmVisualIdentityCommand, RemoveBreedingFarmVisualIdentityResult>(
            new RemoveBreedingFarmVisualIdentityCommand(userId),
            cancellationToken);
        return result.Status switch
        {
            RemoveBreedingFarmVisualIdentityStatus.Removed => Ok(
                new BreedingFarmVisualIdentityResponse(result.BreedingFarmId!.Value, null)),
            RemoveBreedingFarmVisualIdentityStatus.UserNotFound => AuthenticationRequired(),
            RemoveBreedingFarmVisualIdentityStatus.BreedingFarmNotSelected => BreedingFarmNotSelected(),
            RemoveBreedingFarmVisualIdentityStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            _ => throw new InvalidOperationException("The visual identity removal result is not supported.")
        };
    }

    private BreedingFarmVisualIdentityResponse ToResponse(GetBreedingFarmVisualIdentityResult result) =>
        new(result.BreedingFarmId!.Value, ToResponse(result.Identity));

    private BreedingFarmVisualIdentityResponse ToResponse(UploadBreedingFarmVisualIdentityResult result) =>
        new(result.BreedingFarmId!.Value, ToResponse(result.Identity));

    private BreedingFarmVisualIdentityItemResponse? ToResponse(BreedingFarmVisualIdentityMetadata? identity) =>
        identity is null
            ? null
            : new(
                identity.Source.ToString(),
                identity.FileName,
                identity.ContentType,
                identity.Length,
                identity.UpdatedAtUtc,
                identity.Source == BreedingFarmVisualIdentitySource.Upload
                    ? Url.RouteUrl("GetBreedingFarmVisualIdentityContent")
                    : null);

    private static bool IsSupportedImageMetadata(string fileName, string contentType)
    {
        if (!PrivateObjectStorageFileValidation.TryValidateMetadata(fileName, contentType, out _))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return contentType.Trim().Equals("image/png", StringComparison.OrdinalIgnoreCase)
                ? extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                : contentType.Trim().Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                    (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult AuthenticationRequired() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Authentication is required.",
        type: "https://httpstatuses.com/401");

    private IActionResult BreedingFarmNotSelected() => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "A breeding farm must be selected to manage its visual identity.",
        type: "https://httpstatuses.com/409");

    private IActionResult BreedingFarmNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The breeding farm was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult InvalidFile(string message)
    {
        ModelState.Clear();
        ModelState.AddModelError("file", message);
        return ValidationProblem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Visual identity image is invalid.",
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }
}

public sealed class UploadBreedingFarmVisualIdentityRequest
{
    public IFormFile? File { get; set; }
}

public sealed record BreedingFarmVisualIdentityResponse(
    Guid BreedingFarmId,
    BreedingFarmVisualIdentityItemResponse? Identity);

public sealed record BreedingFarmVisualIdentityItemResponse(
    string Source,
    string? FileName,
    string? ContentType,
    long? Length,
    DateTimeOffset UpdatedAtUtc,
    string? ContentUrl);
