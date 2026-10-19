using System.IO.Compression;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.TicFileBuilder;

namespace PriceExplorer.Tests.Ingestion;

public sealed class InNetworkFileParserTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pte-parser-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Facility_rows_take_their_procedure_code_from_additional_information()
    {
        var (builder, outcome) = await ParseAsync(File(
            [Minkowitz],
            [Item("RC", "490", "KNEE ARTHROSCOPY/SURGERY", "[1]", Price(additionalInformation: "29881"))]));

        outcome.IsComplete.Should().BeTrue();
        var rate = builder.Rates.Should().ContainSingle().Subject;
        rate.Key.BillingCodeType.Should().Be("RC");
        rate.Key.BillingCode.Should().Be("0490");
        rate.BillingCodeRaw.Should().Be("490");
        rate.Key.CodeType.Should().Be("CPT");
        rate.Key.Code.Should().Be("29881");
        rate.CodeSource.Should().Be(CodeSources.AdditionalInformation);
    }

    [Fact]
    public async Task Identical_prices_published_under_different_items_collapse_into_one_rate()
    {
        var price = Price(additionalInformation: "29881");
        var (builder, _) = await ParseAsync(File(
            [Minkowitz],
            [
                Item("RC", "490", "KNEE ARTHROSCOPY/SURGERY", "[1]", price),
                Item("RC", "490", "REMOVAL OF FOREIGN BODY", "[1]", price),
            ]));

        var rate = builder.Rates.Should().ContainSingle().Subject;
        rate.Occurrences.Should().Be(2);
        rate.LabelCount.Should().Be(2);
        rate.LabelSamples.Should().BeEquivalentTo(["KNEE ARTHROSCOPY/SURGERY", "REMOVAL OF FOREIGN BODY"]);
        builder.GetStats().DuplicatesRemoved.Should().Be(1);
    }

    [Fact]
    public async Task Leading_zeros_are_restored_and_the_published_code_is_kept()
    {
        var (builder, _) = await ParseAsync(File(
            [Minkowitz],
            [Item("CPT", "812", "ANES LWR INTST SCR COLSC", "[1]", Price("7.94", "[\"24\"]", "professional"))]));

        var rate = builder.Rates.Should().ContainSingle().Subject;
        rate.Key.Code.Should().Be("00812");
        rate.BillingCodeRaw.Should().Be("812");
        builder.Descriptions.Should().ContainSingle(d => d.Code == "00812" && d.Name == "ANES LWR INTST SCR COLSC");
    }

    [Fact]
    public async Task Provider_references_may_come_after_the_rates_that_use_them()
    {
        var (builder, outcome) = await ParseAsync(File(
            [Minkowitz],
            [Item("CPT", "45378", "DIAGNOSTIC COLONOSCOPY", "[1]", Price("300.00", "[\"11\"]", "professional"))],
            referencesFirst: false));

        outcome.IsComplete.Should().BeTrue();
        builder.GetStats().DanglingReferences.Should().Be(0);
        builder.Rates.Should().ContainSingle().Which.ProviderReferences.Should().Equal(1);
    }

    [Fact]
    public async Task Numbers_written_as_strings_and_single_values_instead_of_arrays_are_accepted()
    {
        const string reference = """
            { "provider_group_id": "1", "network_name": [], "provider_groups": [
              { "npi": "1396747861", "tin": { "type": "EIN", "value": 113471596, "business_name": "Minkowitz" } } ] }
            """;
        var (builder, outcome) = await ParseAsync(File(
            [reference],
            [Item("CPT", "45378", "DIAGNOSTIC COLONOSCOPY", "1", Price("\"300.00\"", "\"11\"", "professional"))]));

        outcome.IsComplete.Should().BeTrue();
        var rate = builder.Rates.Should().ContainSingle().Subject;
        rate.Key.Rate.Should().Be(300.00m);
        rate.Places.Should().Equal("11");
        builder.Groups.Should().ContainSingle().Which.Ein.Should().Be("113471596");
        builder.GroupNpis.Should().ContainSingle().Which.Npi.Should().Be(1396747861);
    }

    [Fact]
    public async Task A_rate_that_is_not_a_number_is_counted_as_a_reject_without_stopping_the_file()
    {
        var (builder, outcome) = await ParseAsync(File(
            [Minkowitz],
            [Item("CPT", "45378", "DIAGNOSTIC COLONOSCOPY", "[1]", Price("\"N/A\""), Price("300.00"))]));

        outcome.IsComplete.Should().BeTrue();
        builder.Rates.Should().ContainSingle().Which.Key.Rate.Should().Be(300.00m);
        builder.Rejects.Should().ContainSingle(r => r.Reason == "rate_not_a_number" && r.Count == 1);
        builder.GetStats().RejectedPrices.Should().Be(1);
    }

    [Fact]
    public async Task Npi_zero_marks_a_contract_with_the_whole_tax_id()
    {
        var (builder, _) = await ParseAsync(File([CountyClinicWholeTin], [Item("CPT", "99214", "OFFICE VISIT", "[2]", Price("150.29"))]));

        var group = builder.Groups.Should().ContainSingle().Subject;
        group.EinLevel.Should().BeTrue();
        group.NpiCount.Should().Be(0);
        group.EinUnverified.Should().BeTrue("07 is a prefix the IRS never issues");
        builder.GroupNpis.Should().BeEmpty();
    }

    [Fact]
    public async Task A_truncated_file_keeps_what_was_read_and_is_reported_as_partial()
    {
        var json = File([Minkowitz], [
            Item("CPT", "45378", "DIAGNOSTIC COLONOSCOPY", "[1]", Price("300.00")),
            Item("CPT", "45380", "COLONOSCOPY AND BIOPSY", "[1]", Price("400.00")),
        ]);
        var truncated = json[..(json.LastIndexOf("45380", StringComparison.Ordinal) + 3)];

        var (builder, outcome) = await ParseAsync(truncated);

        outcome.IsComplete.Should().BeFalse();
        outcome.Problem.Should().StartWith("Stopped reading after 1 items");
        builder.Rates.Should().ContainSingle().Which.Key.Code.Should().Be("45378");
    }

    [Fact]
    public async Task Gzip_compressed_files_are_read_transparently()
    {
        var json = File([Minkowitz], [Item("CPT", "45378", "DIAGNOSTIC COLONOSCOPY", "[1]", Price("300.00"))]);

        var (builder, outcome) = await ParseAsync(json, Gzip);

        outcome.IsComplete.Should().BeTrue();
        builder.Rates.Should().ContainSingle();
    }

    [Fact]
    public async Task A_utf8_byte_order_mark_is_skipped()
    {
        var json = File([Minkowitz], [Item("CPT", "45378", "DIAGNOSTIC COLONOSCOPY", "[1]", Price("300.00"))]);

        var (builder, outcome) = await ParseAsync(json, bytes => [0xEF, 0xBB, 0xBF, .. bytes]);

        outcome.IsComplete.Should().BeTrue();
        builder.Rates.Should().ContainSingle();
    }

    [Fact]
    public async Task Provider_groups_written_inline_on_a_rate_are_supported()
    {
        const string item = """
            { "negotiation_arrangement": "ffs", "name": "DIAGNOSTIC COLONOSCOPY", "billing_code_type": "CPT", "billing_code": "45378",
              "negotiated_rates": [ { "provider_groups": [ { "npi": [1396747861], "tin": { "type": "ein", "value": "11-3471596" } } ],
                "negotiated_prices": [ { "negotiated_type": "negotiated", "negotiated_rate": 300, "expiration_date": "9999-12-31",
                  "service_code": ["11"], "billing_class": "professional" } ] } ] }
            """;

        var (builder, outcome) = await ParseAsync(File([], [item]));

        outcome.IsComplete.Should().BeTrue();
        var rate = builder.Rates.Should().ContainSingle().Subject;
        rate.ProviderReferences.Should().ContainSingle().Which.Should().BeNegative();
        builder.Groups.Should().ContainSingle().Which.Ein.Should().Be("113471596");
        builder.GetStats().DanglingReferences.Should().Be(0);
    }

    [Fact]
    public async Task The_real_ambetter_slice_matches_an_independent_count()
    {
        var builder = new InNetworkFileBuilder();
        var outcome = await InNetworkFileParser.ParseAsync(
            TestFiles.Fixture("2026-08-31_fidelis-ex_in-network.json"), builder, null, TestContext.Current.CancellationToken);

        outcome.IsComplete.Should().BeTrue();
        var stats = builder.GetStats();
        stats.Items.Should().Be(7);
        stats.PricesRaw.Should().Be(27);
        stats.RatesUnique.Should().Be(23);
        stats.DuplicatesRemoved.Should().Be(4);
        stats.ProviderReferences.Should().Be(25);
        stats.ProviderGroups.Should().Be(768);
        stats.DistinctEins.Should().Be(764);
        stats.DistinctNpis.Should().Be(3169);
        stats.EinLevelGroups.Should().Be(2);
        stats.UnverifiedEinGroups.Should().Be(67);
        stats.InvalidNpis.Should().Be(1);
        builder.LastUpdatedOn.Should().Be(new DateOnly(2026, 9, 2));

        var kneeScope = builder.Rates.Where(r => r.Key.Code == "29881").ToList();
        kneeScope.Should().HaveCount(2);
        kneeScope.Should().AllSatisfy(r =>
        {
            r.Key.BillingCode.Should().Be("0490");
            r.CodeSource.Should().Be(CodeSources.AdditionalInformation);
            r.Occurrences.Should().Be(2);
        });
    }

    private async Task<(InNetworkFileBuilder Builder, ParseOutcome Outcome)> ParseAsync(string json, Func<byte[], byte[]>? transform = null)
    {
        var bytes = Utf8(json);
        if (transform is not null)
        {
            bytes = transform(bytes);
        }

        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.json");
        await System.IO.File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);

        var builder = new InNetworkFileBuilder();
        var outcome = await InNetworkFileParser.ParseAsync(path, builder, null, TestContext.Current.CancellationToken);
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
