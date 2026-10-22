namespace Cip.Server.Net40
{
    internal static class LegacyRoutes
    {
        public static void Register(System.Web.Routing.RouteCollection routes)
        {
            Web.LegacyRoutes.Register(routes);
        }
    }
}
