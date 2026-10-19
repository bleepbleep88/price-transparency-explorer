using System.Text;
using System.Text.RegularExpressions;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>
/// Derives a product name for each reporting structure from its plan names, without hard-coding any payer's brands:
/// the longest run of leading words shared by at least 80% of the plans ("Ambetter from Fidelis Care", "Essential Plan").
/// The 80% threshold tolerates the occasional typo in published plan names.
/// </summary>
internal static partial class ProductNaming
{
    private const double SharedByAtLeast = 0.8;

    public static string LabelFor(IReadOnlyList<string?> planNames, string fallback)
    {
        var names = planNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Whitespace().Split(name!.Trim()))
            .ToList();
        if (names.Count == 0)
        {
            return fallback;
        }

        var needed = (int)Math.Ceiling(names.Count * SharedByAtLeast);
        var prefix = new List<string>();
        for (var position = 0; ; position++)
        {
            var mostCommon = names
                .Where(words => words.Length > position && words.Take(position).SequenceEqual(prefix, StringComparer.OrdinalIgnoreCase))
                .GroupBy(words => words[position], StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();

            if (mostCommon is null || mostCommon.Count() < needed)
            {
                break;
            }

            prefix.Add(mostCommon.First()[position]);
        }

        return prefix.Count == 0 ? fallback : string.Join(' ', prefix);
    }

    public static string KeyFor(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (var c in label.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } key ? key : "product";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
