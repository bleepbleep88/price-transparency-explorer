namespace PriceExplorer.Api.Core.Ingestion.Tic;

// CMS Transparency in Coverage out-of-network allowed-amount file (schema 2.x). Same rules as TicModels.cs: only the
// fields the explorer uses, snake_case keys, nullable array elements (a bare null is counted, never fatal).

/// <summary>Root of an allowed-amount file. out_of_network is a sink, so items are handled one at a time as they stream.</summary>
public sealed class AllowedAmountFileRoot
{
    public string? ReportingEntityName { get; set; }
    public string? ReportingEntityType { get; set; }
    public string? LastUpdatedOn { get; set; }
    public string? Version { get; set; }
    public OutOfNetworkItemSink? OutOfNetwork { get; set; }
}

/// <summary>One billing code (with the name it was published under) and what the plan allowed for it out of network.</summary>
public sealed class OutOfNetworkItem
{
    public string? Name { get; set; }
    public string? BillingCodeType { get; set; }
    public string? BillingCodeTypeVersion { get; set; }
    public string? BillingCode { get; set; }
    public string? Description { get; set; }
    public List<AllowedAmountEntry?>? AllowedAmounts { get; set; }
}

/// <summary>One tax ID, place(s) of service and claim form, with every distinct amount the plan allowed it.</summary>
public sealed class AllowedAmountEntry
{
    public TaxpayerId? Tin { get; set; }
    public List<string?>? ServiceCode { get; set; }
    public string? BillingClass { get; set; }
    public List<OutOfNetworkPayment?>? Payments { get; set; }
}

public sealed class OutOfNetworkPayment
{
    public decimal? AllowedAmount { get; set; }
    public List<string?>? BillingCodeModifier { get; set; }
    public List<OutOfNetworkProvider?>? Providers { get; set; }
}

public sealed class OutOfNetworkProvider
{
    public decimal? BilledCharge { get; set; }
    public List<long?>? Npi { get; set; }
}

public sealed class OutOfNetworkItemSink(Action<OutOfNetworkItem?> onItem) : StreamingSink<OutOfNetworkItem?>(onItem);
