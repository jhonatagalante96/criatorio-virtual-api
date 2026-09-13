using System.Security.Claims;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reports;
using CriatorioVirtual.Domain.Birds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/reports/birds")]
[Authorize]
public sealed class BirdsReportController(
    IQueryExecutor queryExecutor,
    IBirdsReportRenderer renderer) : ControllerBase
{
    [HttpGet("pdf", Name = "GenerateBirdsReport")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetPdfAsync(
        [FromQuery] string? status,
        [FromQuery] string? sex,
        [FromQuery] Guid? speciesId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        var errors = ValidateRequest(status, sex, speciesId, out var parsedStatus, out var parsedSex);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors);
        }

        var result = await queryExecutor.Execute<GenerateBirdsReportQuery, GenerateBirdsReportResult>(
            new GenerateBirdsReportQuery(userId, parsedStatus, parsedSex, speciesId),
            cancellationToken);

        if (result.Status == GenerateBirdsReportStatus.Success)
        {
            var rendered = await renderer.RenderAsync(result.Report!, cancellationToken);
            return File(rendered.Content, rendered.ContentType, rendered.FileName);
        }

        return result.Status switch
        {
            GenerateBirdsReportStatus.UserNotFound => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401"),
            GenerateBirdsReportStatus.BreedingFarmNotSelected => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A breeding farm must be selected before generating the birds report.",
                type: "https://httpstatuses.com/409"),
            GenerateBirdsReportStatus.BreedingFarmNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The selected breeding farm was not found.",
                type: "https://httpstatuses.com/404"),
            _ => throw new InvalidOperationException("The birds report result is not supported.")
        };
    }

    private IActionResult ValidationProblemResult(IReadOnlyDictionary<string, string[]> errors)
    {
        ModelState.Clear();
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }

        return ValidationProblem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Birds report parameters are invalid.",
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }

    private static Dictionary<string, string[]> ValidateRequest(
        string? status,
        string? sex,
        Guid? speciesId,
        out BirdStatus? parsedStatus,
        out BirdSex? parsedSex)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        parsedStatus = null;
        parsedSex = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseEnumName(status, out BirdStatus value))
            {
                errors[nameof(status)] = ["The bird status is invalid."];
            }
            else
            {
                parsedStatus = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(sex))
        {
            if (!TryParseEnumName(sex, out BirdSex value))
            {
                errors[nameof(sex)] = ["The bird sex is invalid."];
            }
            else
            {
                parsedSex = value;
            }
        }

        if (speciesId == Guid.Empty)
        {
            errors[nameof(speciesId)] = ["The species identifier cannot be empty."];
        }

        return errors;
    }

    private static bool TryParseEnumName<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        var normalized = value.Trim();
        return Enum.TryParse(normalized, ignoreCase: true, out parsed) &&
            Enum.IsDefined(parsed) &&
            string.Equals(parsed.ToString(), normalized, StringComparison.OrdinalIgnoreCase);
    }
}
