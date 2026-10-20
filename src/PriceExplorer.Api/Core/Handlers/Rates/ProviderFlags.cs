using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>
/// "Check this" notes for rates that may be listed for providers who don't perform the service ("zombie" rates). The
/// files carry no claims, so these are prompts to verify, never exclusions.
/// </summary>
internal static class ProviderFlags
{
    public static IReadOnlyList<RowFlag> For(RateGroup group, ProviderProfile profile)
    {
        var flags = new List<RowFlag>();
        var active = profile.NpisInDirectory - profile.Deactivated;
        if (active > 0 && group.FeeType == "facility" && profile.Individuals == active)
        {
            flags.Add(new RowFlag("facility_rate_individual_npis",
                "A facility rate, but the national registry lists every NPI behind it as an individual clinician, not a facility."));
        }

        var category = ServiceCategories.Classify(group.CodeType, group.Code);
        // "anesthe" covers both "Anesthesiology ..." physicians and "Certified Registered Nurse Anesthetist".
        if (active > 0 && group.FeeType == "professional" && category.Key == "anesthesia" && !Has(profile, "anesthe"))
        {
            flags.Add(new RowFlag("anesthesia_without_anesthesia_clinician",
                "An anesthesia rate, but no NPI behind it is an anesthesiologist or nurse anesthetist in the national registry."));
        }

        // A technical-component (TC) rate pays for the equipment and staff, billed by whoever owns the machine (an urgent
        // care center, a cardiology practice), so no reader is expected there.
        var technicalOnly = group.Modifiers.Any(modifier => modifier.Code == "TC");
        var reader = ExpectedReader(group.CodeType, group.Code);
        if (active > 0 && group.FeeType == "professional" && !technicalOnly && reader != Reader.None && profile.ImagingReaders == 0
            && (reader == Reader.Radiologist || profile.CardiacReaders == 0))
        {
            flags.Add(new RowFlag("imaging_without_radiologist", reader == Reader.Radiologist
                ? "A CT, MRI or PET read, but no NPI behind it is a radiologist or nuclear medicine physician in the national registry."
                : "A cardiac CT, MRI or PET read, but no NPI behind it is a radiologist, nuclear medicine physician or cardiologist in the national registry."));
        }

        if (profile.Deactivated > 0)
        {
            flags.Add(new RowFlag("deactivated_npis", (profile.Deactivated, profile.Npis) switch
            {
                (_, 1) => "The only NPI listed for this rate is deactivated in the national registry.",
                var (gone, all) when gone == all => $"All {all} NPIs listed for this rate are deactivated in the national registry.",
                (1, var all) => $"1 of the {all} NPIs listed for this rate is deactivated in the national registry.",
                var (gone, all) => $"{gone} of the {all} NPIs listed for this rate are deactivated in the national registry.",
            }));
        }

        return flags;
    }

    private static bool Has(ProviderProfile profile, string fragment) =>
        profile.Specialties.Any(specialty => specialty.Specialty.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private enum Reader { None, Radiologist, RadiologistOrCardiologist }

    /// <summary>
    /// Who reads a study: only CT, MRI and PET reads expect a radiologist or nuclear medicine physician (and cardiac ones a
    /// cardiologist too). Ultrasound, X-rays, imaging guidance and radiation therapy are left out: the specialists who
    /// order them (OB/GYN, ophthalmology, podiatry, radiation oncology) normally bill them.
    /// </summary>
    private static Reader ExpectedReader(string codeType, string code)
    {
        if (codeType != BillingCodes.Cpt || !int.TryParse(code, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var cpt))
        {
            return Reader.None;
        }

        return cpt switch
        {
            >= 75557 and <= 75574 or >= 78429 and <= 78434 or 78459 or 78491 or 78492 => Reader.RadiologistOrCardiologist,
            70336 or >= 70450 and <= 70498 or >= 70540 and <= 70559 or >= 71250 and <= 71275 or >= 71550 and <= 71555
                or >= 72125 and <= 72159 or >= 72191 and <= 72198 or >= 73200 and <= 73225 or >= 73700 and <= 73725
                or >= 74150 and <= 74185 or >= 74261 and <= 74263 or 76380 or 76390 or >= 77046 and <= 77049 or 77084
                or >= 78811 and <= 78816 => Reader.Radiologist,
            _ => Reader.None,
        };
    }
}
