using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Stpl.PriceManagement.Areas.Core.Domain;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Areas.Core.Services;
using Stpl.PriceManagement.Areas.Core.ViewModels;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement.Areas.Core.Controllers
{
    /// <summary>Signing in and out, changing your own password, and the rail toggle.</summary>
    [Area("Core")]
    public sealed class AccountController : Controller
    {
        private const string NavCookie = "st_nav";

        private readonly SignInService _signIn;
        private readonly CurrentUser _user;
        private readonly ILogger<AccountController> _logger;

        public AccountController(SignInService signIn, CurrentUser user, ILogger<AccountController> logger)
        {
            _signIn = signIn;
            _user = user;
            _logger = logger;
        }

        // GET /Core/Account/Login?ReturnUrl=...
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string returnUrl)
        {
            var model = new LoginViewModel { ReturnUrl = returnUrl };

            // Already signed in and visiting the login page: send them on.
            if (User != null && User.Identity != null && User.Identity.IsAuthenticated)
            {
                return Redirect(SafeReturnUrl(model.ReturnUrl));
            }

            return View(model);
        }

        // POST /Core/Account/Login
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
        {
            SignInResponse result;
            try
            {
                result = await _signIn.SignInAsync(
                    new SignInRequest { Email = model.Email, Password = model.Password }, cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Never log the attempt's contents: the password is in them.
                _logger.LogWarning(ex, "Sign-in could not reach the database.");
                result = null;
            }

            model.Password = null;

            if (result == null)
            {
                // Distinct from a refusal, because the answer is different:
                // wait, do not re-type.
                model.Error = "Sign-in is unavailable at the moment. Please try again shortly.";
                return View(model);
            }

            if (!result.Succeeded)
            {
                model.Error = result.Message;
                return View(model);
            }

            UserRole role;
            if (!UserRoles.TryParse(result.Role, out role))
            {
                // Refuse rather than guess: guessing here means guessing at
                // somebody's permissions.
                model.Error = "That account's role is not one this application recognises.";
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()),
                new Claim(ClaimTypes.Name, result.DisplayName ?? result.Email),
                new Claim(ClaimTypes.Email, result.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, UserRoles.ClaimValue(role)),

                // Which run of the application issued this sign-in - see AppInstance.
                new Claim(AppInstance.ClaimType, AppInstance.Id)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    // Not persistent: a shared office machine should not stay
                    // signed in after the browser closes.
                    IsPersistent = false
                });

            return Redirect(SafeReturnUrl(model.ReturnUrl));
        }

        // GET /Core/Account/Logout - a sign-out is a POST; a GET goes home.
        [HttpGet]
        public IActionResult Logout()
        {
            return Redirect(_user.HomePage);
        }

        // POST /Core/Account/Logout
        [HttpPost]
        [ActionName("Logout")]
        public async Task<IActionResult> LogoutPost()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/Core/Account/Login");
        }

        // GET /Core/Account/Password
        [HttpGet]
        public IActionResult Password()
        {
            return View(new PasswordViewModel { DisplayName = _user.DisplayName, Email = _user.Email });
        }

        // POST /Core/Account/Password
        [HttpPost]
        public async Task<IActionResult> Password(PasswordViewModel model, CancellationToken cancellationToken)
        {
            model.DisplayName = _user.DisplayName;
            model.Email = _user.Email;

            /*
                Checked here rather than in the service: it is the only rule
                this form knows that the service cannot - the service is given
                one new password and has no idea what was typed in the second box.
            */
            if (model.NewPassword != model.ConfirmPassword)
            {
                model.Error = "The two new passwords are not the same.";
                return View(model);
            }

            ChangePasswordResponse result;
            try
            {
                result = await _signIn.ChangePasswordAsync(
                    new ChangePasswordRequest
                    {
                        Email = _user.Email,
                        CurrentPassword = model.CurrentPassword,
                        NewPassword = model.NewPassword
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Never log the attempt's contents: two passwords are in them.
                _logger.LogWarning(ex, "Change-password could not reach the database.");
                result = null;
            }

            if (result == null)
            {
                model.Error = "That could not be saved just now. Please try again shortly.";
                return View(model);
            }

            if (!result.Succeeded)
            {
                model.Error = result.Message;
                return View(model);
            }

            // Not signed out afterwards: the cookie names this person and
            // nothing about it depends on the password.
            model.Changed = true;
            model.CurrentPassword = null;
            model.NewPassword = null;
            model.ConfirmPassword = null;
            return View(model);
        }

        // POST /Core/Account/ToggleNav?returnUrl=...
        [HttpPost]
        public IActionResult ToggleNav(string returnUrl)
        {
            var isOpen = Request.Cookies[NavCookie] != "closed";

            Response.Cookies.Append(
                NavCookie,
                isOpen ? "closed" : "open",
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    Expires = DateTimeOffset.UtcNow.AddYears(1)
                });

            // Only same-site paths are followed, so this cannot bounce a user off-site.
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : Redirect("/");
        }

        private string SafeReturnUrl(string returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return returnUrl;
            }

            return "/IntlGit/Products";
        }
    }
}
