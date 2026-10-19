using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Tests.Search;

public sealed class RateSearchAssemblerTests
{
    private static readonly ReferenceCatalog Catalog = new();

    private static int _nextRateId = 1;

    [Fact]
    public void Each_provider_counts_once_per_group_even_with_several_rate_rows()
    {
        // One surgery center published under two rate ids (different provider references) at the same price.
        var facts = new[]
        {
            Fact(provider: "111111111", rate: 500m, rateId: 1),
            Fact(provider: "111111111", rate: 500m, rateId: 2),
            Fact(provider: "222222222", rate: 700m),
        };

        var group = Assemble(facts).Groups.Should().ContainSingle().Subject;

        group.ProviderCount.Should().Be(2);
        group.RateCount.Should().Be(3);
        group.StatsLevel.Should().Be("list");
        group.ListedRates.Should().Equal(500m, 700m);
        group.Stats.Median.Should().BeNull("fewer than 5 providers only lists rates");
    }

    [Fact]
    public void Facility_and_professional_fees_and_different_settings_never_share_a_group()
    {
        var facts = new[]
        {
            Fact(billingClass: "institutional", pos: "24"),
            Fact(billingClass: "professional", pos: "24"),
            Fact(billingClass: "professional", pos: "11"),
            Fact(billingClass: "professional", pos: "11", modifiers: "26"),
        };

        var groups = Assemble(facts).Groups;

        groups.Should().HaveCount(4);
        groups.Select(g => (g.FeeType, g.SettingKey, string.Join(",", g.Modifiers.Select(m => m.Code))))
            .Should().BeEquivalentTo([("facility", "asc", ""), ("professional", "asc", ""), ("professional", "office", ""), ("professional", "office", "26")]);
        groups.Single(g => g.Modifiers.Count == 1).Notes.Should().Contain(note => note.Contains("modifier 26"));
    }

    [Fact]
    public void Off_campus_and_on_campus_hospital_outpatient_are_one_setting_and_a_rate_listing_both_counts_once()
    {
        var facts = new[]
        {
            Fact(provider: "111111111", rate: 100m, rateId: 7, pos: "19"),
            Fact(provider: "111111111", rate: 100m, rateId: 7, pos: "22"),
        };

        var group = Assemble(facts).Groups.Should().ContainSingle().Subject;

        group.SettingKey.Should().Be("hospital-outpatient");
        group.ProviderCount.Should().Be(1);
        group.Places.Select(p => p.Pos).Should().Equal("19", "22");
    }

    [Fact]
    public void Full_stats_and_savings_need_ten_providers()
    {
        var nine = Enumerable.Range(1, 9).Select(i => Fact(provider: $"10000000{i}", rate: 100m * i)).ToList();
        var ten = Enumerable.Range(1, 10).Select(i => Fact(provider: $"20000000{i % 10}{i / 10}", rate: 100m * i)).ToList();

        var basic = Assemble(nine).Groups.Single();
        var full = Assemble(ten).Groups.Single();

        basic.StatsLevel.Should().Be("basic");
        basic.Stats.Median.Should().Be(500m);
        basic.Stats.SpreadMultiple.Should().BeNull();
        basic.SavingsPerCase.Should().BeNull();

        full.StatsLevel.Should().Be("full");
        full.Stats.P25.Should().Be(325m);   // linear interpolation over 100..1000
        full.Stats.Median.Should().Be(550m);
        full.Stats.P75.Should().Be(775m);
        full.Stats.SpreadMultiple.Should().Be(10m);
        full.SavingsPerCase.Should().Be(225m);
    }

    [Fact]
    public void Highlights_appear_only_when_a_provider_filter_picked_someone_out()
    {
        var facts = Enumerable.Range(1, 6).Select(i => Fact(provider: $"30000000{i}", rate: 100m * i, matched: i == 2)).ToList();

        var unfiltered = Assemble(facts).Groups.Single();
        var filtered = Assemble(facts, providerFilterActive: true);

        unfiltered.Highlights.Should().BeEmpty();
        var highlight = filtered.Groups.Single().Highlights.Should().ContainSingle().Subject;
        highlight.Rate.Should().Be(200m);
        highlight.Percentile.Should().Be(20);
        highlight.VsMedian.Should().Be(0.57m);
        filtered.Rows.Items.Should().ContainSingle().Which.Matched.Should().BeTrue();
    }

    [Fact]
    public void A_provider_with_two_different_rates_shows_a_range_and_the_rate_most_of_its_npis_get()
    {
        var facts = new[]
        {
            Fact(provider: "111111111", rate: 100m, rateId: 1, npis: 2),
            Fact(provider: "111111111", rate: 140m, rateId: 2, npis: 5),
        };

        var row = Assemble(facts).Rows.Items.Should().ContainSingle().Subject;

        row.MultipleRates.Should().BeTrue();
        row.Rate.Should().Be(140m, "a real rate, never the $120 midpoint nobody is paid");
        (row.RateLow, row.RateHigh).Should().Be((100m, 140m));
        row.FirstRateId.Should().Be(2, "the source drawer shows the rate the row displays");
        row.RateIds.Should().Equal(1, 2);
    }

    [Fact]
    public void With_an_npi_filter_the_row_shows_that_npis_own_rate_not_its_tax_ids_blend()
    {
        // One tax ID pays its clinicians differently: the searched NPI is only on the $45 rate.
        var facts = new List<RateFactRow>
        {
            Fact(provider: "454115569", rate: 45m, rateId: 1, npis: 1, matched: true),
            Fact(provider: "454115569", rate: 90m, rateId: 2, npis: 3, matched: false),
        };
        facts.AddRange(Enumerable.Range(1, 9).Select(i => Fact(provider: $"60000000{i}", rate: 50m + (5m * i), matched: false)));

        var result = Assemble(facts, providerFilterActive: true);

        var row = result.Rows.Items.Should().ContainSingle().Subject;
        row.Rate.Should().Be(45m);
        (row.RateLow, row.RateHigh, row.MultipleRates).Should().Be((45m, 45m, false));
        row.FirstRateId.Should().Be(1);
        row.RateIds.Should().Equal(1);
        row.Percentile.Should().Be(0, "every other provider is paid more than $45");
        var group = result.Groups.Single();
        group.ProviderCount.Should().Be(10, "the market still counts the tax ID once, at its own value");
        group.Highlights.Should().ContainSingle().Which.Rate.Should().Be(45m);
    }

    [Fact]
    public void Paging_through_many_equal_rates_shows_every_row_exactly_once_in_a_stable_order()
    {
        var facts = Enumerable.Range(1, 57).Select(i => Fact(provider: $"7{i:D8}", rate: i % 3 == 0 ? 101.05m : 99m)).ToList();

        foreach (var sort in new[] { "rate", "-rate", "vsMedian", "-vsMedian", "provider", "-provider" })
        {
            var seen = new List<string>();
            for (var page = 1; page <= 3; page++)
            {
                seen.AddRange(Assemble(facts, sort: sort, page: page, pageSize: 25).Rows.Items.Select(row => row.ProviderKey));
            }

            seen.Should().HaveCount(57).And.OnlyHaveUniqueItems($"sort {sort} must not repeat or skip rows");
            Assemble(facts, sort: sort, page: 2, pageSize: 25).Rows.Items.Select(row => row.ProviderKey)
                .Should().Equal(Assemble(facts.AsEnumerable().Reverse(), sort: sort, page: 2, pageSize: 25).Rows.Items.Select(row => row.ProviderKey),
                    "the same request gives the same page whatever order the database returned rows in");
        }
    }

    [Fact]
    public void In_a_fee_schedule_providers_paid_the_standard_rate_are_typical()
    {
        var facts = Enumerable.Range(1, 12).Select(i => Fact(provider: $"8{i:D8}", rate: i == 1 ? 140m : i == 12 ? 190m : 167.09m)).ToList();

        var rows = Assemble(facts).Rows.Items;

        rows.Where(row => row.Rate == 167.09m).Should().OnlyContain(row => row.Band == "typical");
        rows.Single(row => row.Rate == 140m).Band.Should().Be("low");
        rows.Single(row => row.Rate == 190m).Band.Should().Be("high");
    }

    [Fact]
    public void A_filtered_row_carries_the_name_of_the_facility_that_matched_not_a_sibling_under_the_same_tax_id()
    {
        // A hospital system: one tax ID, two facilities with their own rates. The name filter matched only Jacobi's.
        var facts = new[]
        {
            Fact(provider: "132655001", rate: 400m, rateId: 1, matched: false, names: "Bellevue Hospital Center"),
            Fact(provider: "132655001", rate: 755.98m, rateId: 2, matched: true, names: "Jacobi Medical Center", matchedNames: "Jacobi Medical Center"),
        };

        var result = Assemble(facts, providerFilterActive: true);

        var row = result.Rows.Items.Should().ContainSingle().Subject;
        row.BusinessName.Should().Be("Jacobi Medical Center");
        row.OtherNames.Should().Equal("Bellevue Hospital Center");
        row.Rate.Should().Be(755.98m);
        result.Groups.Single().Highlights.Single().DisplayName.Should().Be("Jacobi Medical Center");
        Assemble(facts).Rows.Items.Single().BusinessName.Should().Be("Bellevue Hospital Center", "without a filter the first name leads");
    }

    [Fact]
    public void Provider_counters_count_tax_ids_not_rows()
    {
        // One provider with two rates in each of two groups (office and hospital outpatient).
        var facts = new[]
        {
            Fact(provider: "111111111", rate: 100m, rateId: 1, pos: "11", billingClass: "professional"),
            Fact(provider: "111111111", rate: 140m, rateId: 2, pos: "11", billingClass: "professional"),
            Fact(provider: "111111111", rate: 100m, rateId: 3, pos: "22", billingClass: "professional"),
            Fact(provider: "111111111", rate: 140m, rateId: 4, pos: "22", billingClass: "professional"),
        };

        Assemble(facts).Quality.ProvidersWithMultipleRates.Should().Be(1);
    }

    [Fact]
    public void A_rate_from_a_whole_tax_id_contract_flags_the_row_even_when_another_rate_lists_npis()
    {
        var facts = new[]
        {
            Fact(provider: "161078102", rate: 84m, rateId: 1, npis: 0, wholeTaxId: true),
            Fact(provider: "161078102", rate: 84m, rateId: 2, npis: 1),
        };

        var result = Assemble(facts);

        result.Rows.Items.Single().EinLevel.Should().BeTrue();
        result.Quality.WholeTaxIdContracts.Should().Be(1);
        Assemble([Fact(provider: "222222222", npis: 3)]).Rows.Items.Single().EinLevel.Should().BeFalse();
    }

    [Fact]
    public void A_page_past_the_end_is_empty_but_reports_the_total()
    {
        var facts = Enumerable.Range(1, 30).Select(i => Fact(provider: $"9{i:D8}", rate: i)).ToList();

        var page = Assemble(facts, page: 100_000, pageSize: 100).Rows;

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(30);
    }

    [Fact]
    public void Tax_ids_with_never_issued_prefixes_are_masked_in_rows()
    {
        var row = Assemble([Fact(provider: "uabc123def456", ein: "071234567", einUnverified: true)]).Rows.Items.Single();

        row.Ein.Should().BeNull();
        row.EinMasked.Should().BeTrue();
        row.EinDisplay.Should().Be("••-•••4567");
    }

    [Fact]
    public void Community_health_center_rates_are_labeled_as_all_inclusive_per_visit()
    {
        var group = Assemble([Fact(code: "99214", billingClass: "institutional", pos: "50")]).Groups.Single();

        group.SettingKey.Should().Be("fqhc");
        group.Unit.Should().Be("per visit (all-inclusive)");
    }

    [Fact]
    public void A_shared_exact_rate_is_called_out_as_a_likely_fee_schedule()
    {
        var facts = Enumerable.Range(1, 6).Select(i => Fact(provider: $"40000000{i}", rate: i <= 4 ? 167.09m : 200m)).ToList();

        Assemble(facts).Groups.Single().Notes.Should().Contain(note => note.StartsWith("4 of 6 providers are paid exactly $167.09"));
    }

    [Fact]
    public void Rows_sort_and_page_server_side()
    {
        var facts = Enumerable.Range(1, 30).Select(i => Fact(provider: $"5{i:D8}", rate: i)).ToList();

        var page = Assemble(facts, sort: "-rate", page: 2, pageSize: 10).Rows;

        page.Total.Should().Be(30);
        page.Items.Select(r => r.Rate).Should().Equal(Enumerable.Range(11, 10).Select(i => (decimal)(31 - i)));
    }

    [Fact]
    public void Ui_filters_narrow_groups_but_keep_the_full_list_for_hints()
    {
        var facts = new[] { Fact(pos: "24"), Fact(pos: "11", billingClass: "professional") };

        var result = Assemble(facts, setting: "office");

        result.Groups.Should().ContainSingle().Which.SettingKey.Should().Be("office");
        result.AllGroups.Should().HaveCount(2);
    }

    [Fact]
    public void Quality_counts_collapsed_duplicates_once_per_rate()
    {
        var facts = new[]
        {
            Fact(provider: "111111111", rateId: 1, occurrences: 162, pos: "24"),
            Fact(provider: "222222222", rateId: 1, occurrences: 162, pos: "24"),
            Fact(provider: "333333333", rateId: 2, occurrences: 2, pos: "24"),
        };

        var quality = Assemble(facts).Quality;

        quality.UniqueRates.Should().Be(2);
        quality.PublishedRows.Should().Be(164);
        quality.DuplicatesCollapsed.Should().Be(162);
    }

    private static AssembledSearch Assemble(
        IEnumerable<RateFactRow> facts, bool providerFilterActive = false, string sort = "rate", int page = 1, int pageSize = 25,
        string? setting = null) =>
        RateSearchAssembler.Assemble(
            facts.ToList(),
            new AssemblyOptions(null, setting, null, null, null, providerFilterActive, sort, page, pageSize),
            Catalog);

    private static RateFactRow Fact(
        string provider = "111111111",
        decimal rate = 100m,
        long? rateId = null,
        string code = "45378",
        string billingClass = "institutional",
        string pos = "24",
        string modifiers = "",
        bool matched = true,
        int occurrences = 1,
        string? ein = null,
        bool einUnverified = false,
        int npis = 1,
        string? names = null,
        string? matchedNames = null,
        bool wholeTaxId = false) =>
        new(
            RateId: rateId ?? Interlocked.Increment(ref _nextRateId) + 1000,
            ProductKey: "ambetter",
            ProductLabel: "Ambetter",
            CodeType: "CPT",
            Code: code,
            CodeSource: "billing_code",
            BillingCodeType: "CPT",
            BillingCode: code,
            BillingClass: billingClass,
            NegotiatedType: "negotiated",
            Modifiers: modifiers,
            Rate: rate,
            Occurrences: occurrences,
            Pos: pos,
            ProviderKey: provider,
            Ein: ein ?? (provider.Length == 9 ? provider : null),
            EinUnverified: einUnverified,
            EinLevel: wholeTaxId,
            BusinessNames: names ?? $"Provider {provider}",
            NpiCount: npis,
            FilterMatch: matched,
            IdMatch: matched,
            MatchedBusinessNames: matched ? matchedNames ?? names ?? $"Provider {provider}" : null);
}
