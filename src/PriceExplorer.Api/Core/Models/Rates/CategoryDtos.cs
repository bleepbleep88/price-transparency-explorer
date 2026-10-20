namespace PriceExplorer.Api.Core.Models.Rates;

/// <summary>
/// The categories of a snapshot's codes: CMS's RBCS categories, each with its subcategories and families, then one
/// root, "Not in CMS's categories" (key code-ranges), over the code-range categories of codes RBCS doesn't list (revenue
/// codes and a few others). Biggest first by rate count; children add up to their parent.
/// </summary>
public sealed record CategoryTree(SnapshotRef Snapshot, IReadOnlyList<CategoryNode> Categories);

/// <param name="Key">What the category filter on /codes takes: an RBCS category ("P"), subcategory ("PG") or family
/// ("PG004"), or a code-range category ("facility").</param>
/// <param name="Source">"cms_rbcs" or "code_ranges".</param>
/// <param name="Codes">Codes with rates in the snapshot in this category.</param>
/// <param name="Rates">Their unique rates (not expired).</param>
/// <param name="Children">Subcategories of a category; families of a subcategory, with "Other …" (key PG000) for its codes
/// CMS gives no family; the code-range categories under code-ranges. Empty for families, and for a subcategory with no
/// families at all.</param>
public sealed record CategoryNode(string Key, string Label, string Source, int Codes, long Rates, IReadOnlyList<CategoryNode> Children);
