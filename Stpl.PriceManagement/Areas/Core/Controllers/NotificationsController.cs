using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Areas.Core.Services;
using Stpl.PriceManagement.Areas.Core.ViewModels;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.Core.Controllers
{
    /// <summary>
    /// Everything that has happened to you - from every pricing area. No role
    /// check: which notifications you have is decided by who addressed them to
    /// you, not by your job.
    /// </summary>
    [Area("Core")]
    public sealed class NotificationsController : Controller
    {
        private readonly NotificationService _notifications;
        private readonly CurrentUser _user;

        public NotificationsController(NotificationService notifications, CurrentUser user)
        {
            _notifications = notifications;
            _user = user;
        }

        // GET /Core/Notifications
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var result = await _notifications.ListAsync(_user.UserId, cancellationToken);

            return View(new NotificationsViewModel
            {
                Items = result.Items ?? new List<NotificationResponse>(),
                Unread = result.Unread
            });
        }

        /// <summary>The number on the bell, for site.js to refresh it without a reload.</summary>
        // GET /Core/Notifications/Count
        [HttpGet]
        public async Task<IActionResult> Count(CancellationToken cancellationToken)
        {
            var unread = _user.IsSignedIn
                ? await _notifications.CountUnreadAsync(_user.UserId, cancellationToken)
                : 0;

            Response.Headers["Cache-Control"] = "no-store";
            return Json(new { unread });
        }

        /// <summary>
        /// Reading one: marks it and goes where it points. A POST rather than a
        /// link, so following a notification is a deliberate act.
        /// </summary>
        // POST /Core/Notifications/Read/123
        [HttpPost]
        public async Task<IActionResult> Read(long id, CancellationToken cancellationToken)
        {
            var link = await _notifications.MarkReadAsync(id, _user.UserId, cancellationToken);

            // Null means there is no such notification for this person; the
            // answer is their own list rather than an error.
            if (string.IsNullOrEmpty(link))
            {
                return RedirectToAction("Index");
            }

            // Local paths only.
            return Url.IsLocalUrl(link) ? Redirect(link) : (IActionResult)RedirectToAction("Index");
        }

        // POST /Core/Notifications/ReadAll
        [HttpPost]
        public async Task<IActionResult> ReadAll(CancellationToken cancellationToken)
        {
            await _notifications.MarkAllReadAsync(_user.UserId, cancellationToken);
            return RedirectToAction("Index");
        }
    }
}
