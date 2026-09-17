using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/breeding-farm-cover-templates")]
public sealed class BreedingFarmCoverTemplateController(IQueryExecutor queryExecutor) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet(Name = "GetBreedingFarmCoverTemplates")]
    [ProducesResponseType(typeof(IReadOnlyList<BreedingFarmCoverTemplateCatalogItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var items = await queryExecutor.Execute<
            GetBreedingFarmCoverTemplatesQuery,
            IReadOnlyList<BreedingFarmCoverTemplateCatalogItem>>(
            new GetBreedingFarmCoverTemplatesQuery(),
            cancellationToken);
        return Ok(items);
    }

    [AllowAnonymous]
    [HttpGet("{templateId}/{version}/preview", Name = "GetBreedingFarmCoverTemplatePreview")]
    [Produces("image/jpeg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetPreviewAsync(
        string templateId,
        string version,
        CancellationToken cancellationToken)
    {
        var result = await queryExecutor.Execute<
            GetBreedingFarmCoverTemplatePreviewQuery,
            GetBreedingFarmCoverTemplatePreviewResult>(
            new GetBreedingFarmCoverTemplatePreviewQuery(templateId, version),
            cancellationToken);
        if (result.Status == GetBreedingFarmCoverTemplatePreviewStatus.Available)
        {
            Response.Headers.CacheControl = "public, max-age=86400, immutable";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(result.PreviewImage!, result.ContentType!);
        }

        return result.Status switch
        {
            GetBreedingFarmCoverTemplatePreviewStatus.Inactive or
                GetBreedingFarmCoverTemplatePreviewStatus.VersionUnavailable => TemplateUnavailable(),
            _ => TemplateNotFound()
        };
    }

    [Authorize]
    [HttpPost("{modelId}/preview", Name = "PreviewBreedingFarmCoverTemplate")]
    [Consumes("application/json")]
    [Produces("image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PreviewAsync(
        string modelId,
        [FromBody] BreedingFarmCoverPreviewRequest? request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return AuthenticationRequired();
        }

        if (request is null)
        {
            return InvalidConfiguration("A template selection is required.");
        }

        var result = await queryExecutor.Execute<
            PreviewBreedingFarmCoverTemplateQuery,
            PreviewBreedingFarmCoverTemplateResult>(
            new PreviewBreedingFarmCoverTemplateQuery(userId, modelId, request.Version, request.Config),
            cancellationToken);
        if (result.Status == PreviewBreedingFarmCoverTemplateStatus.PreviewReady)
        {
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(result.PngContent!, "image/png");
        }

        return result.Status switch
        {
            PreviewBreedingFarmCoverTemplateStatus.UserNotFound => AuthenticationRequired(),
            PreviewBreedingFarmCoverTemplateStatus.BreedingFarmNotFound => BreedingFarmNotFound(),
            PreviewBreedingFarmCoverTemplateStatus.TemplateInactive or
                PreviewBreedingFarmCoverTemplateStatus.VersionUnavailable => TemplateUnavailable(),
            PreviewBreedingFarmCoverTemplateStatus.InvalidConfiguration =>
                InvalidConfiguration("Only options declared by the selected template are accepted."),
            PreviewBreedingFarmCoverTemplateStatus.RenderingUnavailable => CoverRenderingUnavailable(),
            _ => TemplateNotFound()
        };
    }

    private IActionResult AuthenticationRequired() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Authentication is required.",
        type: "https://httpstatuses.com/401");

    private IActionResult BreedingFarmNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The breeding farm was not found.",
        type: "https://httpstatuses.com/404");

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

    private IActionResult InvalidConfiguration(string message)
    {
        ModelState.Clear();
        ModelState.AddModelError("config", message);
        return ValidationProblem(statusCode: StatusCodes.Status400BadRequest, title: "The cover configuration is invalid.");
    }
}

public sealed class BreedingFarmCoverPreviewRequest
{
    [Required]
    [StringLength(32)]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("config")]
    public JsonElement Config { get; set; }
}
