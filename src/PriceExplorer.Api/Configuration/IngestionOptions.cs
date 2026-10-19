using System.ComponentModel.DataAnnotations;

namespace PriceExplorer.Api.Configuration;

/// <summary>Where the rate data comes from and where downloads + the DuckDB database are kept.</summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Run ingestion at startup. Tests that seed their own database turn this off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Folder for cached downloads and explorer.duckdb. Relative paths resolve against the app's content root.</summary>
    [Required]
    public string DataDirectory { get; set; } = "data";

    /// <summary>Table-of-contents (index) files to load, one per snapshot. Centene's file dates are irregular, so they are listed explicitly.</summary>
    [MinLength(1)]
    public List<SnapshotSource> Snapshots { get; set; } = [];

    /// <summary>Download attempts per file. Each retry resumes from the bytes already on disk.</summary>
    [Range(1, 20)]
    public int MaxDownloadAttempts { get; set; } = 5;

    /// <summary>Abort a download attempt when no bytes arrive for this long.</summary>
    [Range(5, 600)]
    public int StallTimeoutSeconds { get; set; } = 60;

    [Required]
    public string UserAgent { get; set; } = "PriceTransparencyExplorer/1.0";
}

public sealed class SnapshotSource
{
    [Required]
    [Url]
    public string IndexUrl { get; set; } = "";
}
