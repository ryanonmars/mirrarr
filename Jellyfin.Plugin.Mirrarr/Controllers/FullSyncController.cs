using Jellyfin.Plugin.Mirrarr.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Mirrarr.Controllers;

[ApiController]
[Route("Mirrarr/Sync")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class FullSyncController : ControllerBase
{
    private readonly IFullSyncCoordinator _coordinator;

    public FullSyncController(IFullSyncCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(FullSyncStatus), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(FullSyncApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FullSyncApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(FullSyncApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Start([FromBody] FullSyncRequest request)
    {
        var result = _coordinator.Start(request.SourceUserId);
        return result.Outcome switch
        {
            FullSyncStartOutcome.Accepted => Accepted(result.Status),
            FullSyncStartOutcome.InvalidRequest or
            FullSyncStartOutcome.InvalidConfiguration or
            FullSyncStartOutcome.InvalidSource => BadRequest(new FullSyncApiResponse(result.Message, result.Status)),
            FullSyncStartOutcome.AlreadyActive => Conflict(new FullSyncApiResponse(result.Message, result.Status)),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new FullSyncApiResponse(result.Message, result.Status)),
        };
    }

    [HttpGet("Status")]
    [ProducesResponseType(typeof(FullSyncStatus), StatusCodes.Status200OK)]
    public IActionResult GetStatus() => Ok(_coordinator.Status);
}

public sealed record FullSyncApiResponse(string Message, FullSyncStatus Status);
