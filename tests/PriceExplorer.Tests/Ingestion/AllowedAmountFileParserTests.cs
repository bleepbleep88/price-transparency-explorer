using System.IO.Compression;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.AllowedAmountJson;

namespace PriceExplorer.Tests.Ingestion;

public sealed class AllowedAmountFileParserTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pte-oon-parser-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("2026-08-31_fidelis-ex_allowed-amounts.json", 5, 39, 27, 17, 14, 15, 0)]
    [InlineData("2026-08-31_fidelis-es_allowed-amounts.json", 5, 68, 48, 24, 19, 24, 3)]
    public async Task The_real_slices_match_an_independent_count(
        string fixture, int items, long published, int unique, int taxIdEntries, int eins, int npis, int zerosRestored)
    {
        var builder = new AllowedAmountFileBuilder();
        var outcome = await AllowedAmountFileParser.ParseAsync(TestFiles.Fixture(fixture), builder, null, TestContext.Current.CancellationToken);

        outcome.IsComplete.Should().BeTrue();
        var stats = builder.GetStats();
        stats.Items.Should().Be(items);
        stats.PaymentsRaw.Should().Be(published);
        stats.PaymentsUnique.Should().Be(unique);
        stats.DuplicatesRemoved.Should().Be(published - unique, "every repeat is a revenue-code payment published under a second item name");
        stats.RejectedPayments.Should().Be(0);
        stats.TaxIdEntries.Should().Be(taxIdEntries);
        stats.DistinctEins.Should().Be(eins);
        stats.DistinctNpis.Should().Be(npis);
        stats.InvalidNpis.Should().Be(0);
        stats.EinsWithLeadingZeroRestored.Should().Be(zerosRestored);
        builder.Rejects.Should().BeEmpty();
        builder.LastUpdatedOn.Should().Be(new DateOnly(2026, 8, 31));
        builder.SchemaVersion.Should().Be("2.0.0");
        builder.ReportingEntityName.Should().Be("Centene Management Company LLC");
    }

    [Fact]
    public async Task The_same_payment_under_two_item_names_is_one_payment_with_both_names_as_labels()
    {
        var entry = Entry(payments: [Payment("520.64", providers: [Provider("494.00", "[1447255153]")])]);
        var (builder, _) = await ParseAsync(File([
            Item("RC", "450", "Item 1", entry),
            Item("RC", "450", "Item 2", entry),
        ]));

        var payment = builder.Payments.Should().ContainSingle().Subject;
        payment.Occurrences.Should().Be(2);
        payment.LabelCount.Should().Be(2);
        payment.LabelSamples.Should().BeEmpty("a revenue-code item's names carry CPT text, so they're counted but never kept");
        payment.Path.ToString().Should().Be("out_of_network[0].allowed_amounts[0].payments[0]");
        builder.Items.Select(item => item.Payments).Should().Equal(1, 1);
        builder.GetStats().DuplicatesRemoved.Should().Be(1);
    }

    [Fact]
    public async Task Payments_that_differ_in_amount_billed_charge_place_or_tax_id_stay_apart()
    {
        var (builder, _) = await ParseAsync(File([
            Item("CPT", "74177", "Item 3",
                Entry(serviceCode: "[\"22\", \"23\"]", billingClass: "professional", payments: [
                    Payment("91.25", providers: [Provider("246.00", "[1407814825]")]),
                    Payment("91.25", providers: [Provider("315.00", "[1407814825]")]),
                    Payment("182.50", providers: [Provider("246.00", "[1407814825]")]),
                ]),
                Entry(serviceCode: "[\"23\"]", billingClass: "professional", payments: [Payment("91.25", providers: [Provider("246.00", "[1407814825]")])]),
                Entry("""{ "type": "ein", "value": "161201211" }""", "[\"22\", \"23\"]", "professional",
                    Payment("91.25", providers: [Provider("246.00", "[1407814825]")]))),
        ]));

        builder.Payments.Should().HaveCount(5);
        builder.GetStats().DuplicatesRemoved.Should().Be(0);
    }

    [Fact]
    public async Task Codes_and_eins_get_their_leading_zeros_back_and_the_published_values_are_kept()
    {
        var (builder, _) = await ParseAsync(File([
            Item("CPT", "142", "Item 5",
                Entry("""{ "type": "ein", "value": 43431956 }""", "[\"24\"]", "professional", Payment("1700.00"))),
            Item("RC", "450", "Item 6", Entry("""{ "type": "ein", "value": "60646659" }""", "[\"23\"]")),
        ]));

        var anesthesia = builder.Payments.Single(p => p.Key.BillingCodeType == "CPT");
        anesthesia.Key.BillingCode.Should().Be("00142");
        anesthesia.BillingCodeRaw.Should().Be("142");
        anesthesia.TaxId.Value.Should().Be("43431956", "the tax ID is kept as published");
        anesthesia.TaxId.Ein.Should().Be("043431956");
        anesthesia.TaxId.EinUnverified.Should().BeFalse("04 is a prefix the IRS issues");

        var emergency = builder.Payments.Single(p => p.Key.BillingCodeType == "RC");
        emergency.Key.BillingCode.Should().Be("0450");
        emergency.TaxId.Ein.Should().Be("060646659");
        builder.Items.Select(item => (item.BillingCode, item.BillingCodeRaw)).Should().Equal(("00142", "142"), ("0450", "450"));
        builder.GetStats().EinsWithLeadingZeroRestored.Should().Be(2);
        builder.Rejects.Should().BeEmpty();
    }

    [Fact]
    public async Task A_never_issued_prefix_is_flagged_even_when_it_appears_only_after_the_zero_is_restored()
    {
        var (builder, _) = await ParseAsync(File([
            Item("CPT", "97530", "Item 7",
                Entry("""{ "type": "ein", "value": "71234567" }""", "[\"11\"]", "professional"),
                Entry("""{ "type": "ein", "value": "07-1234568" }""", "[\"11\"]", "professional")),
        ]));

        builder.Payments.Select(p => p.TaxId.Ein).Should().BeEquivalentTo(["071234567", "071234568"]);
        builder.Payments.Should().AllSatisfy(p => p.TaxId.EinUnverified.Should().BeTrue("07 is a prefix the IRS never issues"));
        builder.GetStats().UnverifiedTaxIdEntries.Should().Be(2);
    }

    [Fact]
    public async Task Numbers_written_as_strings_and_single_values_instead_of_arrays_are_accepted()
    {
        const string item = """
            { "name": "Item 8", "billing_code_type": "RC", "billing_code": 430,
              "allowed_amounts": [ { "tin": { "type": "EIN", "value": 131740114 }, "service_code": "22", "billing_class": "Institutional",
                "payments": [ { "allowed_amount": "43.24", "billing_code_modifier": "go",
                  "providers": [ { "billed_charge": "481.00", "npi": "1952476988" } ] } ] } ] }
            """;

        var (builder, outcome) = await ParseAsync(File([item]));

        outcome.IsComplete.Should().BeTrue();
        var payment = builder.Payments.Should().ContainSingle().Subject;
        payment.Key.BillingCode.Should().Be("0430");
        payment.Key.AllowedAmount.Should().Be(43.24m);
        payment.Key.Places.Should().Be("22");
        payment.Key.BillingClass.Should().Be("institutional");
        payment.Key.Modifiers.Should().Be("GO");
        payment.TaxId.Ein.Should().Be("131740114");
        var provider = payment.Providers.Should().ContainSingle().Subject;
        provider.BilledCharge.Should().Be(481.00m);
        provider.Npis.Should().ContainSingle().Which.Npi.Should().Be(1952476988);
    }

    [Fact]
    public async Task Bad_or_missing_values_are_counted_as_rejects_without_stopping_the_file()
    {
        const string noTaxId = """{ "service_code": ["22"], "billing_class": "institutional", "payments": [ { "allowed_amount": 10, "providers": [] } ] }""";
        const string noPayments = """{ "tin": { "type": "ein", "value": "131740114" }, "service_code": ["22"], "billing_class": "institutional", "payments": [] }""";
        const string noCode = """{ "name": "MYSTERY", "billing_code_type": "CPT", "allowed_amounts": [ { "tin": { "type": "ein", "value": "131740114" }, "payments": [ { "allowed_amount": 5, "providers": [] }, { "allowed_amount": 6, "providers": [] } ] } ] }""";
        const string noAllowedAmounts = """{ "name": "EMPTY", "billing_code_type": "CPT", "billing_code": "99213", "allowed_amounts": [] }""";

        var (builder, outcome) = await ParseAsync(File([
            Item("CPT", "74177", "Item 3",
                Entry(payments: [
                    Payment("\"N/A\""),
                    Payment("0"),
                    Payment("217.92", providers: [Provider("\"N/A\"", "[\"abc\", 1255962783]")]),
                    """{ "allowed_amount": 544.80 }""",
                ]),
                noTaxId,
                noPayments,
                Entry("""{ "type": "ein", "value": "12-345" }""", payments: [Payment("762.72")])),
            noCode,
            noAllowedAmounts,
        ]));

        outcome.IsComplete.Should().BeTrue();
        builder.Payments.Select(p => p.Key.AllowedAmount).Should().BeEquivalentTo([217.92m, 544.80m, 762.72m]);
        var counts = builder.Rejects.ToDictionary(r => r.Reason, r => r.Count);
        counts.Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["allowed_amount_not_a_number"] = 1,
            ["allowed_amount_not_positive"] = 1,
            ["billed_charge_not_a_number"] = 1,
            ["npi_not_a_number"] = 1,
            ["payment_without_providers"] = 1,
            ["allowed_amount_without_tax_id"] = 1,
            ["allowed_amount_without_payments"] = 1,
            ["ein_not_nine_digits"] = 1,
            ["item_missing_billing_code"] = 2,
            ["item_without_allowed_amounts"] = 1,
        });

        // Kept with what's missing left empty: the billed charge that wasn't a number, the NPIs nobody listed.
        var kept = builder.Payments.Single(p => p.Key.AllowedAmount == 217.92m).Providers.Single();
        kept.BilledCharge.Should().BeNull();
        kept.Npis.Should().ContainSingle().Which.Npi.Should().Be(1255962783);
        builder.Payments.Single(p => p.Key.AllowedAmount == 544.80m).Providers.Should().BeEmpty();
        builder.Payments.Single(p => p.Key.AllowedAmount == 762.72m).TaxId.Should().Match<TaxIdentity>(t => t.Ein == null && t.Value == "12-345");

        var stats = builder.GetStats();
        stats.PaymentsRaw.Should().Be(8, "4 under the first tax ID, 1 without a tax ID, 1 under the short EIN, 2 under the item with no code");
        stats.RejectedPayments.Should().Be(5);
        stats.PaymentsUnique.Should().Be(3);
        stats.DuplicatesRemoved.Should().Be(0);
    }

    [Fact]
    public async Task A_bare_null_inside_any_array_is_counted_and_the_rest_of_the_file_is_kept()
    {
        var (builder, outcome) = await ParseAsync(File([
            "null",
            Item("CPT", "74177", "Item 3",
                "null",
                Entry(payments: ["null", Payment("217.92", providers: ["null", Provider("3892.00", "[1255962783, null]")])])),
        ]));

        outcome.IsComplete.Should().BeTrue();
        var payment = builder.Payments.Should().ContainSingle().Subject;
        payment.Providers.Should().ContainSingle().Which.ProviderIndex.Should().Be(1, "the provider keeps its published position");
        builder.Rejects.Single(r => r.Reason == "null_element").Count.Should().Be(3);
        builder.Rejects.Single(r => r.Reason == "null_payment").Count.Should().Be(1);
        builder.Rejects.Single(r => r.Reason == "npi_not_a_number").Count.Should().Be(1);

        var stats = builder.GetStats();
        stats.Items.Should().Be(2);
        stats.PaymentsRaw.Should().Be(2);
        stats.RejectedPayments.Should().Be(1);
    }

    [Fact]
    public async Task Invalid_npis_are_flagged_and_npi_zero_means_none_listed()
    {
        var (builder, _) = await ParseAsync(File([
            Item("CPT", "74177", "Item 3",
                Entry(payments: [Payment("217.92", providers: [Provider("3892.00", "[1861171663, 0]"), Provider("100.00", "[0]")])])),
        ]));

        var providers = builder.Payments.Single().Providers;
        providers[0].Npis.Should().ContainSingle().Which.Should().Be(new OutOfNetworkNpi(1861171663, Valid: false));
        providers[1].Npis.Should().BeEmpty();
        builder.GetStats().InvalidNpis.Should().Be(1);
        builder.GetStats().DistinctNpis.Should().Be(1);
    }

    [Fact]
    public async Task A_truncated_file_keeps_what_was_read_and_is_reported_as_partial()
    {
        var json = File([
            Item("CPT", "74177", "Item 3", Entry(payments: [Payment("217.92")])),
            Item("CPT", "70450", "Item 4", Entry(payments: [Payment("100.00")])),
        ]);
        var truncated = json[..(json.LastIndexOf("70450", StringComparison.Ordinal) + 3)];

        var (builder, outcome) = await ParseAsync(truncated);

        outcome.IsComplete.Should().BeFalse();
        outcome.Problem.Should().StartWith("Stopped reading after 1 items");
        builder.Payments.Should().ContainSingle().Which.Key.BillingCode.Should().Be("74177");
    }

    [Fact]
    public async Task Gzip_a_byte_order_mark_and_a_header_after_the_items_are_all_read()
    {
        var json = File([Item("CPT", "74177", "Item 3")], headerFirst: false);

        var (gzipped, gzipOutcome) = await ParseAsync(json, Gzip);
        var (withBom, bomOutcome) = await ParseAsync(json, bytes => [0xEF, 0xBB, 0xBF, .. bytes]);

        gzipOutcome.IsComplete.Should().BeTrue();
        bomOutcome.IsComplete.Should().BeTrue();
        gzipped.Payments.Should().ContainSingle();
        withBom.Payments.Should().ContainSingle();
        withBom.LastUpdatedOn.Should().Be(new DateOnly(2026, 8, 31), "the header is read wherever it sits");
    }

    [Fact]
    public async Task A_file_that_is_not_an_object_is_reported_as_partial()
    {
        var (builder, outcome) = await ParseAsync("[]");

        outcome.IsComplete.Should().BeFalse();
        builder.Payments.Should().BeEmpty();
    }

    private async Task<(AllowedAmountFileBuilder Builder, ParseOutcome Outcome)> ParseAsync(string json, Func<byte[], byte[]>? transform = null)
    {
        var bytes = TicFileBuilder.Utf8(json);
        if (transform is not null)
        {
            bytes = transform(bytes);
        }

        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.json");
        await System.IO.File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);

        var builder = new AllowedAmountFileBuilder();
        var outcome = await AllowedAmountFileParser.ParseAsync(path, builder, null, TestContext.Current.CancellationToken);
        return (builder, outcome);
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }
}
