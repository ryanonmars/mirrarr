using System.Reflection;
using Jellyfin.Plugin.JellySync.Controllers;
using Jellyfin.Plugin.JellySync.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellySync.Tests;

public class FullSyncControllerTests
{
    [Fact]
    public void Controller_has_admin_route_and_authorization_metadata()
    {
        var type = typeof(FullSyncController);

        Assert.Equal("JellySync/Sync", type.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy == Policies.RequiresElevation);
        Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>());
    }

    [Theory]
    [InlineData(FullSyncStartOutcome.Accepted, typeof(AcceptedResult))]
    [InlineData(FullSyncStartOutcome.InvalidRequest, typeof(BadRequestObjectResult))]
    [InlineData(FullSyncStartOutcome.InvalidConfiguration, typeof(BadRequestObjectResult))]
    [InlineData(FullSyncStartOutcome.InvalidSource, typeof(BadRequestObjectResult))]
    [InlineData(FullSyncStartOutcome.AlreadyActive, typeof(ConflictObjectResult))]
    [InlineData(FullSyncStartOutcome.WorkerUnavailable, typeof(ObjectResult))]
    public void Post_maps_start_outcomes_to_expected_http_results(FullSyncStartOutcome outcome, Type resultType)
    {
        var coordinator = new StubFullSyncCoordinator(outcome);
        var controller = new FullSyncController(coordinator);

        var result = controller.Start(new FullSyncRequest(Guid.NewGuid()));

        Assert.IsType(resultType, result);
        if (outcome == FullSyncStartOutcome.WorkerUnavailable)
        {
            Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }

    [Fact]
    public void Get_returns_latest_status()
    {
        var coordinator = new StubFullSyncCoordinator(FullSyncStartOutcome.Accepted);
        var controller = new FullSyncController(coordinator);

        var result = Assert.IsType<OkObjectResult>(controller.GetStatus());

        Assert.Same(coordinator.Status, result.Value);
    }

    private sealed class StubFullSyncCoordinator(FullSyncStartOutcome outcome) : IFullSyncCoordinator
    {
        public FullSyncStatus Status { get; } = FullSyncStatus.Idle;

        public FullSyncStartResult Start(Guid sourceUserId) => new(outcome, outcome.ToString(), Status);
    }
}
