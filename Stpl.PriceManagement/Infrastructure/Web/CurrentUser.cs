using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Stpl.PriceManagement.Areas.Core.Domain;

namespace Stpl.PriceManagement.Infrastructure.Web
{
    /// <summary>
    /// Who is using the app and what they are allowed to do.
    /// </summary>
    /// <remarks>
    /// Reads the signed-in person's claims. Before round G it read a plaintext
    /// cookie anybody could set, and a visitor with no cookie at all was given
    /// the product executive's role - the most privileged one - so the failure
    /// was open rather than closed.
    ///
    /// Both of those are gone. An unauthenticated request cannot reach a page
    /// at all (the authorization policy requires a signed-in user everywhere
    /// by default), and if one somehow does, this answers with the role that
    /// can do least rather than the role that can do most.
    ///
    /// Everything downstream already asks this class rather than deciding for
    /// itself, which is why replacing what is underneath it changed almost
    /// nothing else.
    /// </remarks>
    public sealed class CurrentUser
    {
        private readonly IHttpContextAccessor _accessor;

        public CurrentUser(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        private ClaimsPrincipal Principal
        {
            get
            {
                var context = _accessor.HttpContext;
                return context == null ? null : context.User;
            }
        }

        public bool IsSignedIn
        {
            get
            {
                var principal = Principal;
                return principal != null
                    && principal.Identity != null
                    && principal.Identity.IsAuthenticated;
            }
        }

        /// <summary>
        /// The signed-in person's role.
        /// </summary>
        /// <remarks>
        /// Tech support when there is no valid role claim. That is the least
        /// privileged of the three - it cannot edit a price, a fare or the
        /// cost build - so an identity this build cannot make sense of gets
        /// the smallest possible answer instead of the largest.
        /// </remarks>
        public UserRole Role
        {
            get
            {
                var principal = Principal;
                if (principal == null)
                {
                    return UserRole.TechSupport;
                }

                var claim = principal.FindFirst(ClaimTypes.Role);

                UserRole role;
                return claim != null && UserRoles.TryParse(claim.Value, out role)
                    ? role
                    : UserRole.TechSupport;
            }
        }

        /// <summary>
        /// The person's own name, for the audit trail and the sidebar.
        /// </summary>
        /// <remarks>
        /// This is what makes "who did this" answerable. Until round G every
        /// change by any of the four tech support people was recorded against
        /// the literal string "Tech support", because there was no person to
        /// attribute it to.
        /// </remarks>
        public string DisplayName
        {
            get
            {
                var principal = Principal;
                if (principal == null)
                {
                    return null;
                }

                var claim = principal.FindFirst(ClaimTypes.Name);
                return claim == null ? null : claim.Value;
            }
        }

        /// <summary>
        /// Which account this is, for the things addressed to a person rather
        /// than read off the page.
        /// </summary>
        /// <remarks>
        /// Zero when nobody is signed in, and every call that takes it treats
        /// zero as "no notifications" rather than "all of them" - the same
        /// closed-by-default rule as Role above.
        /// </remarks>
        public int UserId
        {
            get
            {
                var principal = Principal;
                if (principal == null)
                {
                    return 0;
                }

                var claim = principal.FindFirst(ClaimTypes.NameIdentifier);

                int id;
                return claim != null && int.TryParse(claim.Value, out id) ? id : 0;
            }
        }

        public string Email
        {
            get
            {
                var principal = Principal;
                if (principal == null)
                {
                    return null;
                }

                var claim = principal.FindFirst(ClaimTypes.Email);
                return claim == null ? null : claim.Value;
            }
        }

        /// <summary>What this role is called on screen.</summary>
        public string RoleName
        {
            get { return UserRoles.DisplayName(Role); }
        }

        public bool IsProductExecutive
        {
            get { return Role == UserRole.ProductExecutive; }
        }

        public bool IsAirTicketing
        {
            get { return Role == UserRole.AirTicketingExecutive; }
        }

        public bool IsTechSupport
        {
            get { return Role == UserRole.TechSupport; }
        }

        /// <summary>The screen this role starts on.</summary>
        /// <remarks>
        /// International GIT is the only pricing area today. When a second
        /// area is added, this is where a person's landing screen is decided.
        /// </remarks>
        public string HomePage
        {
            get { return IsTechSupport ? "/IntlGit/ChangeSets" : "/IntlGit/Products"; }
        }
    }
}
