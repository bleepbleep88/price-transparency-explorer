using System.ComponentModel.DataAnnotations;

namespace PriceExplorer.Api.Configuration;

public sealed class ReferenceOptions
{
    public const string SectionName = "Reference";

    /// <summary>Provider directory built by tools/PriceExplorer.ReferenceBuilder. Relative paths resolve against the app folder.</summary>
    [Required]
    public string ProviderDirectoryPath { get; set; } = Path.Combine("reference", "provider_directory.csv.gz");
}
