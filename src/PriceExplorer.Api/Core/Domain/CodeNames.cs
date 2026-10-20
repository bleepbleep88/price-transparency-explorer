namespace PriceExplorer.Api.Core.Domain;

/// <summary>Display rules for code names from CMS's HCPCS list, whose long names run up to a few thousand characters.</summary>
internal static class CodeNames
{
    public const int ShortLength = 100;

    /// <summary>The name cut at the last space before <paramref name="max"/> characters, with "…"; unchanged when it fits.</summary>
    public static string Short(string name, int max = ShortLength)
    {
        var trimmed = name.Trim();
        if (trimmed.Length <= max)
        {
            return trimmed;
        }

        var cut = trimmed.LastIndexOf(' ', max - 1);
        return $"{trimmed[..(cut > max / 2 ? cut : max - 1)].TrimEnd(',', ';', ':', ' ')}…";
    }
}
