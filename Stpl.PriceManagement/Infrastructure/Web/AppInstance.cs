using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Stpl.PriceManagement.Infrastructure.Web
{
    /// <summary>
    /// Ties every sign-in to the running copy of the application, so that
    /// stopping and starting the application signs everybody out.
    /// </summary>
    /// <remarks>
    /// The auth cookie on its own survives a restart: it is a browser session
    /// cookie (the browser keeps it until the browser itself fully closes -
    /// and Chrome/Edge often never do, or restore it on start-up), and the
    /// Data Protection keys that encrypt it are saved to disk, so a new run
    /// can still read it.
    ///
    /// Each run therefore gets a fresh id, stamped into the cookie at sign-in
    /// and checked on every request. A cookie from an earlier run carries a
    /// different id and is thrown away, which sends the person to sign-in.
    ///
    /// Note: one id per process. If the site is ever run on more than one
    /// server behind a load balancer, replace this with a shared value.
    /// </remarks>
    public static class AppInstance
    {
        public const string ClaimType = "st:instance";

        /// <summary>Generated once when the application starts.</summary>
        public static readonly string Id = Guid.NewGuid().ToString("N");

        /// <summary>Cookie hook: rejects a sign-in made by an earlier run.</summary>
        public static async Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            var stamped = context.Principal?.Claims
                .FirstOrDefault(c => c.Type == ClaimType)?.Value;

            if (!string.Equals(stamped, Id, StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }
    }
}
