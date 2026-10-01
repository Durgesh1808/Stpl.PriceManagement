using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Stpl.PriceManagement.Areas.Core.Models;
using Stpl.PriceManagement.Areas.Core.Repositories;
using Stpl.PriceManagement.Areas.Core.Services;
using Stpl.PriceManagement.Areas.Core.Services.Email;
using Stpl.PriceManagement.Areas.IntlGit.Repositories;
using Stpl.PriceManagement.Areas.IntlGit.Services;
using Stpl.PriceManagement.Infrastructure.Data;
using Stpl.PriceManagement.Infrastructure.Web;

namespace Stpl.PriceManagement
{
    /// <summary>
    /// Southern Travels - Price Management. One application:
    ///   browser -> Controller (Areas/*/Controllers) -> Service -> Repository (ADO.NET) -> stored procedure.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            AddServices(builder);

            // ---------------------------------------------------------------
            // "account ..." on the command line runs an account job and exits
            // (list, reset, unlock, disable, enable - see AccountCommands).
            // ---------------------------------------------------------------
            if (args.Length > 0 && string.Equals(args[0], "account", StringComparison.OrdinalIgnoreCase))
            {
                using (var tool = builder.Build())
                {
                    return await AccountCommands.RunAsync(tool.Services, args);
                }
            }

            var app = builder.Build();
            UsePipeline(app);

            await app.RunAsync();
            return 0;
        }

        /// <summary>Everything the application is made of, area by area.</summary>
        public static void AddServices(WebApplicationBuilder builder)
        {

            // ---------------------------------------------------------------
            // MVC with Areas. Every POST must carry the antiforgery token -
            // the form tag helper adds it, and the grid's preview sends it as
            // a header.
            // ---------------------------------------------------------------
            builder.Services.AddControllersWithViews(options =>
            {
                options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            });

            /*
                Every screen requires a signed-in user. A fallback policy rather
                than [Authorize] on each controller: a screen is protected by
                EXISTING, and the way to get it wrong is to opt out explicitly.
                Sign-in, the error page and the health probes are the only
                things that do.
            */
            builder.Services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            builder.Services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Core/Account/Login";
                    options.LogoutPath = "/Core/Account/Logout";
                    options.AccessDeniedPath = "/Core/Account/Login";

                    // An office machine left unattended should not stay signed
                    // in all week. Sliding, so somebody working is not thrown out.
                    options.ExpireTimeSpan = TimeSpan.FromHours(8);
                    options.SlidingExpiration = true;

                    options.Cookie.Name = "st_auth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.IsEssential = true;
                    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                        ? CookieSecurePolicy.SameAsRequest
                        : CookieSecurePolicy.Always;
                });

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<CurrentUser>();

            // ---------------------------------------------------------------
            // Data: ADO.NET against Int_StplGITPricing.
            // ---------------------------------------------------------------
            builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
            builder.Services.AddSingleton<SqlDatabase>();

            // ---------------------------------------------------------------
            // Area: Core - accounts, notifications, email (shared by every pricing type)
            // ---------------------------------------------------------------
            builder.Services.AddScoped<IUserRepository, UserRepository>();
            builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
            builder.Services.AddScoped<INotificationEmailRepository, NotificationEmailRepository>();
            builder.Services.AddScoped<SignInService>();
            builder.Services.AddScoped<NotificationService>();
            builder.Services.AddSingleton<IPasswordHasher<UserSignInRow>, PasswordHasher<UserSignInRow>>();
            builder.Services.AddHostedService<UserSeeder>();

            /*
                Email with every notification. On when MailSettings:Enabled is
                true: SmtpNotificationEmailSender mails the address configured
                for the recipient's role and logs each attempt to core.EmailLog.
                Off: the no-op, in-app notifications only.
            */
            builder.Services.Configure<MailSettings>(builder.Configuration.GetSection(MailSettings.SectionName));
            if (builder.Configuration.GetValue<bool>(MailSettings.SectionName + ":Enabled"))
            {
                builder.Services.AddScoped<INotificationEmailSender, SmtpNotificationEmailSender>();
            }
            else
            {
                builder.Services.AddScoped<INotificationEmailSender, NoOpNotificationEmailSender>();
            }

            builder.Services.AddHostedService<NotificationEmailDispatcher>();

            // ---------------------------------------------------------------
            // Area: IntlGit - International GIT tour pricing
            // (a new pricing area registers its repositories and services here)
            // ---------------------------------------------------------------
            builder.Services.AddScoped<ITourRepository, TourRepository>();
            builder.Services.AddScoped<IChangeSetRepository, ChangeSetRepository>();
            builder.Services.AddScoped<TourService>();
            builder.Services.AddScoped<ChangeSetService>();
            builder.Services.AddHostedService<UnstampedChangeSetSweeper>();

            // Readiness proves the database can be reached.
            builder.Services
                .AddHealthChecks()
                .AddCheck<DatabaseHealthCheck>("database", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });
        }

        /// <summary>The request pipeline and the routes.</summary>
        public static void UsePipeline(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Core/Home/Error");
                app.UseHsts();

                /* And the failures that carry no exception at all - a bare 404
                   or 403 - get the same page, keeping their status code. */
                app.UseStatusCodePagesWithReExecute("/Core/Home/Error", "?code={0}");
            }

            app.UseStaticFiles();
            app.UseRouting();

            // Who you are, then what you may do.
            app.UseAuthentication();
            app.UseAuthorization();

            // /IntlGit/Products, /IntlGit/Revision, /Core/Account/Login, ...
            app.MapControllerRoute(
                name: "areas",
                pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

            // "/" - each role's own start screen.
            app.MapAreaControllerRoute(
                name: "root",
                areaName: "Core",
                pattern: "",
                defaults: new { controller = "Home", action = "Index" });

            app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
                .AllowAnonymous();
            app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
                .AllowAnonymous();
        }
    }
}
