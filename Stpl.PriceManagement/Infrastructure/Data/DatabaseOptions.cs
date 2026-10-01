namespace Stpl.PriceManagement.Infrastructure.Data
{
    /// <summary>
    /// Where the database is. Bound from the "Database" section of
    /// appsettings.json (ConnectionStrings are kept out of source control in
    /// production - use user secrets or an environment variable).
    /// </summary>
    public sealed class DatabaseOptions
    {
        public const string SectionName = "Database";

        /// <summary>Connection string to Int_StplGITPricing.</summary>
        public string ConnectionString { get; set; }

        public int CommandTimeoutSeconds { get; set; } = 30;
    }
}
