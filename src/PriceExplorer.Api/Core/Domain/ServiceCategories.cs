using System.Globalization;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>What kind of service a code is, and the unit a negotiated rate for it is priced in.</summary>
public sealed record ServiceCategory(string Key, string Label, string Unit, bool PerUnit);

/// <summary>
/// Classifies codes by the standard CPT section ranges and HCPCS Level II letter groups, so each rate can be shown in
/// the unit payers and providers price it in (per procedure, per visit, per test, per HCPCS unit, per anesthesia unit).
/// </summary>
internal static class ServiceCategories
{
    public static readonly ServiceCategory FacilityRevenueCode = new("facility", "Facility rate (revenue code only)", "per facility charge (unit not stated in the file)", false);
    private static readonly ServiceCategory Anesthesia = new("anesthesia", "Anesthesia", "per anesthesia unit", true);
    private static readonly ServiceCategory Surgery = new("surgery", "Surgery & procedures", "per procedure", false);
    private static readonly ServiceCategory Imaging = new("imaging", "Imaging & radiology", "per study", false);
    private static readonly ServiceCategory Lab = new("lab", "Lab & pathology", "per test", false);
    private static readonly ServiceCategory BehavioralHealth = new("behavioral-health", "Behavioral health", "per session", false);
    private static readonly ServiceCategory Visits = new("visits", "Office, ER & hospital visits", "per visit", false);
    private static readonly ServiceCategory Medicine = new("medicine", "Medicine, therapy & tests", "per service", false);
    private static readonly ServiceCategory QualityMeasure = new("quality-measure", "Quality reporting (CPT Category II)", "per report", false);
    private static readonly ServiceCategory EmergingTech = new("emerging-tech", "Emerging technology (CPT Category III)", "per service", false);
    public const string DrugsKey = "drugs";
    private static readonly ServiceCategory Drugs = new(DrugsKey, "Drugs & biologicals", "per HCPCS unit (see the code's description)", true);
    private static readonly ServiceCategory Supplies = new("supplies", "Supplies & equipment", "per item or unit", true);
    private static readonly ServiceCategory Other = new("other", "Other services", "per service", false);

    /// <summary>Overrides the unit for settings that pay one all-inclusive amount per visit.</summary>
    public const string PerVisitAllInclusive = "per visit (all-inclusive)";

    public static ServiceCategory Classify(string codeType, string code)
    {
        if (codeType == BillingCodes.RevenueCode)
        {
            return FacilityRevenueCode;
        }

        if (codeType == BillingCodes.Hcpcs && code.Length == 5 && char.IsAsciiLetter(code[0]))
        {
            return code[0] switch
            {
                'J' => Drugs,
                'Q' => TemporaryCode(code),
                'A' or 'E' or 'K' or 'L' or 'B' => Supplies,
                'G' => new ServiceCategory("medicare-g-codes", "Procedures & services (HCPCS G-codes)", "per service", false),
                'H' or 'T' => new ServiceCategory("community-services", "Behavioral health & community services", "per service or unit", false),
                'S' => new ServiceCategory("private-payer", "Private-payer service codes", "per service", false),
                'C' => new ServiceCategory("hospital-outpatient", "Hospital outpatient items (temporary codes)", "per item", true),
                _ => Other,
            };
        }

        if (codeType != BillingCodes.Cpt || code.Length != 5)
        {
            return Other;
        }

        if (code[4] == 'F')
        {
            return QualityMeasure;
        }

        if (code[4] == 'T')
        {
            return EmergingTech;
        }

        if (code[4] is 'U' or 'M')
        {
            return Lab;
        }

        if (!int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return Other;
        }

        return number switch
        {
            <= 1999 => Anesthesia,
            >= 10004 and <= 69990 => Surgery,
            >= 70010 and <= 79999 => Imaging,
            >= 80047 and <= 89398 => Lab,
            >= 90785 and <= 90899 => BehavioralHealth,
            >= 99202 and <= 99499 => Visits,
            >= 90281 and <= 99199 => Medicine,
            _ => Other,
        };
    }

    /// <summary>
    /// HCPCS Q codes (CMS temporary codes) mix drugs, supplies and services: Q0138–Q0181 (drugs such as ferumoxytol and
    /// antiemetics), Q2xxx, Q5101 on (biosimilars) and Q9950 on (contrast and radiopharmaceuticals) are drugs; Q4001–Q4051
    /// (casting) and Q4100 on (skin substitutes) are supplies; the rest are services (Q0091 is a Pap smear collection).
    /// </summary>
    private static ServiceCategory TemporaryCode(string code)
    {
        if (!int.TryParse(code.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return Other;
        }

        return number switch
        {
            >= 138 and <= 181 => Drugs,
            >= 2000 and <= 2999 => Drugs,
            >= 4001 and <= 4051 => Supplies,
            >= 4100 and <= 4999 => Supplies,
            >= 5101 and <= 5999 => Drugs,
            >= 9950 => Drugs,
            _ => Other,
        };
    }

    /// <summary>Visit codes billed in levels, lowest first. 99201 (new patient, level 1) was deleted in 2021.</summary>
    private static readonly LevelFamilyInfo[] LevelFamilies =
    [
        new("Emergency room visit", ["99281", "99282", "99283", "99284", "99285"], FirstLevel: 1),
        new("Office visit, new patient", ["99202", "99203", "99204", "99205"], FirstLevel: 2),
        new("Office visit, established patient", ["99211", "99212", "99213", "99214", "99215"], FirstLevel: 1),
    ];

    /// <summary>Codes billed in levels (ER and office visits). Rates are only compared level to level.</summary>
    public static string? LevelFamily(string codeType, string code) => FamilyOf(codeType, code)?.Name;

    /// <summary>The level family a CPT code belongs to, with all its codes; null for other codes.</summary>
    public static LevelFamilyInfo? FamilyOf(string codeType, string code) =>
        codeType == BillingCodes.Cpt ? LevelFamilies.FirstOrDefault(family => family.Codes.Contains(code)) : null;
}

/// <summary>A family of visit codes billed in levels: "Office visit, established patient" is 99211 (level 1) to 99215 (level 5).</summary>
public sealed record LevelFamilyInfo(string Name, IReadOnlyList<string> Codes, int FirstLevel)
{
    public int LevelOf(string code) => FirstLevel + Codes.ToList().IndexOf(code);
}
