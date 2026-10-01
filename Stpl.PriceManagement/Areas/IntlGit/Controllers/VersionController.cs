using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stpl.PriceManagement.Areas.IntlGit.Services;
using Stpl.PriceManagement.Areas.IntlGit.ViewModels;

namespace Stpl.PriceManagement.Areas.IntlGit.Controllers
{
    /// <summary>
    /// A past version of a product page, read-only. Versions raised before the
    /// snapshot existed have no photograph; the page says so and offers the
    /// change set, which is the only record those versions have.
    /// </summary>
    [Area("IntlGit")]
    public sealed class VersionController : Controller
    {
        private readonly TourService _tours;
        private readonly ChangeSetService _changeSets;

        public VersionController(TourService tours, ChangeSetService changeSets)
        {
            _tours = tours;
            _changeSets = changeSets;
        }

        // GET /IntlGit/Version?code=...&version=v14
        [HttpGet]
        public async Task<IActionResult> Index(string code, string version, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(version))
            {
                return RedirectToAction("Index", "Products");
            }

            var vm = new VersionViewModel { Code = code, Version = version };

            vm.History = await _tours.GetVersionHistoryAsync(code, cancellationToken);
            vm.Snapshot = await _tours.GetVersionSnapshotAsync(code, version, cancellationToken);

            /*
                The side panel of changes comes from the change set, not from
                the snapshot: the snapshot says what the page looked like, the
                change set says what moved to make it look that way.
            */
            var reference = vm.ReferenceFor(version);
            if (!string.IsNullOrWhiteSpace(reference))
            {
                vm.ChangeSet = await _changeSets.GetAsync(reference, cancellationToken);
            }

            return View(vm);
        }
    }
}
