using System;
using System.Web;
using System.Web.Security;

namespace Cip.Server.Net40.Security
{
    internal static class LegacyAuthentication
    {
        private const string CookieName = "cip.admin.net40";
        private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

        public static bool IsAuthenticated(HttpContext context, out string username)
        {
            username = null;
            HttpCookie cookie = context.Request.Cookies[CookieName];
            if (cookie == null || string.IsNullOrEmpty(cookie.Value))
            {
                return false;
            }

            try
            {
                FormsAuthenticationTicket ticket = FormsAuthentication.Decrypt(cookie.Value);
                if (ticket == null || ticket.Expired || string.IsNullOrWhiteSpace(ticket.Name))
                {
                    return false;
                }

                username = ticket.Name;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void SignIn(HttpContext context, string username)
        {
            DateTime issuedAt = DateTime.Now;
            FormsAuthenticationTicket ticket = new FormsAuthenticationTicket(
                1,
                username,
                issuedAt,
                issuedAt.Add(Lifetime),
                false,
                "Administrator",
                "/");

            HttpCookie cookie = new HttpCookie(CookieName, FormsAuthentication.Encrypt(ticket));
            cookie.HttpOnly = true;
            cookie.Secure = context.Request.IsSecureConnection;
            cookie.Path = "/";
            context.Response.Cookies.Add(cookie);
        }

        public static void SignOut(HttpContext context)
        {
            HttpCookie cookie = new HttpCookie(CookieName, string.Empty);
            cookie.HttpOnly = true;
            cookie.Secure = context.Request.IsSecureConnection;
            cookie.Path = "/";
            cookie.Expires = DateTime.UtcNow.AddYears(-1);
            context.Response.Cookies.Add(cookie);
        }
    }
}
