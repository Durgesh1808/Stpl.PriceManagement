using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Stpl.PriceManagement.Areas.Core.ViewModels;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.Core.Controllers
{
    /// <summary>The root, and the page somebody lands on when something has gone wrong.</summary>
    [Area("Core")]
    public sealed class HomeController : Controller
    {
        private readonly CurrentUser _user;
        private readonly ILogger<HomeController> _logger;

        public HomeController(CurrentUser user, ILogger<HomeController> logger)
        {
            _user = user;
            _logger = logger;
        }

        /// <summary>The root has no screen of its own - each role starts elsewhere.</summary>
        // GET /
        [HttpGet]
        public IActionResult Index()
        {
            return Redirect(_user.HomePage);
        }

        /// <summary>
        /// Where failures land: an unhandled exception (outside Development), or
        /// a status code with no body (404, 403).
        /// </summary>
        /// <remarks>
        /// It never shows the exception - type names, stack traces, connection
        /// strings and SQL are internal detail. The real exception is logged
        /// with the request id shown on screen, so the two can be matched. It
        /// touches no database, deliberately: the commonest reason to be here
        /// is that the database is not answering.
        /// </remarks>
        // GET /Core/Home/Error?code=404
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error(int? code)
        {
            var model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            };

            var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            model.ReturnPath = SafeReturnPath(feature?.Path);

            if (feature?.Error != null)
            {
                Describe(model, feature.Error);

                _logger.LogError(
                    feature.Error,
                    "Unhandled error shown to a user. RequestId {RequestId}, path {Path}",
                    model.RequestId,
                    feature.Path);

                return View(model);
            }

            DescribeStatusCode(model, code);
            return View(model);
        }

        /// <summary>Turns an exception into something worth reading, without naming it.</summary>
        private static void Describe(ErrorViewModel model, Exception error)
        {
            var inner = error;
            while (inner is AggregateException aggregate && aggregate.InnerException != null)
            {
                inner = aggregate.InnerException;
            }

            // The database not answering (or answering with a fault) is worth
            // waiting out and trying again.
            if (inner is SqlException
                || inner is TimeoutException
                || inner is OperationCanceledException)
            {
                model.IsServiceDown = true;
                model.Title = "This is taking longer than it should";
                model.Explanation =
                    "The pricing database is not answering at the moment, so this page "
                    + "could not be loaded. Nothing you have saved has been lost. "
                    + "Wait a moment and try again.";
                return;
            }

            model.IsServiceDown = false;
            model.Title = "Something went wrong";
            model.Explanation =
                "This page could not be shown. The problem has been recorded. "
                + "If it keeps happening, quote the reference below when you report it.";
        }

        /// <summary>A status code with no exception behind it.</summary>
        private static void DescribeStatusCode(ErrorViewModel model, int? code)
        {
            switch (code)
            {
                case 404:
                    model.Title = "That page does not exist";
                    model.Explanation =
                        "The address may have been mistyped, or the thing it pointed at "
                        + "may have been removed.";
                    break;
                case 403:
                    model.Title = "That is not yours to open";
                    model.Explanation =
                        "Your role does not have access to this screen. If you think it "
                        + "should, ask whoever looks after the accounts.";
                    break;
                default:
                    model.Title = "Something went wrong";
                    model.Explanation =
                        "This page could not be shown. The problem has been recorded. "
                        + "If it keeps happening, quote the reference below when you report it.";
                    break;
            }
        }

        /// <summary>
        /// Where "try again" points: only a path within this application, and
        /// never one carrying a query string.
        /// </summary>
        private static string SafeReturnPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!path.StartsWith("/", StringComparison.Ordinal)
                || path.StartsWith("//", StringComparison.Ordinal)
                || path.Contains(":"))
            {
                return null;
            }

            var query = path.IndexOf('?');
            return query >= 0 ? path.Substring(0, query) : path;
        }
    }
}
