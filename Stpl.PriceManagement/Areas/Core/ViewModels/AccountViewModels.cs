namespace Stpl.PriceManagement.Areas.Core.ViewModels
{
    /// <summary>The sign-in form.</summary>
    public sealed class LoginViewModel
    {
        public string Email { get; set; }

        /// <summary>Never sent back to the page.</summary>
        public string Password { get; set; }

        public string ReturnUrl { get; set; }

        public string Error { get; set; }
    }

    /// <summary>Changing your own password.</summary>
    public sealed class PasswordViewModel
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }

        public string Error { get; set; }
        public bool Changed { get; set; }

        public string DisplayName { get; set; }
        public string Email { get; set; }
    }
}
