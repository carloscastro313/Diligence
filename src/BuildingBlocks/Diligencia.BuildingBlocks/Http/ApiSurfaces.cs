using Microsoft.AspNetCore.Routing;

namespace Diligencia.BuildingBlocks.Http;

public sealed record ApiSurfaces(RouteGroupBuilder Backoffice, RouteGroupBuilder Portal);
