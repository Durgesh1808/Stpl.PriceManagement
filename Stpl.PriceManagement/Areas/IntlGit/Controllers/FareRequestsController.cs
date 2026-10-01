using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stpl.PriceManagement.Areas.IntlGit.Services;
using Stpl.PriceManagement.Areas.IntlGit.ViewModels;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.IntlGit.Controllers
{
    /// <summary>
    /// Air-ticketing's queue: every tour the product team is waiting on - the
    /// counterpart of the product team's "send to air-ticketing".
    /// </summary>
    [Area("IntlGit")]
    public sealed class FareRequestsController : Controller
    {
        private readonly TourService _tours;
        private readonly CurrentUser _user;

        public FareRequestsController(TourService tours, CurrentUser user)
        {
            _tours = tours;
            _user = user;
        }

        // GET /IntlGit/FareRequests
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            // Product executives raise these; tech support has no part in them.
            if (!_user.IsAirTicketing)
            {
                return Redirect(_user.HomePage);
            }

            var vm = new FareRequestsViewModel
            {
                Requests = await _tours.ListFareRequestsAsync(cancellationToken),
                Crumbs = new List<(string, string)>
                {
                    ("Airfare", null),
                    ("Fare requests", null)
                }
            };

            return View(vm);
        }
    }
}
