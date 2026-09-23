using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Mise.ApiService.Realtime;

/// <summary>
/// FR-10/NFR-04/US-03 — server-to-client only; no client-invokable methods this phase (a live
/// floor plan is a read, per the mockups' "Live Floor Plan" screen, not a two-way channel). The
/// "FloorStaff" policy (FloorStaff-or-Manager, the same OR every other floor-plan read uses —
/// see <see cref="Mise.ApiService.Tables.TablesEndpoints"/>'s <c>GetFloorPlanAsync</c>) is
/// enforced at connection time: an unauthenticated or wrong-role client is rejected before the
/// handshake completes, the same 401/403 guarantee every REST endpoint already gives (the
/// charter's own "authorization on every ... SignalR hub method"). See CLAUDE.md's "Real-time
/// propagation" section for why the JWT has to travel as an <c>access_token</c> query parameter
/// here rather than the usual <c>Authorization</c> header.
/// </summary>
[Authorize(Policy = "FloorStaff")]
public sealed class FloorPlanHub : Hub
{
    public const string RoutePattern = "/hubs/floorplan";
}
