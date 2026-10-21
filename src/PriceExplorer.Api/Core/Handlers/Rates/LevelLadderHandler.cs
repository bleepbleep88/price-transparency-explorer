using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Models.Statistics;
using PriceExplorer.Api.Core.Queries.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>
/// The level ladder for a visit code: every level of its family, side by side for each plan, fee type, setting, modifier
/// set and rate type, so levels are only ever compared like for like. Built from the same facts and statistics search
/// uses (SearchRates.sql, then <see cref="RateSearchAssembler.Summarize"/>), so each step equals that level's search card.
/// </summary>
public sealed class GetLevelLadderHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<GetLevelLadderQuery, LevelLadder>
{
    public async Task<LevelLadder> Handle(GetLevelLadderQuery request, CancellationToken cancellationToken)
    {
        var family = ServiceCategories.FamilyOf(BillingCodes.Cpt, GetLevelLadderQuery.CodeOf(request.Code))!;
        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        var facts = (await SearchRatesHandler.QueryFactsAsync(connection, snapshot.SnapshotId, family.Codes))
            .Where(fact => fact.CodeType == BillingCodes.Cpt)
            .ToList();

        // A percentage of billed charges isn't a dollar amount, so it can't stand on a ladder of prices.
        var lines = RateSearchAssembler.Summarize(facts, catalog)
            .Where(group => group.NegotiatedType != "percentage")
            .GroupBy(group => string.Join('|', group.ProductKey, group.CodeType, group.FeeType, group.SettingKey, group.Modifiers, group.NegotiatedType),
                StringComparer.Ordinal)
            .Select(line => Line(line.Key, line.ToList(), family))
            .OrderByDescending(line => line.Levels.Count(level => level.ProviderCount >= RateStatistics.MinimumForBasicStats))
            .ThenByDescending(line => line.Levels.Sum(level => level.ProviderCount))
            .ThenBy(line => line.Key, StringComparer.Ordinal)
            .ToList();

        return new LevelLadder(
            new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest),
            family.Name,
            family.Codes.Select(code => new LevelCode(code, family.LevelOf(code))).ToList(),
            lines);
    }

    /// <summary>What a line says when its steps dip: the typical prices are of different places at each level.</summary>
    internal const string DifferentPlacesNote = "Different places are priced at each level, so the steps don't always rise.";

    /// <summary>
    /// The note for a line whose typical price is lower at a higher level than at a lower one, when the places priced at
    /// those two levels differ (99213 Essential Plan in the office: level 5 $101 from 70 places, level 4 $163 from 40).
    /// The same places paid less for more is another matter, so it gets no note.
    /// </summary>
    internal static string? DipNote(IReadOnlyList<GroupStats> levels)
    {
        var priced = levels.Where(level => level.Stats.Median is not null).ToList();
        for (var lower = 0; lower < priced.Count; lower++)
        {
            for (var higher = lower + 1; higher < priced.Count; higher++)
            {
                if (priced[higher].Stats.Median >= priced[lower].Stats.Median)
                {
                    continue;
                }

                var places = priced[lower].Providers.Select(provider => provider.ProviderKey).ToHashSet(StringComparer.Ordinal);
                if (!places.SetEquals(priced[higher].Providers.Select(provider => provider.ProviderKey)))
                {
                    return DifferentPlacesNote;
                }
            }
        }

        return null;
    }

    private LevelLine Line(string key, List<GroupStats> groups, LevelFamilyInfo family)
    {
        var first = groups[0];
        var ordered = groups.OrderBy(group => family.LevelOf(group.Code)).ToList();
        return new LevelLine(
            key,
            first.ProductKey,
            first.ProductLabel,
            first.FeeType,
            first.SettingKey,
            catalog.Setting(first.SettingKey)?.Label ?? first.SettingKey,
            first.Modifiers.Length == 0 ? [] : first.Modifiers.Split(',').Select(code => new ModifierRef(code, catalog.Modifier(code).Label)).ToList(),
            first.NegotiatedType,
            first.Unit,
            ordered
                .Select(group => new LevelStep(group.Code, family.LevelOf(group.Code), group.Key, group.ProviderCount, group.StatsLevel, group.Stats))
                .ToList(),
            DipNote(ordered));
    }
}
