using System.Globalization;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.TicFileBuilder;

namespace PriceExplorer.Tests.Insights;

/// <summary>The real API with both rate files replaced by the synthetic ones in <see cref="InsightFiles"/>.</summary>
public sealed class InsightsApiFactory() : ExplorerApiFactory(
    http =>
    {
        http.ServeJson(FixtureHttpHandler.AmbetterUrl, InsightFiles.AmbetterFile);
        http.ServeJson(FixtureHttpHandler.EssentialUrl, InsightFiles.EssentialFile);
    },
    null,
    null);

/// <summary>
/// Small synthetic rate files for both products, with numbers chosen so every insight can be worked out by hand
/// (the expected values in the tests are those hand calculations). Providers A01–A12 hold EINs 21-0000001 to
/// 21-0000012; M holds a masked tax ID (prefix 07, never issued); T is keyed by an NPI-type tax ID.
/// </summary>
internal static class InsightFiles
{
    public const string Ambetter = "ambetter-from-fidelis-care";
    public const string Essential = "essential-plan";
    public const string MaskedEin = "071234567";
    public const string TypedKey = "npi:1234567893";

    public static string Key(int provider) => $"2100000{provider:00}";

    public static string GroupKey(string product, string codeType, string code, string feeType, string setting) =>
        $"{product}|{codeType}|{code}|{feeType}|{setting}||negotiated";

    // Ambetter:
    //   CPT 99213 office, 12 providers:   80 90 100 100 100 110 120 130 140 150 160 200 → p25 100, median 115, gap 15
    //   CPT 45378 under RC 0490 at a surgery center, 10 providers: 400 450 500 550 600 650 700 800 900 1200
    //                                     → p10 445, p25 512.50, median 625, p90 930, gap 112.50, p90/p10 2.09
    //   HCPCS J0585 office, 10 providers: 1 2 3 4 5 6 7 8 9 30 → p10 1.90, p25 3.25, median 5.50, p90 11.10, p90/p10 5.84
    //   CPT 99214 office: A01, A02, T (a list group); J0585 hospital outpatient, billed per unit: A01 at 10
    //   RC 0360 billed directly (no procedure named) at hospital outpatient departments, hospitals H30–H39:
    //                                     1000 1100 … 1900 → p10 1090, p25 1225, median 1450, p90 1810, gap 225
    //   CPT 99215 office, H30–H39: 100 ×6, 104 ×4 → p25 = median = 100: no gap, so never ranked; H36 at 104 is "high"
    //                                     but within 5% of typical
    //   CPT 86235 at a federally qualified health center, H31–H35: 25 each (paid by visit rules the files don't explain)
    public static readonly string AmbetterFile = File(
        [
            .. Enumerable.Range(1, 12).Select(provider => Reference(provider, "ein", Key(provider), Name(provider), provider switch
            {
                2 => [0],
                3 => [Npi(3), Npi(203)],
                _ => [Npi(provider)],
            })),
            Reference(13, "npi", "1234567893", "Typed Tax ID Telehealth", Npi(13)),
            .. Enumerable.Range(30, 10).Select(hospital => Reference(hospital, "ein", Key(hospital), $"Hospital H{hospital}", Npi(hospital))),
        ],
        [
            .. Rates("CPT", "99213", Office, (1, 80), (2, 90), (3, 100), (4, 100), (5, 100), (6, 110), (7, 120), (8, 130), (9, 140), (10, 150), (11, 160), (12, 200)),
            .. Rates("RC", "0490", rate => SurgeryCenter(rate, "45378"), (1, 400), (2, 450), (3, 500), (4, 550), (5, 600), (6, 650), (7, 700), (8, 800), (9, 900), (10, 1200)),
            .. Rates("HCPCS", "J0585", Office, (1, 1), (2, 2), (3, 3), (4, 4), (5, 5), (6, 6), (7, 7), (8, 8), (9, 9), (10, 30)),
            .. Rates("CPT", "99214", Office, (1, 150), (2, 160), (13, 170)),
            .. Rates("HCPCS", "J0585", rate => HospitalOutpatient(rate), (1, 10)),
            .. Rates("RC", "0360", rate => HospitalOutpatient(rate), [.. Enumerable.Range(0, 10).Select(i => (30 + i, 1000m + (100m * i)))]),
            .. Rates("CPT", "99215", Office, [.. Enumerable.Range(0, 10).Select(i => (30 + i, i < 6 ? 100m : 104m))]),
            .. Rates("CPT", "86235", HealthCenter, [.. Enumerable.Range(31, 5).Select(hospital => (hospital, 25m))]),
        ]);

    // Essential Plan:
    //   CPT 99213 office, A01–A10 and M: 80 90 100 90 50 121 120 260 140 147, M 100
    //                                     → p10 80, p25 90, median 100, p75 130.50, p90 147, gap 10, p90/p10 1.84
    //     against Ambetter: same ×5 (A01 A02 A03 A07 A09), A04 0.9, A05 0.5, A06 1.1, A08 2.0, A10 0.98
    //   CPT 45378 under RC 0490 at a surgery center, A01–A05: the same rates as Ambetter
    //   J0585 named in an RC 0490 hospital outpatient line for A01: a facility charge, so not comparable with Ambetter's unit price
    //   RC 0360 billed directly for H30 at 1100 (Ambetter 1000): a pair of facility charges with no procedure named
    //   CPT 86235 at a federally qualified health center: H31 at 1.50, H32–H35 at 25 (Ambetter 25): 5 health-center pairs
    public static readonly string EssentialFile = File(
        [
            .. Enumerable.Range(1, 10).Select(provider =>
                Reference(provider, "ein", Key(provider), Name(provider), Npi(provider), Npi(100 + provider))),
            Reference(20, "ein", MaskedEin, "Masked Solo Practice", Npi(300)),
            .. Enumerable.Range(30, 6).Select(hospital => Reference(hospital, "ein", Key(hospital), $"Hospital H{hospital}", Npi(hospital))),
        ],
        [
            .. Rates("CPT", "99213", Office, (1, 80), (2, 90), (3, 100), (4, 90), (5, 50), (6, 121), (7, 120), (8, 260), (9, 140), (10, 147), (20, 100)),
            .. Rates("RC", "0490", rate => SurgeryCenter(rate, "45378"), (1, 400), (2, 450), (3, 500), (4, 550), (5, 600)),
            .. Rates("RC", "0490", rate => HospitalOutpatient(rate, "J0585"), (1, 550)),
            .. Rates("RC", "0360", rate => HospitalOutpatient(rate), (30, 1100)),
            .. Rates("CPT", "86235", HealthCenter, (31, 1.50m), (32, 25), (33, 25), (34, 25), (35, 25)),
        ]);

    public static string Name(int provider) => $"Clinic A{provider:00}";

    private static string Office(decimal rate) => Price(rate: Text(rate), serviceCode: "[\"11\"]", billingClass: "professional");

    private static string SurgeryCenter(decimal rate, string code) =>
        Price(rate: Text(rate), serviceCode: "[\"24\"]", billingClass: "institutional", additionalInformation: code);

    private static string HealthCenter(decimal rate) => Price(rate: Text(rate), serviceCode: "[\"50\"]", billingClass: "institutional");

    private static string HospitalOutpatient(decimal rate, string? code = null) =>
        Price(rate: Text(rate), serviceCode: "[\"22\"]", billingClass: "institutional", additionalInformation: code);

    private static IEnumerable<string> Rates(string codeType, string code, Func<decimal, string> price, params (int Reference, decimal Rate)[] rates) =>
        rates.Select(rate => Item(codeType, code, $"{codeType} {code}", $"[{rate.Reference}]", price(rate.Rate)));

    private static string Reference(int id, string tinType, string tinValue, string name, params long[] npis) =>
        $$"""
        { "provider_group_id": {{id}}, "network_name": [], "provider_groups": [
          { "npi": [{{string.Join(", ", npis)}}], "tin": { "type": "{{tinType}}", "value": "{{tinValue}}", "business_name": "{{name}}" } } ] }
        """;

    private static string Text(decimal rate) => rate.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A valid NPI: a 9-digit body starting with 1 plus the CMS check digit (Luhn over 80840 + the body).</summary>
    public static long Npi(int n)
    {
        var body = 100_000_000L + n;
        var (sum, doubleIt) = (24, true);
        for (var rest = body; rest > 0; rest /= 10)
        {
            var digit = (int)(rest % 10);
            if (doubleIt)
            {
                digit = digit * 2 > 9 ? (digit * 2) - 9 : digit * 2;
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return (body * 10) + ((10 - (sum % 10)) % 10);
    }
}
