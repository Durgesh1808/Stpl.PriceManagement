using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Stpl.PriceManagement.Areas.Core.Controllers
{
    /// <summary>
    /// Keeps the old addresses working: bookmarks, links in old emails and old
    /// notifications that point at /Products/Revision?code=... and so on are
    /// sent to the same screen at its new address, query string intact.
    /// </summary>
    [Area("Core")]
    public sealed class LegacyRedirectController : Controller
    {
        [HttpGet("Products")]
        public IActionResult Products() { return Moved("/IntlGit/Products"); }

        [HttpGet("Products/Revision")]
        public IActionResult Revision() { return Moved("/IntlGit/Revision"); }

        [HttpGet("Products/Review")]
        public IActionResult Review() { return Moved("/IntlGit/Review"); }

        [HttpGet("Products/Version")]
        public IActionResult Version() { return Moved("/IntlGit/Version"); }

        [HttpGet("Products/FlightSheet")]
        public IActionResult FlightSheet() { return Moved("/IntlGit/FlightSheet"); }

        [HttpGet("ChangeSets")]
        public IActionResult ChangeSets() { return Moved("/IntlGit/ChangeSets"); }

        [HttpGet("ChangeSets/Detail")]
        public IActionResult ChangeSetDetail() { return Moved("/IntlGit/ChangeSets/Detail"); }

        [HttpGet("FareRequests")]
        public IActionResult FareRequests() { return Moved("/IntlGit/FareRequests"); }

        [HttpGet("Notifications")]
        public IActionResult Notifications() { return Moved("/Core/Notifications"); }

        [AllowAnonymous]
        [HttpGet("Account/Login")]
        public IActionResult Login() { return Moved("/Core/Account/Login"); }

        [HttpGet("Account/Password")]
        public IActionResult Password() { return Moved("/Core/Account/Password"); }

        private IActionResult Moved(string path)
        {
            return Redirect(path + Request.QueryString.Value);
        }
    }
}
