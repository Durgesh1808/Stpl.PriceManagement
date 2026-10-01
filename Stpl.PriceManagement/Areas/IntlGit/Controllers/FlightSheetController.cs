using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stpl.PriceManagement.Areas.IntlGit.Services;
using Stpl.PriceManagement.Areas.IntlGit.ViewModels;

namespace Stpl.PriceManagement.Areas.IntlGit.Controllers
{
    /// <summary>
    /// The airfare summary sheet for one tour. Every role can open it; the
    /// product team is the audience - they need to notice a fare creeping up
    /// across three hand-overs.
    /// </summary>
    [Area("IntlGit")]
    public sealed class FlightSheetController : Controller
    {
        private readonly TourService _tours;

        public FlightSheetController(TourService tours)
        {
            _tours = tours;
        }

        // GET /IntlGit/FlightSheet?code=...&changedOnly=true
        [HttpGet]
        public async Task<IActionResult> Index(string code, bool changedOnly, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return RedirectToAction("Index", "Products");
            }

            var vm = new FlightSheetViewModel { Code = code, ChangedOnly = changedOnly };

            vm.Sheet = await _tours.GetFlightSheetAsync(code, cancellationToken);
            if (vm.Sheet == null)
            {
                return NotFound();
            }

            return View(vm);
        }
    }
}
