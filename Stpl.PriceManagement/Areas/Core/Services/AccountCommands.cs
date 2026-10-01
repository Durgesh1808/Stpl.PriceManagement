using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Stpl.PriceManagement.Areas.Core.Services
{
    /// <summary>
    /// Account jobs run from the machine, not from a screen:
    /// <code>
    ///   dotnet Stpl.PriceManagement.dll account list
    ///   dotnet Stpl.PriceManagement.dll account reset   someone@southerntravels.com
    ///   dotnet Stpl.PriceManagement.dll account unlock  someone@southerntravels.com
    ///   dotnet Stpl.PriceManagement.dll account disable someone@southerntravels.com
    ///   dotnet Stpl.PriceManagement.dll account enable  someone@southerntravels.com
    /// </code>
    /// (From the source folder: <c>dotnet run -- account list</c>.)
    /// </summary>
    /// <remarks>
    /// This application has no administrator role - the three roles are jobs,
    /// not ranks - so resetting a forgotten password or turning off somebody
    /// who has left is done by whoever runs the server. It replaces
    /// tools/manage-account.sh and the operator-key API endpoint it called.
    /// </remarks>
    public static class AccountCommands
    {
        public static async Task<int> RunAsync(IServiceProvider services, string[] args)
        {
            var action = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            var email = args.Length > 2 ? args[2] : null;

            using (var scope = services.CreateScope())
            {
                var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
                var token = CancellationToken.None;

                switch (action)
                {
                    case "list":
                    {
                        var all = await signIn.ListAsync(token);
                        Console.WriteLine("{0,-40} {1,-26} {2,-10} {3}", "EMAIL", "ROLE", "STATE", "LAST SIGN-IN (UTC)");
                        foreach (var user in all)
                        {
                            var state = !user.IsActive ? "disabled"
                                : !user.HasPassword ? "no password"
                                : user.IsLockedOut ? "locked"
                                : "ready";

                            Console.WriteLine(
                                "{0,-40} {1,-26} {2,-10} {3}",
                                user.Email, user.Role, state,
                                user.LastSignInUtc.HasValue
                                    ? user.LastSignInUtc.Value.ToString("yyyy-MM-dd HH:mm")
                                    : "never signed in");
                        }

                        return 0;
                    }

                    case "reset":
                    {
                        if (string.IsNullOrWhiteSpace(email)) { return Usage(); }

                        // Generated rather than typed, so it never sits in a shell history.
                        var password = NewPassword();
                        var result = await signIn.ResetPasswordAsync(email, password, token);
                        if (!result.Succeeded)
                        {
                            Console.Error.WriteLine("The reset was refused: " + result.Message);
                            return 1;
                        }

                        Console.WriteLine("New password for " + email + ":");
                        Console.WriteLine();
                        Console.WriteLine("    " + password);
                        Console.WriteLine();
                        Console.WriteLine("It is shown once. They can change it from \"Change password\" after signing in.");
                        return 0;
                    }

                    case "unlock":
                        if (string.IsNullOrWhiteSpace(email)) { return Usage(); }
                        await signIn.UnlockAsync(email, token);
                        Console.WriteLine(email + " can try again now.");
                        return 0;

                    case "disable":
                    case "enable":
                    {
                        if (string.IsNullOrWhiteSpace(email)) { return Usage(); }

                        var row = await signIn.SetActiveAsync(email, action == "enable", token);
                        if (row == null)
                        {
                            Console.Error.WriteLine("No account with that address.");
                            return 1;
                        }

                        Console.WriteLine(action == "enable"
                            ? email + " is active again, with NO password. Run 'account reset' to give them one."
                            : email + " can no longer sign in, and their password has been cleared.");
                        return 0;
                    }

                    default:
                        return Usage();
                }
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: account list | reset <email> | unlock <email> | disable <email> | enable <email>");
            return 2;
        }

        /// <summary>Four groups of five lower-case letters, e.g. "xmlrp-ncpxa-cqtpz-ofpem".</summary>
        private static string NewPassword()
        {
            const string letters = "abcdefghijklmnopqrstuvwxyz";
            var groups = Enumerable.Range(0, 4).Select(g =>
                new string(Enumerable.Range(0, 5).Select(i => letters[RandomNumberGenerator.GetInt32(letters.Length)]).ToArray()));
            return string.Join("-", groups);
        }
    }
}
