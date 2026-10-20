using System.Collections.Concurrent;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>
/// Runs the search behind every hint link and drops the links that find no rows, so a hint never sends someone to an
/// empty page. Hints are built to lead somewhere; filters combine in many ways, and this keeps that promise for all of
/// them. A dropped link is logged as a warning, since it means a hint was built wrong. A link that only changes the
/// narrowing filters (setting, fee type, product, group, code type) reuses the current search's facts, so most checks
/// cost no query at all.
/// </summary>
internal sealed class HintLinkChecker(DuckDbConnectionFactory connections, ResolvedSnapshot snapshot, ReferenceCatalog catalog, ILogger logger)
{
    /// <summary>A few searches at once: each is one query plus in-memory grouping.</summary>
    private const int Parallelism = 4;

    /// <summary>The search parameters a link may set. Anything else is a bug in the hint.</summary>
    private static readonly HashSet<string> Keys =
        new(["code", "codeType", "npi", "ein", "provider", "specialty", "county", "product", "setting", "feeType", "group"], StringComparer.Ordinal);

    public async Task<List<SearchHint>> KeepLinksThatFindRatesAsync(
        SearchRatesQuery request, SearchContext search, IReadOnlyList<RateFactRow> facts, List<SearchHint> hints, CancellationToken cancellationToken)
    {
        var links = hints.SelectMany(hint => hint.Links).ToList();
        if (links.Count == 0)
        {
            return hints;
        }

        var targets = links.Select(link => Target(request, link)).OfType<SearchRatesQuery>().Distinct().ToList();
        var findsRates = new ConcurrentDictionary<SearchRatesQuery, bool>();
        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (target, token) => findsRates[target] = await FindsRatesAsync(target, search, facts, token));

        return hints.Select(hint =>
        {
            var kept = hint.Links.Where(link => Target(request, link) is { } target && findsRates[target]).ToList();
            if (kept.Count == hint.Links.Count)
            {
                return hint;
            }

            foreach (var dropped in hint.Links.Except(kept))
            {
                logger.LogWarning("Dropped a {Kind} hint link that finds no rates: {Label} {@Query}", hint.Kind, dropped.Label, dropped.Query);
            }

            return hint with { Message = kept.Count == 0 ? WithoutLeadIn(hint.Message) : hint.Message, Links = kept };
        }).ToList();
    }

    /// <summary>The search a link leads to: its values applied to the current search (null removes one), page 1.</summary>
    private static SearchRatesQuery? Target(SearchRatesQuery request, HintLink link)
    {
        if (!link.Query.Keys.All(Keys.Contains))
        {
            return null;
        }

        string? Value(string key, string? current) => link.Query.TryGetValue(key, out var value) ? value : current;
        return request with
        {
            Code = Value("code", request.Code),
            CodeType = Value("codeType", request.CodeType),
            Npi = Value("npi", request.Npi),
            Ein = Value("ein", request.Ein),
            Provider = Value("provider", request.Provider),
            Specialty = Value("specialty", request.Specialty),
            County = Value("county", request.County),
            Product = Value("product", request.Product),
            Setting = Value("setting", request.Setting),
            FeeType = Value("feeType", request.FeeType),
            Group = Value("group", request.Group),
            Page = 1,
        };
    }

    private async Task<bool> FindsRatesAsync(
        SearchRatesQuery target, SearchContext current, IReadOnlyList<RateFactRow> facts, CancellationToken cancellationToken)
    {
        var context = SearchContext.From(target);
        if (SameFacts(context, current))
        {
            // Same code forms and provider filters: the facts are the same, only the grouping options differ.
            return SearchRatesHandler.Assemble(facts, context, "rate", page: 1, pageSize: 1, catalog).Rows.Total > 0;
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        var (_, assembled) = await SearchRatesHandler.RunAsync(connection, snapshot, context, "rate", page: 1, pageSize: 1, catalog);
        return assembled.Rows.Total > 0;
    }

    /// <summary>The facts a search finds depend on its code forms, its provider filters and whether its code type is foreign.</summary>
    private static bool SameFacts(SearchContext target, SearchContext current) =>
        target.Candidates.SequenceEqual(current.Candidates, StringComparer.Ordinal)
        && target.Npi == current.Npi
        && target.Ein == current.Ein
        && target.Name == current.Name
        && target.Specialty == current.Specialty
        && target.County == current.County
        && target.ForeignCodeType == current.ForeignCodeType
        && (!target.ForeignCodeType || target.CodeType == current.CodeType);

    /// <summary>"… (12 codes). Most common:" without its links reads "… (12 codes)."</summary>
    private static string WithoutLeadIn(string message)
    {
        if (!message.EndsWith(':'))
        {
            return message;
        }

        var end = message.LastIndexOf(". ", StringComparison.Ordinal);
        return end >= 0 ? message[..(end + 1)] : message.TrimEnd(':') + ".";
    }
}
