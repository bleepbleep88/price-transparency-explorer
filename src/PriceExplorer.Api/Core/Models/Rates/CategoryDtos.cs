namespace PriceExplorer.Api.Core.Models.Rates;

/// <summary>
/// The categories of a snapshot's codes: CMS's RBCS categories, each with its subcategories and families, then the
/// code-range categories of codes RBCS doesn't list (revenue codes and a few others). Biggest first by rate count.
/// </summary>
public sealed record CategoryTree(SnapshotRef Snapshot, IReadOnlyList<CategoryNode> Categories);

/// <param name="Key">What the category filter on /codes takes: an RBCS category ("P"), subcategory ("PG") or family
/// ("PG004"), or a code-range category ("facility").</param>
/// <param name="Source">"cms_rbcs" or "code_ranges".</param>
/// <param name="Codes">Codes with rates in the snapshot in this category.</param>
/// <param name="Rates">Their unique rates (not expired).</param>
/// <param name="Children">Subcategories of a category, families of a subcategory; empty for families and code-range categories.</param>
public sealed record CategoryNode(string Key, string Label, string Source, int Codes, long Rates, IReadOnlyList<CategoryNode> Children);
