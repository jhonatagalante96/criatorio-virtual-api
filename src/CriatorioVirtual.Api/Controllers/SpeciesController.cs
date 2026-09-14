using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Species;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/species")]
[Authorize]
public sealed class SpeciesController(IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpGet(Name = "SearchSpecies")]
    [ProducesResponseType(typeof(IReadOnlyCollection<SpeciesResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (search is not null && search.Trim().Length > 100)
        {
            ModelState.AddModelError(nameof(search), "The species search cannot exceed 100 characters.");
            return ValidationProblem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Species search is invalid.",
                type: "https://httpstatuses.com/400");
        }

        var result = await queryExecutor.Execute<SearchSpeciesQuery, IReadOnlyCollection<SpeciesSearchResult>>(
            new SearchSpeciesQuery(search),
            cancellationToken);

        return Ok(result.Select(ToResponse).ToArray());
    }

    private static SpeciesResponse ToResponse(SpeciesSearchResult species) =>
        new(
            species.SpeciesId,
            species.ScientificName,
            species.PopularName,
            species.DefaultImageFileName is null
                ? null
                : $"/species-images/{Uri.EscapeDataString(species.DefaultImageFileName)}");
}

public sealed record SpeciesResponse(
    Guid SpeciesId,
    string ScientificName,
    string PopularName,
    string? DefaultImageUrl = null);
