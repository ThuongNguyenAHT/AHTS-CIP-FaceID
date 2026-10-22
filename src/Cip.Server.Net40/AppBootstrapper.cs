using System.Threading;
using System.Web;
using System.Web.Routing;

[assembly: PreApplicationStartMethod(typeof(Cip.Server.Net40.AppBootstrapper), "Start")]

namespace Cip.Server.Net40
{
    public static class AppBootstrapper
    {
        private static int _started;

        public static void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                return;
            }

            LegacyRoutes.Register(RouteTable.Routes);
        }
    }
}
