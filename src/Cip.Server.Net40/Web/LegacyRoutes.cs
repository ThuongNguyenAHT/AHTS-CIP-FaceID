using System.Web.Routing;

namespace Cip.Server.Net40.Web
{
    internal enum LegacyEndpoint
    {
        Root,
        TabletPage,
        AdminLoginPage,
        AdminPage,
        SiteCss,
        TabletJs,
        TrackingFaceJs,
        AdminJs,
        AdminLoginJs,
        TabletConfig,
        TabletCurrent,
        TabletBiometric,
        TabletDecision,
        Login,
        Logout,
        CurrentUser,
        Dashboard,
        SourcePassengers,
        DisplayPassenger,
        SimulateScan,
        HealthLive,
        HealthReady
    }

    internal sealed class LegacyRouteHandler : IRouteHandler
    {
        private readonly LegacyEndpoint _endpoint;

        public LegacyRouteHandler(LegacyEndpoint endpoint)
        {
            _endpoint = endpoint;
        }

        public System.Web.IHttpHandler GetHttpHandler(RequestContext requestContext)
        {
            return new LegacyHttpHandler(_endpoint, requestContext.RouteData);
        }
    }

    internal static class LegacyRoutes
    {
        public static void Register(RouteCollection routes)
        {
            routes.Add("Root", new Route(string.Empty, new LegacyRouteHandler(LegacyEndpoint.Root)));
            routes.Add("TabletPage", new Route("tablet", new LegacyRouteHandler(LegacyEndpoint.TabletPage)));
            routes.Add("AdminLoginPage", new Route("admin/login", new LegacyRouteHandler(LegacyEndpoint.AdminLoginPage)));
            routes.Add("AdminPage", new Route("admin", new LegacyRouteHandler(LegacyEndpoint.AdminPage)));

            routes.Add("SiteCss", new Route("css/site.css", new LegacyRouteHandler(LegacyEndpoint.SiteCss)));
            routes.Add("TabletJs", new Route("js/tablet.js", new LegacyRouteHandler(LegacyEndpoint.TabletJs)));
            routes.Add("TrackingFaceJs", new Route("js/tracking-face.min.js", new LegacyRouteHandler(LegacyEndpoint.TrackingFaceJs)));
            routes.Add("AdminJs", new Route("js/admin.js", new LegacyRouteHandler(LegacyEndpoint.AdminJs)));
            routes.Add("AdminLoginJs", new Route("js/admin-login.js", new LegacyRouteHandler(LegacyEndpoint.AdminLoginJs)));

            routes.Add("TabletConfig", new Route("api/tablet/config", new LegacyRouteHandler(LegacyEndpoint.TabletConfig)));
            routes.Add("TabletCurrent", new Route("api/tablet/current", new LegacyRouteHandler(LegacyEndpoint.TabletCurrent)));
            routes.Add("TabletBiometric", new Route(
                "api/tablet/requests/{id}/biometric",
                new LegacyRouteHandler(LegacyEndpoint.TabletBiometric)));
            routes.Add("TabletDecision", new Route(
                "api/tablet/requests/{id}/decision",
                new LegacyRouteHandler(LegacyEndpoint.TabletDecision)));
            routes.Add("Login", new Route("api/auth/login", new LegacyRouteHandler(LegacyEndpoint.Login)));
            routes.Add("Logout", new Route("api/auth/logout", new LegacyRouteHandler(LegacyEndpoint.Logout)));
            routes.Add("CurrentUser", new Route("api/auth/me", new LegacyRouteHandler(LegacyEndpoint.CurrentUser)));
            routes.Add("Dashboard", new Route("api/admin/dashboard", new LegacyRouteHandler(LegacyEndpoint.Dashboard)));
            routes.Add("SourcePassengers", new Route(
                "api/admin/source-passengers",
                new LegacyRouteHandler(LegacyEndpoint.SourcePassengers)));
            routes.Add("DisplayPassenger", new Route(
                "api/admin/display",
                new LegacyRouteHandler(LegacyEndpoint.DisplayPassenger)));
            routes.Add("SimulateScan", new Route(
                "api/admin/simulate-scan",
                new LegacyRouteHandler(LegacyEndpoint.SimulateScan)));
            routes.Add("HealthLive", new Route("health/live", new LegacyRouteHandler(LegacyEndpoint.HealthLive)));
            routes.Add("HealthReady", new Route("health/ready", new LegacyRouteHandler(LegacyEndpoint.HealthReady)));
        }
    }
}
