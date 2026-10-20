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
        // care center, a cardiology practice), so no radiologist is expected there.
        var technicalOnly = group.Modifiers.Any(modifier => modifier.Code == "TC");
        if (active > 0 && group.FeeType == "professional" && category.Key == "imaging" && !technicalOnly && !Has(profile, "radiolog"))
        {
            flags.Add(new RowFlag("imaging_without_radiologist",
                "An imaging rate, but no NPI behind it is a radiologist in the national registry."));
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
}
