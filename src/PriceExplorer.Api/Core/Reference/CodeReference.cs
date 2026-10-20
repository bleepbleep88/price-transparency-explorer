using System.Collections.Frozen;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Reference;

/// <summary>One code's class in CMS's Restructured BETOS Classification System (reference/rbcs.csv.gz).</summary>
/// <param name="FamilyCode">The family number within the subcategory ("004"); null where CMS gives none.</param>
public sealed record RbcsClass(string CategoryCode, string Category, string SubcategoryCode, string Subcategory, string? FamilyCode, string? Family);

/// <summary>
/// What each code is, for browsing and filtering: CMS's RBCS category, subcategory and family when CMS classifies the
/// code, else the code-range category of <see cref="ServiceCategories"/> (revenue codes, codes RBCS doesn't list). Loaded
/// at startup by <see cref="ReferenceDataLoader"/>; without the file every code falls back to the code ranges.
/// </summary>
public sealed class CodeReference
{
    public const string RbcsSource = "cms_rbcs";
    public const string CodeRangesSource = "code_ranges";

    /// <summary>The one root over the code-range categories: codes CMS's RBCS doesn't list.</summary>
    public const string CodeRangesKey = "code-ranges";
    private const string CodeRangesLabel = "Not in CMS's categories";

    /// <summary>The family key of a subcategory's codes CMS gives no family: "PM000" holds "Other Musculoskeletal".</summary>
    private const string NoFamily = "000";

    /// <summary>RBCS category letters in plain words; CMS's own subcategory and family names are kept as written.</summary>
    private static readonly FrozenDictionary<string, string> CategoryLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["A"] = "Anesthesia",
        ["D"] = "Medical equipment and supplies",
        ["E"] = "Visits and care management",
        ["I"] = "Imaging",
        ["O"] = "Other services",
        ["P"] = "Procedures",
        ["R"] = "Treatments",
        ["T"] = "Tests",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private volatile FrozenDictionary<string, RbcsClass> _rbcs = FrozenDictionary<string, RbcsClass>.Empty;
    private volatile FrozenSet<string> _keys = CodeRangeKeys();

    /// <summary>How many codes RBCS classifies; 0 when the file is missing.</summary>
    public int RbcsCodes => _rbcs.Count;

    internal void Set(IEnumerable<(string Code, RbcsClass Class)> classes)
    {
        var rbcs = classes.ToFrozenDictionary(row => row.Code, row => row.Class, StringComparer.Ordinal);
        _keys = rbcs.Values
            .SelectMany(rbcs => new[] { rbcs.CategoryCode, rbcs.SubcategoryCode, rbcs.SubcategoryCode + (rbcs.FamilyCode ?? NoFamily) })
            .Concat(CodeRangeKeys())
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _rbcs = rbcs;
    }

    /// <summary>A key GET /api/v1/categories can list (any snapshot), in any letter case.</summary>
    public bool IsKnownCategory(string key) => _keys.Contains(key);

    private static FrozenSet<string> CodeRangeKeys() =>
        ServiceCategories.Keys.Append(CodeRangesKey).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>"Procedures · Digestive/Gastrointestinal"; just "Anesthesia" when the subcategory repeats the category.</summary>
    public static string Label(CodeClassification classification) =>
        classification.Subcategory is null || string.Equals(classification.Subcategory, classification.Category, StringComparison.OrdinalIgnoreCase)
            ? classification.Category
            : $"{classification.Category} · {classification.Subcategory}";

    /// <summary>The RBCS class of a CPT or HCPCS code, or null.</summary>
    public RbcsClass? Rbcs(string codeType, string code) =>
        codeType is BillingCodes.Cpt or BillingCodes.Hcpcs && _rbcs.TryGetValue(code, out var found) ? found : null;

    /// <summary>The code's category, as search results, the code typeahead and GET /api/v1/categories show it.</summary>
    public CodeClassification Classify(string codeType, string code)
    {
        if (Rbcs(codeType, code) is { } rbcs)
        {
            var key = rbcs.FamilyCode is null ? rbcs.SubcategoryCode : rbcs.SubcategoryCode + rbcs.FamilyCode;
            return new CodeClassification(key, CategoryLabel(rbcs.CategoryCode, rbcs.Category), rbcs.Subcategory, rbcs.Family, RbcsSource);
        }

        var fallback = ServiceCategories.Classify(codeType, code);
        return new CodeClassification(fallback.Key, fallback.Label, null, null, CodeRangesSource);
    }

    /// <summary>
    /// The category keys a code sits under, broadest first: an RBCS category ("P"), subcategory ("PG") and family
    /// ("PG004"; "PM000", "Other Musculoskeletal", for a code CMS gives no family), or the code-range root and its
    /// category ("code-ranges", "facility"). A category filter matches any of them.
    /// </summary>
    public IReadOnlyList<CategoryLevel> PathOf(string codeType, string code)
    {
        if (Rbcs(codeType, code) is not { } rbcs)
        {
            var fallback = ServiceCategories.Classify(codeType, code);
            return [new CategoryLevel(CodeRangesKey, CodeRangesLabel, CodeRangesSource), new CategoryLevel(fallback.Key, fallback.Label, CodeRangesSource)];
        }

        return
        [
            new(rbcs.CategoryCode, CategoryLabel(rbcs.CategoryCode, rbcs.Category), RbcsSource),
            new(rbcs.SubcategoryCode, rbcs.Subcategory, RbcsSource),
            rbcs.FamilyCode is null
                ? new CategoryLevel(rbcs.SubcategoryCode + NoFamily, $"Other {rbcs.Subcategory}", RbcsSource)
                : new CategoryLevel(rbcs.SubcategoryCode + rbcs.FamilyCode, rbcs.Family!, RbcsSource),
        ];
    }

    /// <summary>Whether a path level is an "Other …" family, which the tree shows only beside real families.</summary>
    public static bool IsOtherFamily(CategoryLevel level) =>
        level.Source == RbcsSource && level.Key.Length > 2 && level.Key.EndsWith(NoFamily, StringComparison.Ordinal);

    private static string CategoryLabel(string code, string cmsName) => CategoryLabels.GetValueOrDefault(code, cmsName);
}

/// <summary>One level of a code's category path: its key, name, and where it comes from.</summary>
public sealed record CategoryLevel(string Key, string Label, string Source);
