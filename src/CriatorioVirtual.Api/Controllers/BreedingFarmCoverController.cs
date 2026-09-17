using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/breeding-farms/{breedingFarmId:guid}/cover")]
public sealed class BreedingFarmCoverController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "GetBreedingFarmCover")]
    [ProducesResponseType(typeof(BreedingFarmCoverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid breedingFarmId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<GetBreedingFarmCoverQuery, GetBreedingFarmCoverResult>(
            new GetBreedingFarmCoverQuery(userId, breedingFarmId),
            cancellationToken);
        return result.Status switch
        {
            BreedingFarmCoverAccessStatus.Success => Ok(ToResponse(result)),
            BreedingFarmCoverAccessStatus.UserNotFound => AuthenticationRequired(),
            _ => BreedingFarmNotFound()
        };
    }

    [HttpGet("content", Name = "GetBreedingFarmCoverContent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetContentAsync(Guid breedingFarmId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<GetBreedingFarmCoverContentQuery, GetBreedingFarmCoverContentResult>(
            new GetBreedingFarmCoverContentQuery(userId, breedingFarmId),
            cancellationToken);
        switch (result.Status)
        {
            case GetBreedingFarmCoverContentStatus.Success:
                Response.Headers.CacheControl = "private, no-store";
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                return File(result.Content!, result.ContentType!, enableRangeProcessing: true);
            case GetBreedingFarmCoverContentStatus.UserNotFound:
                return AuthenticationRequired();
            case GetBreedingFarmCoverContentStatus.BreedingFarmNotFound:
            case GetBreedingFarmCoverContentStatus.CoverNotFound:
                return BreedingFarmNotFound();
            default:
                return CoverStorageUnavailable();
        }
    }

    [HttpPut("upload", Name = "UploadBreedingFarmCover")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(BreedingFarmCoverUploadLimits.MaxRequestLength)]
    [RequestFormLimits(MultipartBodyLengthLimit = BreedingFarmCoverUploadLimits.MaxRequestLength)]
    [ProducesResponseType(typeof(BreedingFarmCoverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadAsync(
        Guid breedingFarmId,
        [FromForm] UploadBreedingFarmCoverRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var file = request?.File;
        if (file is null || file.Length <= 0 || file.Length > BreedingFarmCoverUploadLimits.MaxFileLength ||
            !PrivateObjectStorageFileValidation.TryValidateMetadata(file.FileName, file.ContentType, out _) ||
            file.ContentType.Trim().ToLowerInvariant() is not ("image/png" or "image/jpeg" or "image/webp"))
        {
            return InvalidCover("Only PNG, JPEG, or WebP images up to 8 MiB are accepted.");
        }

        await using var content = file.OpenReadStream();
        var result = await commandExecutor.Execute<UploadBreedingFarmCoverCommand, UploadBreedingFarmCoverResult>(
            new UploadBreedingFarmCoverCommand(
                userId,
                breedingFarmId,
                file.FileName,
                file.ContentType,
                file.Length,
                content),
            cancellationToken);
        return result.Status switch
        {
            UploadBreedingFarmCoverStatus.Uploaded => Ok(ToResponse(result)),
            UploadBreedingFarmCoverStatus.UserNotFound => AuthenticationRequired(),
            UploadBreedingFarmCoverStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            UploadBreedingFarmCoverStatus.InvalidData => InvalidCover("The cover image is invalid or has unsupported dimensions."),
            UploadBreedingFarmCoverStatus.RenderingUnavailable => CoverRenderingUnavailable(),
            _ => CoverStorageUnavailable()
        };
    }

    [HttpDelete(Name = "RemoveBreedingFarmCover")]
    [ProducesResponseType(typeof(BreedingFarmCoverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAsync(Guid breedingFarmId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await commandExecutor.Execute<RemoveBreedingFarmCoverCommand, RemoveBreedingFarmCoverResult>(
            new RemoveBreedingFarmCoverCommand(userId, breedingFarmId),
            cancellationToken);
        return result.Status switch
        {
            RemoveBreedingFarmCoverStatus.Removed => Ok(new BreedingFarmCoverResponse(breedingFarmId, null)),
            RemoveBreedingFarmCoverStatus.UserNotFound => AuthenticationRequired(),
            _ => BreedingFarmNotFound()
        };
    }

    [HttpPut("template", Name = "ApplyBreedingFarmCoverTemplate")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BreedingFarmCoverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ApplyTemplateAsync(
        Guid breedingFarmId,
        [FromBody] BreedingFarmCoverTemplateRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        if (request is null)
        {
            return InvalidConfiguration("A template selection is required.");
        }

        var result = await commandExecutor.Execute<
            ApplyBreedingFarmCoverTemplateCommand,
            ApplyBreedingFarmCoverTemplateResult>(
            new ApplyBreedingFarmCoverTemplateCommand(
                userId,
                breedingFarmId,
                request.ModelId,
                request.Version,
                request.Config),
            cancellationToken);
        return result.Status switch
        {
            ApplyBreedingFarmCoverTemplateStatus.Applied => Ok(ToResponse(result)),
            ApplyBreedingFarmCoverTemplateStatus.UserNotFound => AuthenticationRequired(),
            ApplyBreedingFarmCoverTemplateStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            ApplyBreedingFarmCoverTemplateStatus.TemplateInactive or
                ApplyBreedingFarmCoverTemplateStatus.VersionUnavailable => TemplateUnavailable(),
            ApplyBreedingFarmCoverTemplateStatus.InvalidConfiguration =>
                InvalidConfiguration("Only options declared by the selected template are accepted."),
            ApplyBreedingFarmCoverTemplateStatus.RenderingUnavailable => CoverRenderingUnavailable(),
            ApplyBreedingFarmCoverTemplateStatus.StorageUnavailable => CoverStorageUnavailable(),
            _ => TemplateNotFound()
        };
    }

    private BreedingFarmCoverResponse ToResponse(GetBreedingFarmCoverResult result) =>
        new(result.BreedingFarmId!.Value, ToResponse(result.Cover));

    private BreedingFarmCoverResponse ToResponse(UploadBreedingFarmCoverResult result) =>
        new(result.BreedingFarmId!.Value, ToResponse(result.Cover));

    private BreedingFarmCoverResponse ToResponse(ApplyBreedingFarmCoverTemplateResult result) =>
        new(result.BreedingFarmId!.Value, ToResponse(result.Cover));

    private BreedingFarmCoverItemResponse? ToResponse(BreedingFarmCoverMetadata? cover) =>
        cover is null
            ? null
            : new(
                cover.Source.ToString(),
                cover.FileName,
                cover.ContentType,
                cover.Length,
                cover.UpdatedAtUtc,
                Url.RouteUrl("GetBreedingFarmCoverContent", new { breedingFarmId = RouteData.Values["breedingFarmId"] })!,
                cover.TemplateModelId,
                cover.TemplateVersion,
                cover.TemplateConfiguration is null
                    ? null
                    : JsonDocument.Parse(cover.TemplateConfiguration).RootElement.Clone());

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult AuthenticationRequired() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Authentication is required.",
        type: "https://httpstatuses.com/401");

    private IActionResult BreedingFarmNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The breeding farm was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult InvalidCover(string message)
    {
        ModelState.Clear();
        ModelState.AddModelError("file", message);
        return ValidationProblem(statusCode: StatusCodes.Status400BadRequest, title: "The cover image is invalid.");
    }

    private IActionResult InvalidConfiguration(string message)
    {
        ModelState.Clear();
        ModelState.AddModelError("config", message);
        return ValidationProblem(statusCode: StatusCodes.Status400BadRequest, title: "The cover configuration is invalid.");
    }

    private IActionResult TemplateNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The cover template was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult TemplateUnavailable() => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "The cover template or version is no longer available.",
        type: "https://httpstatuses.com/409");

    private IActionResult CoverRenderingUnavailable() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Cover image rendering is temporarily unavailable.",
        type: "https://httpstatuses.com/503");

    private IActionResult CoverStorageUnavailable() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Private cover storage is temporarily unavailable.",
        type: "https://httpstatuses.com/503");
}

public sealed class UploadBreedingFarmCoverRequest
{
    public IFormFile? File { get; set; }
}

public sealed class BreedingFarmCoverTemplateRequest
{
    [Required]
    [StringLength(100)]
    [JsonPropertyName("modelId")]
    public string ModelId { get; set; } = string.Empty;

    [Required]
    [StringLength(32)]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("config")]
    public JsonElement Config { get; set; }
}

public sealed record BreedingFarmCoverResponse(Guid BreedingFarmId, BreedingFarmCoverItemResponse? Cover);

public sealed record BreedingFarmCoverItemResponse(
    string Source,
    string FileName,
    string ContentType,
    long Length,
    DateTimeOffset UpdatedAtUtc,
    string ContentUrl,
    string? ModelId,
    string? Version,
    JsonElement? Configuration);
