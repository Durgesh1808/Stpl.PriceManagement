using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Stpl.PriceManagement.Areas.Core.Services
{
    /// <summary>
    /// Creates the team's accounts from configuration at startup.
    /// </summary>
    /// <remarks>
    /// WHY NOT IN A MIGRATION
    ///
    /// A migration is committed to the repository, so a password in one is a
    /// password in the repository - readable by anyone who can read the code,
    /// and still readable in the history after it is changed. So accounts come
    /// from configuration, which is not committed.
    ///
    /// WHAT IT WILL AND WILL NOT DO
    ///
    /// It creates people and keeps their name and role in step. It never
    /// clears a password that is already set, so running it again cannot lock
    /// the team out of accounts they are using, and it never deletes anybody -
    /// removing somebody who has left is a deliberate act, not a side effect
    /// of dropping a line from a configuration file.
    ///
    /// An account configured without a password is created and cannot be
    /// signed into. That is the intended way to add somebody: create them, and
    /// set the password separately.
    ///
    /// CONFIGURATION
    ///
    ///   Seed:Users:0:Email        someone@southerntravels.com
    ///   Seed:Users:0:DisplayName  Their Name
    ///   Seed:Users:0:Role         product.executive
    ///   Seed:Users:0:Password     (optional; omit to create without one)
    ///
    /// As environment variables that is Seed__Users__0__Email and so on. Put
    /// them in user-secrets or environment variables for production.
    /// </remarks>
    public sealed class UserSeeder : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserSeeder> _logger;

        public UserSeeder(
            IServiceProvider services, IConfiguration configuration, ILogger<UserSeeder> logger)
        {
            _services = services;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var configured = _configuration.GetSection("Seed:Users").GetChildren();

            var people = new List<SeedUser>();
            foreach (var entry in configured)
            {
                var person = new SeedUser
                {
                    Email = entry["Email"],
                    DisplayName = entry["DisplayName"],
                    Role = entry["Role"],
                    Password = entry["Password"]
                };

                if (string.IsNullOrWhiteSpace(person.Email)
                    || string.IsNullOrWhiteSpace(person.DisplayName)
                    || string.IsNullOrWhiteSpace(person.Role))
                {
                    _logger.LogWarning(
                        "Skipping a seeded user: Email, DisplayName and Role are all required.");
                    continue;
                }

                people.Add(person);
            }

            if (people.Count == 0)
            {
                _logger.LogInformation(
                    "No users configured under Seed:Users. Nobody will be able to sign in until "
                    + "an account exists.");
                return;
            }

            using (var scope = _services.CreateScope())
            {
                var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();

                foreach (var person in people)
                {
                    try
                    {
                        var result = await signIn.UpsertAsync(
                            person.Email.Trim(),
                            person.DisplayName.Trim(),
                            person.Role.Trim(),
                            person.Password,
                            cancellationToken);

                        // The address and whether a password is set; never the
                        // password, and never anything derived from it.
                        _logger.LogInformation(
                            "Seeded account {Email} as {Role}, password set: {HasPassword}.",
                            result.Email, result.Role, result.HasPassword);
                    }
                    catch (Exception ex)
                    {
                        // One bad line must not stop the service starting, and
                        // must not take the others with it.
                        _logger.LogError(
                            ex, "Could not seed the account {Email}.", person.Email);
                    }
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private sealed class SeedUser
        {
            public string Email { get; set; }
            public string DisplayName { get; set; }
            public string Role { get; set; }
            public string Password { get; set; }
        }
    }
}
