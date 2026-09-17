using System.Security.Claims;
using CriatorioVirtual.Application.Identity;
using CriatorioVirtual.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/avatar")]
public sealed class UserAvatarController(
    ICommandExecutor commandExecutor,
    IQueryExecutor queryExecutor) : ControllerBase
{
    [HttpPut(Name = "UploadCurrentUserAvatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UserAvatarUploadLimits.MaxRequestLength)]
    [RequestFormLimits(MultipartBodyLengthLimit = UserAvatarUploadLimits.MaxRequestLength)]
    [ProducesResponseType(typeof(UserAvatarResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadAsync(
        [FromForm] UploadUserAvatarRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var file = request?.File;
        if (file is null)
        {
            return InvalidFile("An avatar image is required.");
        }

        if (file.Length is <= 0 or > UserAvatarUploadLimits.MaxFileLength)
        {
            return InvalidFile($"The avatar image must be between 1 and {UserAvatarUploadLimits.MaxFileLength} bytes.");
        }

        await using var content = file.OpenReadStream();
        UploadUserAvatarResult result;
        try
        {
            result = await commandExecutor.Execute<UploadUserAvatarCommand, UploadUserAvatarResult>(
                new UploadUserAvatarCommand(
                    userId,
                    file.FileName,
                    file.ContentType,
                    file.Length,
                    content),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UpdateConflict();
        }

        return result.Status switch
        {
            UploadUserAvatarStatus.Updated => Ok(new UserAvatarResponse(
                Url.RouteUrl("GetCurrentUserAvatar") ?? "/api/me/avatar")),
            UploadUserAvatarStatus.UserNotFound => AuthenticationRequired(),
            UploadUserAvatarStatus.InvalidData => InvalidFile("Only valid PNG or JPEG avatar images are accepted."),
            UploadUserAvatarStatus.StorageUnavailable => StorageUnavailable(),
            _ => throw new InvalidOperationException("The user avatar upload result is not supported.")
        };
    }

    [HttpGet(Name = "GetCurrentUserAvatar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        var result = await queryExecutor.Execute<GetUserAvatarContentQuery, GetUserAvatarContentResult>(
            new GetUserAvatarContentQuery(userId),
            cancellationToken);
        switch (result.Status)
        {
            case GetUserAvatarContentStatus.Success:
                Response.Headers.CacheControl = "private, no-store";
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                return File(result.Content!, result.ContentType!);
            case GetUserAvatarContentStatus.UserNotFound:
                return AuthenticationRequired();
            case GetUserAvatarContentStatus.AvatarNotFound:
                return AvatarNotFound();
            case GetUserAvatarContentStatus.StorageUnavailable:
                return StorageUnavailable();
            default:
                throw new InvalidOperationException("The user avatar content result is not supported.");
        }
    }

    [HttpDelete(Name = "RemoveCurrentUserAvatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RemoveAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return AuthenticationRequired();
        }

        RemoveUserAvatarResult result;
        try
        {
            result = await commandExecutor.Execute<RemoveUserAvatarCommand, RemoveUserAvatarResult>(
                new RemoveUserAvatarCommand(userId),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UpdateConflict();
        }
        return result.Status switch
        {
            RemoveUserAvatarStatus.Removed => NoContent(),
            RemoveUserAvatarStatus.UserNotFound => AuthenticationRequired(),
            _ => throw new InvalidOperationException("The user avatar removal result is not supported.")
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult AuthenticationRequired() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Authentication is required.",
        type: "https://httpstatuses.com/401");

    private IActionResult AvatarNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The user avatar was not found.",
        type: "https://httpstatuses.com/404");

    private IActionResult StorageUnavailable() => Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Private avatar storage is temporarily unavailable.",
        type: "https://httpstatuses.com/503");

    private IActionResult UpdateConflict() => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "The avatar changed in another request. Retry with the latest state.",
        type: "https://httpstatuses.com/409");

    private IActionResult InvalidFile(string message)
    {
        ModelState.Clear();
        ModelState.AddModelError("file", message);
        return ValidationProblem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Avatar image is invalid.",
            type: "https://httpstatuses.com/400",
            modelStateDictionary: ModelState);
    }
}

public sealed record UploadUserAvatarRequest(IFormFile? File);

public sealed record UserAvatarResponse(string AvatarUrl);
