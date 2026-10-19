namespace PriceExplorer.Tests.Support;

internal static class TestFiles
{
    /// <summary>
    /// Real slices of Centene's 2026-08-31 Fidelis files (see Fixtures/README.md), copied next to the test assembly.
    /// </summary>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
