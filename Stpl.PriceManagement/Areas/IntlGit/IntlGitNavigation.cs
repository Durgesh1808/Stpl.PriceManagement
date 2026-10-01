using System.Collections.Generic;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit
{
    /// <summary>
    /// The International GIT section of the navigation rail.
    /// </summary>
    /// <remarks>
    /// Each role gets only the screens it works in. No Notifications item: the
    /// bell in the top bar is the way in, on every screen and for every role.
    /// </remarks>
    public static class IntlGitNavigation
    {
        public const string Title = "International GIT";

        /// <summary>Every screen that belongs under "Products" (a tour and its pages).</summary>
        private static readonly string[] ProductScreens =
        {
            "/IntlGit/Products", "/IntlGit/Revision", "/IntlGit/Review", "/IntlGit/Version", "/IntlGit/FlightSheet"
        };

        public static NavSection Section(CurrentUser user)
        {
            List<NavItem> items;

            if (user.IsTechSupport)
            {
                items = new List<NavItem>
                {
                    new NavItem("Website updates", "layers", "/IntlGit/ChangeSets")
                };
            }
            else if (user.IsAirTicketing)
            {
                items = new List<NavItem>
                {
                    new NavItem("Products", "grid", "/IntlGit/Products", ProductScreens),
                    new NavItem("Fare requests", "ticket", "/IntlGit/FareRequests")
                };
            }
            else
            {
                items = new List<NavItem>
                {
                    new NavItem("Products", "grid", "/IntlGit/Products", ProductScreens),
                    new NavItem("Change sets", "layers", "/IntlGit/ChangeSets")
                };
            }

            return new NavSection(Title, items);
        }
    }
}
