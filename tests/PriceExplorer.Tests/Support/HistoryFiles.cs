using static PriceExplorer.Tests.Support.TicFileBuilder;

namespace PriceExplorer.Tests.Support;

/// <summary>
/// Three small synthetic monthly snapshots (2026-07-27, 2026-08-31, 2026-09-28) of the same two products, so history can
/// be tested without the network. Between August and September (all in surgery-center facility fees for CPT 45378
/// unless said otherwise):
/// <list type="bullet">
/// <item>Alpha +10% (600 → 660), Gamma +2% (450 → 459), the masked County clinic −10% (500 → 450), Beta unchanged.</item>
/// <item>Omega is new; Sigma is dropped; Delta moves to a hospital outpatient department (dropped there, new here).</item>
/// <item>A telehealth group with two NPIs on CPT 99213: one NPI 45 → 50, the other 90 both months.</item>
/// <item>Essential Plan: Alpha 610 → 620. Its July file is a partial upload (one rate).</item>
/// <item>The payer renumbers provider_group_id in September, as Centene does every month.</item>
/// </list>
/// July's Ambetter file equals August's.
/// </summary>
internal static class HistoryFiles
{
    public const string July = "2026-07-27";
    public const string August = "2026-08-31";
    public const string September = "2026-09-28";

    public const string Ambetter = "ambetter-from-fidelis-care";
    public const string Essential = "essential-plan";
    public const string AmbetterColonoscopyAsc = "ambetter-from-fidelis-care|CPT|45378|facility|asc||negotiated";
    public const string AmbetterColonoscopyHospital = "ambetter-from-fidelis-care|CPT|45378|facility|hospital-outpatient||negotiated";
    public const string AmbetterOfficeVisit = "ambetter-from-fidelis-care|CPT|99213|professional|office||negotiated";
    public const string EssentialColonoscopyAsc = "essential-plan|CPT|45378|facility|asc||negotiated";

    public static readonly Provider Alpha = new("Alpha Surgery Center", "22-1111111", 1234567893);
    public static readonly Provider Beta = new("Beta Surgery Center", "33-2222222", 1245319599);
    public static readonly Provider Gamma = new("Gamma Endoscopy", "44-3333333", 1386622339);
    public static readonly Provider Delta = new("Delta Digestive Care", "55-4444444", 1255555555);

    /// <summary>A prefix the IRS never issues, so its key is the per-install HMAC, not the number.</summary>
    public static readonly Provider County = new("County Health Clinic", "07-1234567", 1266666666);
    public static readonly Provider Omega = new("Omega Surgical Suites", "66-5555555", 1277777777);
    public static readonly Provider Sigma = new("Sigma Surgery Center", "77-6666666", 1288888888);

    /// <summary>One tax ID, two clinicians paid differently (like Teladoc's 99213).</summary>
    public static readonly Provider TeleFirst = new("Telehealth Partners", "88-7777777", 1299999991);
    public static readonly Provider TeleSecond = new("Telehealth Partners", "88-7777777", 1299999992);

    public static IReadOnlyList<string> Indexes => [IndexUrl(July), IndexUrl(August), IndexUrl(September)];

    public static string IndexUrl(string date) => $"{FixtureHttpHandler.BaseUrl}{date}_history_index.json";

    public static string AmbetterUrl(string date) => $"{FixtureHttpHandler.BaseUrl}{date}_history-ex_in-network.json";

    public static string EssentialUrl(string date) => $"{FixtureHttpHandler.BaseUrl}{date}_history-es_in-network.json";

    /// <summary>Serves all three snapshots' indexes and rate files.</summary>
    public static void Serve(FixtureHttpHandler http)
    {
        foreach (var date in new[] { July, August, September })
        {
            http.ServeJson(IndexUrl(date), Index(date));
            http.ServeJson(AmbetterUrl(date), RateFile(date, AmbetterRates(date)));
            http.ServeJson(EssentialUrl(date), RateFile(date, EssentialRates(date)));
        }
    }

    /// <summary>A factory loading the three snapshots (and nothing else).</summary>
    public static ExplorerApiFactory Factory(
        Action<FixtureHttpHandler>? configureHttp = null, string? dataDirectory = null, IReadOnlyDictionary<string, string?>? settings = null) =>
        ExplorerApiFactory.With(http =>
        {
            Serve(http);
            configureHttp?.Invoke(http);
        }, dataDirectory, settings, Indexes);

    public static IReadOnlyList<Rate> AmbetterRates(string date) => date == September
        ?
        [
            new(Alpha, "45378", "660.00"), new(Beta, "45378", "900.00"), new(Gamma, "45378", "459.00"), new(County, "45378", "450.00"),
            new(Omega, "45378", "750.00"), new(Delta, "45378", "700.00", Pos: "22"),
            new(TeleFirst, "99213", "50.00", "11", "professional"), new(TeleSecond, "99213", "90.00", "11", "professional"),
            new(Alpha, "99213", "100.00", "11", "professional"),
        ]
        :
        [
            new(Alpha, "45378", "600.00"), new(Beta, "45378", "900.00"), new(Gamma, "45378", "450.00"), new(County, "45378", "500.00"),
            new(Sigma, "45378", "800.00"), new(Delta, "45378", "700.00"),
            new(TeleFirst, "99213", "45.00", "11", "professional"), new(TeleSecond, "99213", "90.00", "11", "professional"),
            new(Alpha, "99213", "100.00", "11", "professional"),
        ];

    public static IReadOnlyList<Rate> EssentialRates(string date) => date == July
        ? [new(Alpha, "45378", "610.00")]
        :
        [
            new(Alpha, "45378", date == September ? "620.00" : "610.00"), new(Beta, "45378", "905.00"), new(Gamma, "45378", "455.00"),
            new(Delta, "45378", "705.00"), new(County, "45378", "505.00"),
            new(Alpha, "99213", "102.00", "11", "professional"), new(Beta, "99213", "98.00", "11", "professional"),
        ];

    private static string Index(string date) => $$"""
        {
          "reporting_entity_name": "Centene Management Company LLC",
          "reporting_entity_type": "Third Party Administrator",
          "last_updated_on": "{{date}}",
          "version": "2.0.0",
          "reporting_structure": [
            {
              "reporting_plans": [
                { "plan_name": "Ambetter from Fidelis Care Bronze", "plan_id_type": "hios", "plan_id": "25303NY005", "plan_market_type": "individual", "issuer_name": "Fidelis Care" },
                { "plan_name": "Ambetter from Fidelis Care Gold", "plan_id_type": "hios", "plan_id": "25303NY017", "plan_market_type": "individual", "issuer_name": "Fidelis Care" }
              ],
              "in_network_files": [ { "description": "in-network file", "location": "{{AmbetterUrl(date)}}" } ]
            },
            {
              "reporting_plans": [
                { "plan_name": "Essential Plan 1", "plan_id_type": "hios", "plan_id": "25303EP000", "plan_market_type": "individual", "issuer_name": "Fidelis Care" },
                { "plan_name": "Essential Plan 4", "plan_id_type": "hios", "plan_id": "25303EP000", "plan_market_type": "individual", "issuer_name": "Fidelis Care" }
              ],
              "in_network_files": [ { "description": "in-network file", "location": "{{EssentialUrl(date)}}" } ]
            }
          ]
        }
        """;

    private static string RateFile(string date, IReadOnlyList<Rate> rates)
    {
        // Group ids are local to a file and renumbered each month: September's don't match earlier months'.
        var firstId = date == September ? 101 : 1;
        var providers = rates.Select(rate => rate.Provider).Distinct().ToList();
        var ids = providers.Select((provider, index) => (provider, id: firstId + index)).ToDictionary(pair => pair.provider, pair => pair.id);

        var references = providers.Select(provider => $$"""
            { "provider_group_id": {{ids[provider]}}, "network_name": [], "provider_groups": [
              { "npi": [{{provider.Npi}}], "tin": { "type": "ein", "value": "{{provider.Ein}}", "business_name": "{{provider.Name}}" } } ] }
            """);
        var items = rates.Select(rate => Item(
            "CPT", rate.Code, rate.Code == "45378" ? "COLONOSCOPY" : "OFFICE VISIT", $"[{ids[rate.Provider]}]",
            Price(rate: rate.Amount, serviceCode: $"[\"{rate.Pos}\"]", billingClass: rate.BillingClass)));
        return File(references, items);
    }

    internal sealed record Provider(string Name, string Ein, long Npi)
    {
        /// <summary>Its provider key when the EIN is a real one (masked keys depend on the install's secret).</summary>
        public string Key => Ein.Replace("-", "", StringComparison.Ordinal);
    }

    internal sealed record Rate(Provider Provider, string Code, string Amount, string Pos = "24", string BillingClass = "institutional");
}
