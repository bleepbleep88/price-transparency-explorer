namespace PriceExplorer.Api.Core.Ingestion.Tic;

// CMS Transparency in Coverage schemas (v2.x, tolerant of 1.x shapes). Only the fields the explorer uses are mapped;
// unknown fields are ignored. Property names map to snake_case JSON keys. Array elements are nullable because
// a bare null inside an array is valid JSON; the readers count it instead of failing.

// ---------- Table of contents (index) file ----------

public sealed class TableOfContentsFile
{
    public string? ReportingEntityName { get; set; }
    public string? ReportingEntityType { get; set; }
    public string? LastUpdatedOn { get; set; }
    public string? Version { get; set; }
    public List<ReportingStructure?>? ReportingStructure { get; set; }
}

public sealed class ReportingStructure
{
    public List<ReportingPlan?>? ReportingPlans { get; set; }
    public List<FileLocation?>? InNetworkFiles { get; set; }
    public FileLocation? AllowedAmountFile { get; set; }
}

public sealed class ReportingPlan
{
    public string? PlanName { get; set; }
    public string? PlanIdType { get; set; }
    public string? PlanId { get; set; }
    public string? PlanMarketType { get; set; }
    public string? IssuerName { get; set; }
    public string? PlanSponsorName { get; set; }
}

public sealed class FileLocation
{
    public string? Description { get; set; }
    public string? Location { get; set; }
}

// ---------- In-network rate file ----------

/// <summary>
/// Root of an in-network file. The two big arrays are "sinks": the serializer hands each element to the
/// builder as soon as it is parsed, so memory stays flat no matter how large the file is.
/// </summary>
public sealed class InNetworkFileRoot
{
    public string? ReportingEntityName { get; set; }
    public string? ReportingEntityType { get; set; }
    public string? LastUpdatedOn { get; set; }
    public string? Version { get; set; }
    public ProviderReferenceSink? ProviderReferences { get; set; }
    public InNetworkItemSink? InNetwork { get; set; }
}

public sealed class ProviderReference
{
    public int? ProviderGroupId { get; set; }
    public List<string?>? NetworkName { get; set; }
    public List<ProviderGroup?>? ProviderGroups { get; set; }

    /// <summary>Schema 1.x pointed to external provider files. Not fetched; counted and reported.</summary>
    public string? Location { get; set; }
}

public sealed class ProviderGroup
{
    public List<long?>? Npi { get; set; }
    public TaxpayerId? Tin { get; set; }
}

public sealed class TaxpayerId
{
    public string? Type { get; set; }
    public string? Value { get; set; }
    public string? BusinessName { get; set; }
}

public sealed class InNetworkItem
{
    public string? NegotiationArrangement { get; set; }
    public string? Name { get; set; }
    public string? BillingCodeType { get; set; }
    public string? BillingCodeTypeVersion { get; set; }
    public string? BillingCode { get; set; }
    public string? Description { get; set; }
    public List<NegotiatedRateBlock?>? NegotiatedRates { get; set; }
}

public sealed class NegotiatedRateBlock
{
    public List<int?>? ProviderReferences { get; set; }

    /// <summary>Schema 1.x allowed provider groups inline on each rate; still accepted.</summary>
    public List<ProviderGroup?>? ProviderGroups { get; set; }

    public List<NegotiatedPrice?>? NegotiatedPrices { get; set; }
}

public sealed class NegotiatedPrice
{
    public string? NegotiatedType { get; set; }
    public decimal? NegotiatedRate { get; set; }
    public string? ExpirationDate { get; set; }
    public List<string?>? ServiceCode { get; set; }
    public string? BillingClass { get; set; }
    public string? Setting { get; set; }
    public List<string?>? BillingCodeModifier { get; set; }
    public string? AdditionalInformation { get; set; }
}
