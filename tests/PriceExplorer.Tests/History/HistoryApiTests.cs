using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.HistoryFiles;

namespace PriceExplorer.Tests.History;

/// <summary>Snapshots, changes and trends over three synthetic monthly snapshots (see <see cref="HistoryFiles"/>).</summary>
public sealed class HistoryApiTests(HistoryApiTests.Factory factory) : IClassFixture<HistoryApiTests.Factory>
{
    public sealed class Factory() : ExplorerApiFactory(Serve, null, null, Indexes);

    [Fact(Timeout = 60_000)]
    public async Task The_snapshot_list_flags_a_partial_upload_against_its_neighbours()
    {
        var ct = TestContext.Current.CancellationToken;
        var snapshots = await GetAsync("/api/v1/snapshots", ct);
        var ids = await IdsAsync(ct);

        var essentialJuly = Completeness(snapshots, July, Essential);
        essentialJuly.GetProperty("partial").GetBoolean().Should().BeTrue();
        essentialJuly.GetProperty("score").GetDecimal().Should().Be(0.14m, "1 unique rate against a median of 7 nearby");
        essentialJuly.GetProperty("rates").GetDecimal().Should().Be(0.14m);
        essentialJuly.GetProperty("eins").GetDecimal().Should().Be(0.2m);
        essentialJuly.GetProperty("codes").GetDecimal().Should().Be(0.5m);
        essentialJuly.GetProperty("comparedWith").EnumerateArray().Select(id => id.GetInt32()).Should().Equal(ids[August], ids[September]);

        // August's neighbours include the partial July file, which only lowers their median.
        Completeness(snapshots, August, Essential).GetProperty("score").GetDecimal().Should().Be(1.33m);
        Completeness(snapshots, August, Essential).GetProperty("partial").GetBoolean().Should().BeFalse();
        foreach (var date in new[] { July, August, September })
        {
            Completeness(snapshots, date, Ambetter).GetProperty("score").GetDecimal().Should().Be(1m);
            Completeness(snapshots, date, Ambetter).GetProperty("partial").GetBoolean().Should().BeFalse();
        }

        Snapshot(snapshots, September).GetProperty("isLatest").GetBoolean().Should().BeTrue();
    }

    [Fact(Timeout = 60_000)]
    public async Task Changes_compare_the_newest_snapshot_with_the_one_before_by_default()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await IdsAsync(ct);

        var result = await GetAsync("/api/v1/changes", ct);

        result.GetProperty("query").GetProperty("from").GetInt32().Should().Be(ids[August]);
        result.GetProperty("query").GetProperty("to").GetInt32().Should().Be(ids[September]);
        result.GetProperty("from").GetProperty("statisticsCurrent").GetBoolean().Should().BeTrue();
        result.GetProperty("to").GetProperty("isLatest").GetBoolean().Should().BeTrue();
        result.GetProperty("current").GetBoolean().Should().BeTrue();
        result.GetProperty("products").EnumerateArray().Should().OnlyContain(product => product.GetProperty("compared").GetBoolean());
        Counts(result).Should().Equal(5, 4, 1, 2, 1, 2, 1, 8);

        // Biggest moves first, then new and dropped rates by amount.
        Rows(result).Select(Describe).Should().Equal(
            "changed 88-7777777 99213 45.00→50.00 11.1",
            "changed 22-1111111 45378 600.00→660.00 10.0",
            "changed masked 45378 500.00→450.00 -10.0",
            "changed 44-3333333 45378 450.00→459.00 2.0",
            "changed 22-1111111 45378 610.00→620.00 1.6",
            "dropped 77-6666666 45378 800.00→",
            "new 66-5555555 45378 →750.00",
            "dropped 55-4444444 45378 700.00→",
            "new 55-4444444 45378 →700.00");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_change_row_names_the_provider_group_and_rates_as_search_does()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await GetAsync($"/api/v1/changes?code=45378&product={Ambetter}&kind=changed", ct);
        var alpha = Rows(result).Single(row => row.GetProperty("ein").GetString() == Alpha.Key);
        var county = Rows(result).Single(row => row.GetProperty("einMasked").GetBoolean());

        alpha.GetProperty("groupKey").GetString().Should().Be(AmbetterColonoscopyAsc);
        alpha.GetProperty("productLabel").GetString().Should().Be("Ambetter from Fidelis Care");
        alpha.GetProperty("codeName").GetString().Should().Be("COLONOSCOPY");
        alpha.GetProperty("feeTypeLabel").GetString().Should().Be("Facility fee");
        alpha.GetProperty("settingLabel").GetString().Should().Be("Ambulatory surgery center");
        alpha.GetProperty("unit").GetString().Should().Be("per procedure");
        alpha.GetProperty("businessName").GetString().Should().Be(Alpha.Name);
        alpha.GetProperty("einDisplay").GetString().Should().Be(Alpha.Ein);
        alpha.GetProperty("change").GetDecimal().Should().Be(60m);
        alpha.GetProperty("fromRates").GetInt32().Should().Be(1);
        alpha.GetProperty("toRates").GetInt32().Should().Be(1);

        // The masked tax ID is matched across files by its key, and stays masked.
        county.GetProperty("providerKey").GetString().Should().MatchRegex("^u[0-9a-f]{12}$");
        county.GetProperty("ein").ValueKind.Should().Be(JsonValueKind.Null);
        county.GetProperty("einDisplay").GetString().Should().Be("••-•••4567");
        result.GetRawText().Should().NotContain("071234567");

        // Each side's rate id opens that file's rate in the source drawer.
        var ids = await IdsAsync(ct);
        foreach (var (side, date, rate) in new[] { ("fromRateId", August, 600m), ("toRateId", September, 660m) })
        {
            var source = await GetAsync($"/api/v1/rates/{alpha.GetProperty(side).GetInt64()}/source", ct);
            source.GetProperty("snapshot").GetProperty("snapshotId").GetInt32().Should().Be(ids[date]);
            source.GetProperty("rate").GetDecimal().Should().Be(rate);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task A_provider_that_moves_to_another_setting_is_dropped_there_and_new_here_not_changed()
    {
        var result = await GetAsync($"/api/v1/changes?code=45378&product={Ambetter}", TestContext.Current.CancellationToken);
        var delta = Rows(result).Where(row => row.GetProperty("ein").GetString() == Delta.Key).ToList();

        delta.Select(row => (row.GetProperty("kind").GetString(), row.GetProperty("groupKey").GetString()))
            .Should().BeEquivalentTo([("dropped", AmbetterColonoscopyAsc), ("new", AmbetterColonoscopyHospital)]);
        delta.Should().OnlyContain(row => row.GetProperty("listedElsewhere").GetBoolean(), "each file lists Delta for 45378, in another setting");
        Rows(result).Where(row => row.GetProperty("ein").GetString() != Delta.Key)
            .Should().OnlyContain(row => !row.GetProperty("listedElsewhere").GetBoolean());
    }

    [Theory(Timeout = 60_000)]
    [InlineData("false", 5, "dropped 77-6666666 45378 800.00→|new 66-5555555 45378 →750.00", 1, 0, 1, 0)]
    [InlineData("true", 0, "dropped 55-4444444 45378 700.00→|new 55-4444444 45378 →700.00", 1, 1, 1, 1)]
    public async Task New_and_dropped_rates_listed_elsewhere_can_be_left_out_or_shown_alone(
        string listedElsewhere, int changed, string rows, int newCount, int newElsewhere, int droppedCount, int droppedElsewhere)
    {
        var result = await GetAsync($"/api/v1/changes?listedElsewhere={listedElsewhere}", TestContext.Current.CancellationToken);

        string.Join('|', Rows(result).Where(row => row.GetProperty("kind").GetString() != "changed").Select(Describe)).Should().Be(rows);
        Counts(result)[0].Should().Be(changed, "a changed rate is in the same group in both files, so it's never listed elsewhere");
        Counts(result)[3..7].Should().Equal(newCount, newElsewhere, droppedCount, droppedElsewhere);
        result.GetProperty("query").GetProperty("listedElsewhere").GetBoolean().Should().Be(listedElsewhere == "true");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_partial_file_is_left_out_of_changes_and_the_response_says_why()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await IdsAsync(ct);

        var result = await GetAsync($"/api/v1/changes?from={ids[July]}&to={ids[August]}", ct);

        var products = result.GetProperty("products").EnumerateArray().ToDictionary(
            product => product.GetProperty("productKey").GetString()!, product => product);
        products[Essential].GetProperty("compared").GetBoolean().Should().BeFalse();
        products[Essential].GetProperty("notComparedBecause").GetString().Should()
            .Be("Left out: the 2026-07-27 file looks incomplete. It has 14% of the rates that nearby months have.");
        products[Ambetter].GetProperty("compared").GetBoolean().Should().BeTrue();
        products[Ambetter].GetProperty("notComparedBecause").ValueKind.Should().Be(JsonValueKind.Null);
        Counts(result).Should().Equal([0, 0, 0, 0, 0, 0, 0, 8], "July's Ambetter file equals August's");
        Rows(result).Should().BeEmpty();
    }

    [Theory(Timeout = 60_000)]
    [InlineData("kind=changed&minChangePct=5", 3, "88-7777777|22-1111111|masked")]
    [InlineData("kind=new", 2, "66-5555555|55-4444444")]
    [InlineData("kind=dropped&code=45378", 2, "77-6666666|55-4444444")]
    [InlineData("product=essential-plan", 1, "22-1111111")]
    [InlineData("code=99213", 1, "88-7777777")]
    [InlineData("code=CPT%2099213&kind=changed", 1, "88-7777777")]
    [InlineData("sort=changePct&kind=changed", 5, "masked|22-1111111|44-3333333|22-1111111|88-7777777")]
    [InlineData("sort=-changePct&kind=changed&minChangePct=2", 4, "88-7777777|22-1111111|44-3333333|masked")]
    [InlineData("sort=provider&kind=dropped", 2, "55-4444444|77-6666666")]
    [InlineData("kind=increased", 4, "88-7777777|22-1111111|44-3333333|22-1111111")]
    [InlineData("kind=decreased", 1, "masked")]
    public async Task Filters_and_sorts_narrow_the_rows_and_counts(string filters, int total, string providers)
    {
        var result = await GetAsync($"/api/v1/changes?{filters}", TestContext.Current.CancellationToken);

        result.GetProperty("rows").GetProperty("total").GetInt32().Should().Be(total);
        string.Join('|', Rows(result).Select(Who)).Should().Be(providers);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_provider_with_several_rates_in_a_group_says_how_many_on_each_side()
    {
        var result = await GetAsync("/api/v1/changes?code=99213&kind=changed", TestContext.Current.CancellationToken);
        var tele = Rows(result).Single();

        tele.GetProperty("fromRates").GetInt32().Should().Be(2);
        tele.GetProperty("toRates").GetInt32().Should().Be(2);
        tele.GetProperty("from").GetDecimal().Should().Be(45m, "its value is the NPI-weighted median of 45 and 90");
    }

    [Fact(Timeout = 60_000)]
    public async Task Counts_ignore_the_kind_filter_and_a_minimum_change_only_narrows_changed_rates()
    {
        var ct = TestContext.Current.CancellationToken;

        var all = await GetAsync("/api/v1/changes?kind=new", ct);
        var bigOnes = await GetAsync("/api/v1/changes?minChangePct=5", ct);

        Counts(all).Should().Equal(5, 4, 1, 2, 1, 2, 1, 8);
        Counts(bigOnes).Should().Equal(3, 2, 1, 2, 1, 2, 1, 8);
        bigOnes.GetProperty("rows").GetProperty("total").GetInt32().Should().Be(7);
    }

    [Fact(Timeout = 60_000)]
    public async Task Pages_cover_every_change_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var whole = Rows(await GetAsync("/api/v1/changes?sort=code", ct)).Select(Describe).ToList();

        var paged = new List<string>();
        for (var page = 1; page <= 5; page++)
        {
            paged.AddRange(Rows(await GetAsync($"/api/v1/changes?sort=code&pageSize=2&page={page}", ct)).Select(Describe));
        }

        whole.Should().HaveCount(9);
        paged.Should().Equal(whole);
        Rows(await GetAsync("/api/v1/changes?sort=code&pageSize=2&page=6", ct)).Should().BeEmpty();
    }

    [Theory(Timeout = 60_000)]
    [InlineData("kind=raised", "kind")]
    [InlineData("sort=rate", "sort")]
    [InlineData("minChangePct=-1", "minChangePct")]
    [InlineData("minChangePct=1001", "minChangePct")]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("product=Essential%20Plan", "product")]
    [InlineData("code=4537-8", "code")]
    [InlineData("from=1&to=1", "from")]
    [InlineData("to=0", "to")]
    [InlineData("listedElsewhere=maybe", "listedElsewhere")]
    public async Task Bad_change_inputs_are_400s_naming_the_field(string query, string field)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/changes?{query}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().Should().Be("validation_failed");
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(query);
    }

    [Fact(Timeout = 60_000)]
    public async Task Snapshots_that_cant_be_compared_say_why()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await IdsAsync(ct);
        using var client = factory.CreateClient();

        var backwards = await ProblemAsync(client, $"/api/v1/changes?from={ids[September]}&to={ids[August]}", ct);
        var oldest = await ProblemAsync(client, $"/api/v1/changes?to={ids[July]}", ct);
        var unknown = await ProblemAsync(client, "/api/v1/changes?from=99", ct);

        backwards.Should().Be((HttpStatusCode.BadRequest, "snapshot_order"));
        oldest.Should().Be((HttpStatusCode.NotFound, "no_earlier_snapshot"));
        unknown.Should().Be((HttpStatusCode.NotFound, "snapshot_not_found"));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_trend_lists_the_selected_snapshots_groups_and_each_point_equals_that_snapshots_search()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await IdsAsync(ct);

        var trend = await GetAsync("/api/v1/rates/trend?code=45378", ct);

        trend.GetProperty("selected").GetProperty("snapshotId").GetInt32().Should().Be(ids[September]);
        trend.GetProperty("snapshots").EnumerateArray().Select(s => s.GetProperty("indexDate").GetString())
            .Should().Equal(July, August, September);
        var latest = await GetAsync("/api/v1/rates?code=45378", ct);
        Groups(trend).Select(group => group.GetProperty("key").GetString())
            .Should().Equal(latest.GetProperty("groups").EnumerateArray().Select(group => group.GetProperty("key").GetString()),
                "the trend lists the cards on screen, in their order");

        foreach (var date in new[] { July, August, September })
        {
            var search = await GetAsync($"/api/v1/rates?code=45378&snapshot={ids[date]}", ct);
            foreach (var card in search.GetProperty("groups").EnumerateArray())
            {
                var point = Points(trend, card.GetProperty("key").GetString()!).SingleOrDefault(p => p.GetProperty("indexDate").GetString() == date);
                if (point.ValueKind == JsonValueKind.Undefined)
                {
                    continue; // a group the September search doesn't show
                }

                var stats = card.GetProperty("stats");
                point.GetProperty("providerCount").GetInt32().Should().Be(card.GetProperty("providerCount").GetInt32());
                point.GetProperty("statsLevel").GetString().Should().Be(card.GetProperty("statsLevel").GetString());
                foreach (var name in new[] { "min", "p10", "p25", "median", "p75", "p90", "max" })
                {
                    point.GetProperty(name).GetRawText().Should().Be(stats.GetProperty(name).GetRawText(), $"{date} {name}");
                }
            }
        }

        Points(trend, AmbetterColonoscopyAsc).Select(p => p.GetProperty("providerCount").GetInt32()).Should().Equal(6, 6, 5);
        Points(trend, AmbetterColonoscopyHospital).Select(p => p.GetProperty("indexDate").GetString()).Should().Equal(September);
        Points(trend, EssentialColonoscopyAsc).Select(p => p.GetProperty("partial").GetBoolean())
            .Should().Equal([true, false, false], "July's Essential Plan file is a partial upload");
        Points(trend, AmbetterColonoscopyAsc).Should().OnlyContain(p => p.GetProperty("provider").ValueKind == JsonValueKind.Null);

        // Who stayed, moved, left and joined from each complete file to the next (July's Essential Plan file is partial).
        Points(trend, AmbetterColonoscopyAsc).Select(Step).Should().Equal(
            "none", $"{ids[July]}: 6 stayed, 0 up, 0 down, 0 reweighted, 0 left, 0 joined", $"{ids[August]}: 4 stayed, 2 up, 1 down, 0 reweighted, 2 left, 1 joined");
        Points(trend, EssentialColonoscopyAsc).Select(Step).Should().Equal(
            "none", "none", $"{ids[August]}: 5 stayed, 1 up, 0 down, 0 reweighted, 0 left, 0 joined");
    }

    [Theory(Timeout = 60_000)]
    [InlineData("npi=1299999991", "45.00|45.00|50.00")]
    [InlineData("npi=1299999992", "90.00|90.00|90.00")]
    [InlineData("ein=88-7777777", "45.00|45.00|50.00")]
    public async Task A_trend_follows_the_searched_providers_own_rate_as_each_snapshots_card_marks_it(string filter, string rates)
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await IdsAsync(ct);

        var trend = await GetAsync($"/api/v1/rates/trend?code=99213&{filter}", ct);
        var points = Points(trend, AmbetterOfficeVisit);

        string.Join('|', points.Select(p => p.GetProperty("provider").GetProperty("rate").GetDecimal().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))
            .Should().Be(rates);
        foreach (var point in points)
        {
            var search = await GetAsync($"/api/v1/rates?code=99213&{filter}&snapshot={point.GetProperty("snapshotId").GetInt32()}", ct);
            var marked = search.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("key").GetString() == AmbetterOfficeVisit)
                .GetProperty("highlights")[0];
            point.GetProperty("provider").GetProperty("rate").GetDecimal().Should().Be(marked.GetProperty("rate").GetDecimal());
            point.GetProperty("provider").GetProperty("providerKey").GetString().Should().Be("887777777");
        }

        Groups(trend).Select(group => group.GetProperty("key").GetString()).Should().Equal(
            [AmbetterOfficeVisit], "with a provider filter, only groups where it has a rate show, as on the cards");
        trend.GetProperty("selected").GetProperty("snapshotId").GetInt32().Should().Be(ids[September]);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_trend_for_an_older_snapshot_lists_that_snapshots_groups()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await IdsAsync(ct);

        var trend = await GetAsync($"/api/v1/rates/trend?code=45378&snapshot={ids[August]}", ct);

        trend.GetProperty("selected").GetProperty("isLatest").GetBoolean().Should().BeFalse();
        Groups(trend).Select(group => group.GetProperty("key").GetString()).Should().Equal(AmbetterColonoscopyAsc, EssentialColonoscopyAsc);
        Points(trend, AmbetterColonoscopyAsc).Select(p => p.GetProperty("indexDate").GetString()).Should().Equal(July, August, September);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_trend_takes_the_searchs_inputs_and_messages()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/rates/trend?npi=12", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("errors");
        errors.GetProperty("code")[0].GetString().Should().Be("Enter a billing code, for example 45378 (colonoscopy) or 0490 (ambulatory surgery).");
        errors.GetProperty("npi")[0].GetString().Should().Be("An NPI is 10 digits, for example 1396747861.");
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        using var response = await client.GetAsync(url, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private async Task<(HttpStatusCode Status, string? Code)> ProblemAsync(HttpClient client, string url, CancellationToken ct)
    {
        await factory.WaitForIngestionAsync(ct);
        using var response = await client.GetAsync(url, ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (response.StatusCode, problem.GetProperty("code").GetString());
    }

    /// <summary>Snapshot ids by index date: they're given out in load order (newest first), so tests look them up.</summary>
    private async Task<Dictionary<string, int>> IdsAsync(CancellationToken ct) =>
        (await GetAsync("/api/v1/snapshots", ct)).EnumerateArray().ToDictionary(
            snapshot => snapshot.GetProperty("indexDate").GetString()!, snapshot => snapshot.GetProperty("snapshotId").GetInt32());

    private static JsonElement Snapshot(JsonElement snapshots, string date) =>
        snapshots.EnumerateArray().Single(snapshot => snapshot.GetProperty("indexDate").GetString() == date);

    private static JsonElement Completeness(JsonElement snapshots, string date, string product) =>
        Snapshot(snapshots, date).GetProperty("products").EnumerateArray()
            .Single(file => file.GetProperty("productKey").GetString() == product).GetProperty("completeness");

    private static int[] Counts(JsonElement result)
    {
        var counts = result.GetProperty("counts");
        return new[] { "changed", "increased", "decreased", "new", "newListedElsewhere", "dropped", "droppedListedElsewhere", "unchanged" }
            .Select(name => counts.GetProperty(name).GetInt32()).ToArray();
    }

    private static List<JsonElement> Rows(JsonElement result) => result.GetProperty("rows").GetProperty("items").EnumerateArray().ToList();

    private static List<JsonElement> Groups(JsonElement trend) => trend.GetProperty("groups").EnumerateArray().ToList();

    private static List<JsonElement> Points(JsonElement trend, string key) =>
        Groups(trend).Single(group => group.GetProperty("key").GetString() == key).GetProperty("points").EnumerateArray().ToList();

    private static string Step(JsonElement point)
    {
        var step = point.GetProperty("sincePrevious");
        return step.ValueKind == JsonValueKind.Null
            ? "none"
            : $"{step.GetProperty("sinceSnapshotId").GetInt32()}: {step.GetProperty("stayed").GetInt32()} stayed, {step.GetProperty("raised").GetInt32()} up, " +
              $"{step.GetProperty("lowered").GetInt32()} down, {step.GetProperty("reweighted").GetInt32()} reweighted, " +
              $"{step.GetProperty("left").GetInt32()} left, {step.GetProperty("joined").GetInt32()} joined";
    }

    /// <summary>The provider's EIN as shown, or "masked".</summary>
    private static string Who(JsonElement row) =>
        row.GetProperty("einMasked").GetBoolean() ? "masked" : row.GetProperty("einDisplay").GetString()!;

    private static string Describe(JsonElement row)
    {
        static string Amount(JsonElement value) =>
            value.ValueKind == JsonValueKind.Null ? "" : value.GetDecimal().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        var pct = row.GetProperty("changePct");
        return $"{row.GetProperty("kind").GetString()} {Who(row)} {row.GetProperty("code").GetString()} " +
               $"{Amount(row.GetProperty("from"))}→{Amount(row.GetProperty("to"))}" +
               (pct.ValueKind == JsonValueKind.Null ? "" : $" {pct.GetDecimal().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
    }
}
