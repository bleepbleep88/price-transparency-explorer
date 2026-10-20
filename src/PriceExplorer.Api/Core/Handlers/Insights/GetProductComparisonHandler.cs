using Dapper;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Exceptions;
using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.Insights;
using PriceExplorer.Api.Core.Queries.Insights;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Insights;

/// <summary>
/// Pairs each provider's saved value in a comparison group under one product with its value in the same group under
/// another (provider_values), and sums up the ratio product ÷ versus: overall, by band, by category, pair by pair.
/// </summary>
internal sealed class GetProductComparisonHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<GetProductComparisonQuery, ProductComparison>
{
    public async Task<ProductComparison> Handle(GetProductComparisonQuery request, CancellationToken cancellationToken)
    {
        var sort = request.Sort ?? "difference";
        var (codes, codeType) = InsightInput.Codes(request.Code);
        var category = InsightSupport.Clean(request.Category);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var products = (await connection.QueryAsync<ProductRef>(
            SqlFile.For<GetProductComparisonHandler>("SnapshotProducts.sql"), new { snapshotId = snapshot.SnapshotId }, transaction)).ToList();
        var (product, versus) = Choose(products, InsightSupport.Clean(request.Product), InsightSupport.Clean(request.Versus));
        var statistics = await InsightSupport.BasisAsync(connection, snapshot, transaction);

        var rows = await connection.QueryAsync<PairRow>(
            SqlFile.For<GetProductComparisonHandler>("ProductPairs.sql"),
            new
            {
                snapshotId = snapshot.SnapshotId,
                product = product.Key,
                versus = versus.Key,
                codes = InsightSupport.CodeList(codes),
                codeType,
                setting = InsightSupport.Clean(request.Setting),
            },
            transaction);

        // A ratio only means something when both rates price the same thing: a drug can be a facility charge in one
        // product and a price per unit in the other.
        var (differentUnits, noVersusRate) = (0, 0);
        var comparable = new List<Pair>();
        foreach (var row in rows)
        {
            if (row.ProductUnit != row.VersusUnit)
            {
                differentUnits++;
            }
            else if (InsightMath.ExactRatio(row.ProductValue, row.VersusValue) is not { } ratio)
            {
                noVersusRate++;
            }
            else
            {
                comparable.Add(new Pair(row, ratio, ServiceCategories.Classify(row.CodeType, row.Code)));
            }
        }

        var categories = comparable
            .GroupBy(pair => pair.Category.Key, StringComparer.Ordinal)
            .Select(same => new CategoryOption(same.Key, same.First().Category.Label, same.Count()))
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Label, StringComparer.Ordinal)
            .ToList();
        var shown = comparable.Where(pair => category is null || pair.Category.Key == category).ToList();

        var sorted = Sort(shown, sort).ToList();
        var skip = (long)(request.Page - 1) * request.PageSize;
        var page = skip >= sorted.Count ? [] : sorted.Skip((int)skip).Take(request.PageSize).ToList();
        var names = await InsightSupport.CodeNamesAsync(connection, snapshot.SnapshotId, page.Select(pair => pair.Row.Code), transaction);

        var notes = new List<string>();
        if (differentUnits > 0)
        {
            notes.Add($"{Count(differentUnits, "pair")} left out: the two products price {(differentUnits == 1 ? "it" : "them")} in different units.");
        }

        if (noVersusRate > 0)
        {
            notes.Add($"{Count(noVersusRate, "pair")} left out: {versus.Label} lists a rate of $0.");
        }

        return new ProductComparison(
            statistics,
            new ProductComparisonEcho(InsightSupport.Clean(request.Code), codes, category, InsightSupport.Clean(request.Setting), sort,
                request.Page, request.PageSize),
            product,
            versus,
            products,
            categories,
            Summarize(shown),
            InsightMath.Bands
                .Select(band => new RatioBand(band.Key, band.Label, band.Direction, shown.Count(pair => InsightMath.BandOf(pair.Ratio) == band.Key)))
                .ToList(),
            shown
                .GroupBy(pair => pair.Category.Key, StringComparer.Ordinal)
                .Select(same => new CategoryComparison(
                    same.Key, same.First().Category.Label, same.Count(), same.Count(pair => pair.Ratio == 1), same.Count(pair => pair.Ratio < 1),
                    same.Count(pair => pair.Ratio > 1), RateStatistics.Median(same.Select(pair => pair.Ratio).Order().ToList())))
                .OrderByDescending(entry => entry.Pairs)
                .ThenBy(entry => entry.Label, StringComparer.Ordinal)
                .ToList(),
            new ComparisonPairsPage(sorted.Count, request.Page, request.PageSize, page.Select(pair => Describe(pair, names, snapshot)).ToList()),
            notes);
    }

    /// <summary>
    /// The two products to compare. Without a choice, a snapshot's two products are compared the second by name against
    /// the first, so the order never depends on a payer's brand names.
    /// </summary>
    private static (ProductRef Product, ProductRef Versus) Choose(IReadOnlyList<ProductRef> products, string? product, string? versus)
    {
        if (product is not null && versus is not null)
        {
            return (Find(product, "product"), Find(versus, "versus"));
        }

        if (products.Count != 2)
        {
            throw new BadInputException(
                products.Count < 2
                    ? "This snapshot has only one product, so there is nothing to compare."
                    : $"Pick the two products to compare: {string.Join(", ", products.Select(item => item.Key))}.",
                "pick_products");
        }

        if (product is not null)
        {
            var chosen = Find(product, "product");
            return (chosen, products.Single(item => item != chosen));
        }

        if (versus is not null)
        {
            var chosen = Find(versus, "versus");
            return (products.Single(item => item != chosen), chosen);
        }

        return (products[1], products[0]);

        ProductRef Find(string key, string field) =>
            products.FirstOrDefault(item => item.Key == key)
            ?? throw new ValidationException([new ValidationFailure(field,
                $"{key} isn't a product in this snapshot. Products: {string.Join(", ", products.Select(item => item.Key))}.")]);
    }

    private static ComparisonSummary Summarize(IReadOnlyList<Pair> pairs)
    {
        var ratios = pairs.Select(pair => pair.Ratio).Order().ToList();
        decimal? Quantile(double q) => ratios.Count == 0 ? null : RateStatistics.Quantile(ratios, q);
        return new ComparisonSummary(
            pairs.Count,
            pairs.Select(pair => pair.Row.ProviderKey).Distinct(StringComparer.Ordinal).Count(),
            pairs.Select(pair => (pair.Row.CodeType, pair.Row.Code)).Distinct().Count(),
            pairs.Count(pair => pair.Ratio == 1),
            pairs.Count(pair => pair.Ratio < 1),
            pairs.Count(pair => pair.Ratio > 1),
            Quantile(0.10),
            Quantile(0.25),
            Quantile(0.50),
            Quantile(0.75),
            Quantile(0.90));
    }

    /// <summary>Every sort ends on code, provider and group, so equal ratios keep one order and pages never overlap.</summary>
    private static IEnumerable<Pair> Sort(IEnumerable<Pair> pairs, string sort)
    {
        var sorted = sort switch
        {
            "ratio" => pairs.OrderBy(pair => pair.Ratio),
            "-ratio" => pairs.OrderByDescending(pair => pair.Ratio),
            _ => pairs.OrderByDescending(pair => InsightMath.Distance(pair.Ratio)),
        };
        return sorted
            .ThenByDescending(pair => Math.Abs(pair.Row.ProductValue - pair.Row.VersusValue))
            .ThenBy(pair => pair.Row.CodeType, StringComparer.Ordinal)
            .ThenBy(pair => pair.Row.Code, StringComparer.Ordinal)
            .ThenBy(pair => pair.Row.ProviderKey, StringComparer.Ordinal)
            .ThenBy(pair => pair.Row.ProductGroupKey, StringComparer.Ordinal);
    }

    private ComparisonPair Describe(Pair pair, IReadOnlyDictionary<(string, string), string> names, ResolvedSnapshot snapshot)
    {
        var row = pair.Row;
        return new ComparisonPair(
            row.ProviderKey,
            row.Ein,
            row.EinDisplay,
            row.EinMasked,
            row.BusinessName,
            row.CodeType,
            row.Code,
            InsightSupport.NameOf(names, catalog, row.CodeType, row.Code),
            pair.Category.Key,
            pair.Category.Label,
            row.FeeType,
            RateSearchAssembler.FeeTypeLabelOf(row.FeeType),
            row.SettingKey,
            InsightSupport.SettingLabel(row.SettingKey, catalog),
            InsightSupport.Modifiers(row.Modifiers, catalog),
            row.ProductUnit,
            row.PerUnit,
            row.ProductValue,
            row.VersusValue,
            RateStatistics.Ratio(row.ProductValue, row.VersusValue)!.Value,
            row.ProductValue - row.VersusValue,
            InsightSupport.SearchLink(snapshot, row.CodeType, row.Code, row.ProductGroupKey, row.ProviderKey),
            InsightSupport.SearchLink(snapshot, row.CodeType, row.Code, row.VersusGroupKey, row.ProviderKey));
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    /// <param name="Ratio">ProductValue ÷ VersusValue, unrounded.</param>
    private sealed record Pair(PairRow Row, decimal Ratio, ServiceCategory Category);

    private sealed record PairRow(
        string ProviderKey, string? Ein, string EinDisplay, bool EinMasked, string BusinessName, string CodeType, string Code,
        string FeeType, string SettingKey, string Modifiers, string ProductGroupKey, string VersusGroupKey, string ProductUnit,
        string VersusUnit, bool PerUnit, decimal ProductValue, decimal VersusValue);
}
