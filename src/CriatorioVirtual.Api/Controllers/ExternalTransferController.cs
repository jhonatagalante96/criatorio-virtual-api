using System.Security.Claims;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Route("api/external-transfers")]
[Authorize]
public sealed class ExternalTransferController(ICommandExecutor commandExecutor) : ControllerBase
{
    [HttpPost(Name = "CompleteExternalTransfer")]
    [ProducesResponseType(typeof(ExternalTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteAsync(
        [FromBody] CompleteExternalTransferRequest? request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                type: "https://httpstatuses.com/401");
        }

        if (request is null)
        {
            return ValidationProblemResult(new Dictionary<string, string[]>
            {
                ["request"] = ["The request body is required."]
            });
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ValidationProblemResult(errors, "External transfer data is invalid.");
        }

        try
        {
            var result = await commandExecutor.Execute<CompleteExternalTransferCommand, CompleteExternalTransferResult>(
                new CompleteExternalTransferCommand(
                    userId,
                    request.BirdId,
                    request.RecipientName,
                    request.Notes,
                    request.Confirmed),
                cancellationToken);

            return result.Status switch
            {
                CompleteExternalTransferStatus.Completed => Created(
                    $"/api/external-transfers/{result.Transfer!.ExternalTransferId}",
                    ToResponse(result.Transfer)),
                CompleteExternalTransferStatus.UserNotFound => Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication is required.",
                    type: "https://httpstatuses.com/401"),
                CompleteExternalTransferStatus.BreedingFarmNotSelected => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A breeding farm must be selected before completing an external transfer.",
                    type: "https://httpstatuses.com/409"),
                CompleteExternalTransferStatus.BreedingFarmNotFound or
                CompleteExternalTransferStatus.BirdNotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "The bird was not found.",
                    type: "https://httpstatuses.com/404"),
                CompleteExternalTransferStatus.ConfirmationRequired => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.Confirmed)] = ["Explicit confirmation is required."]
                    },
                    "External transfer confirmation is required."),
                CompleteExternalTransferStatus.BirdNotEligible => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.BirdId)] = ["Only an active bird with a valid six-digit ring number can be transferred externally."]
                    },
                    "The bird is not eligible for an external transfer."),
                CompleteExternalTransferStatus.InternalTransferPending => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The bird has a pending internal transfer.",
                    type: "https://httpstatuses.com/409"),
                CompleteExternalTransferStatus.AlreadyCompleted => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The external transfer has already been completed.",
                    type: "https://httpstatuses.com/409"),
                CompleteExternalTransferStatus.InvalidData => ValidationProblemResult(
                    new Dictionary<string, string[]>
                    {
                        ["request"] = ["The external transfer data is invalid."]
                    },
                    "External transfer data is invalid."),
                _ => throw new InvalidOperationException("The external transfer result is not supported.")
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The bird was changed by another request. Reload it and try again.",
                type: "https://httpstatuses.com/409");
        }
    }

    private IActionResult ValidationProblemResult(
        IReadOnlyDictionary<string, string[]> errors,
        string title = "External transfer data is invalid.")
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
            title: title,
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }

    private static Dictionary<string, string[]> Validate(CompleteExternalTransferRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.BirdId == Guid.Empty)
        {
            errors[nameof(request.BirdId)] = ["The bird identifier is required."];
        }

        if (string.IsNullOrWhiteSpace(request.RecipientName))
        {
            errors[nameof(request.RecipientName)] = ["The external recipient name is required."];
        }
        else if (request.RecipientName.Trim().Length > 200)
        {
            errors[nameof(request.RecipientName)] = ["The external recipient name cannot exceed 200 characters."];
        }

        if (request.Notes?.Trim().Length > 2000)
        {
            errors[nameof(request.Notes)] = ["The external transfer notes cannot exceed 2000 characters."];
        }

        return errors;
    }

    private static ExternalTransferResponse ToResponse(ExternalTransferResult result) =>
        new(
            result.ExternalTransferId,
            result.BirdId,
            result.BreedingFarmId,
            result.RecipientName,
            result.Notes,
            result.BirdStatus,
            result.CompletedAtUtc);
}

public sealed record CompleteExternalTransferRequest(
    Guid BirdId,
    string? RecipientName,
    string? Notes,
    bool Confirmed);

public sealed record ExternalTransferResponse(
    Guid ExternalTransferId,
    Guid BirdId,
    Guid BreedingFarmId,
    string RecipientName,
    string? Notes,
    string Status,
    DateTimeOffset CompletedAtUtc);
