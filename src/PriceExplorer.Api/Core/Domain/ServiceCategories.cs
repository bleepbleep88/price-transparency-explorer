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
    private static readonly ServiceCategory Drugs = new("drugs", "Drugs & biologicals", "per HCPCS unit (see the code's description)", true);
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
                'J' or 'Q' => Drugs,
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

    /// <summary>Codes billed in levels (ER and office visits). Rates are only compared level to level.</summary>
    public static string? LevelFamily(string codeType, string code) => (codeType, code) switch
    {
        (BillingCodes.Cpt, "99281" or "99282" or "99283" or "99284" or "99285") => "Emergency room visit",
        (BillingCodes.Cpt, "99202" or "99203" or "99204" or "99205") => "Office visit, new patient",
        (BillingCodes.Cpt, "99211" or "99212" or "99213" or "99214" or "99215") => "Office visit, established patient",
        _ => null,
    };
}
