using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.History;
using PriceExplorer.Api.Core.Queries.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Queries.History;

/// <summary>
/// Negotiated rates that changed between two snapshots: the same provider (tax ID) in the same comparison group with a
/// different value, plus providers only in the newer file (new) or only in the older one (dropped). Read from the saved
/// provider values; products whose file looks partial in either snapshot are left out.
/// </summary>
/// <param name="From">Older snapshot id. Defaults to the snapshot just before <paramref name="To"/>.</param>
/// <param name="To">Newer snapshot id. Defaults to the snapshot searches default to (the newest).</param>
/// <param name="Product">Optional product key, e.g. "essential-plan".</param>
/// <param name="Code">Optional billing code, as search takes it ("45378", "490", "RC 0490").</param>
/// <param name="Kind">changed | new | dropped. Omit for all three.</param>
/// <param name="MinChangePct">Only changes of at least this many percent, up or down (changed rates only).</param>
/// <param name="Sort">largest (biggest moves first) | -changePct | changePct | code | -code | provider | -provider.</param>
public sealed record GetChangesQuery(
    int? From = null,
    int? To = null,
    string? Product = null,
    string? Code = null,
    string? Kind = null,
    decimal? MinChangePct = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 25) : IRequest<ChangesResult>, IRequiresData;

public sealed class GetChangesQueryValidator : AbstractValidator<GetChangesQuery>
{
    public static readonly string[] Kinds = ["changed", "new", "dropped"];
    public static readonly string[] Sorts = ["largest", "-changePct", "changePct", "code", "-code", "provider", "-provider"];
    public const decimal MaxChangePct = 1_000;

    public GetChangesQueryValidator()
    {
        RuleFor(query => query.From).GreaterThan(0).When(query => query.From is not null)
            .WithMessage("Snapshot id must be a positive number.");
        RuleFor(query => query.To).GreaterThan(0).When(query => query.To is not null)
            .WithMessage("Snapshot id must be a positive number.");
        RuleFor(query => query.From).NotEqual(query => query.To).When(query => query.From is not null)
            .WithMessage("Pick two different snapshots to compare.");

        RuleFor(query => query.Product)
            .Must(product => SearchRatesQueryValidator.IsProductKey(product!))
            .When(query => !string.IsNullOrWhiteSpace(query.Product))
            .WithMessage("A product is a product key from the snapshot, for example essential-plan.");

        RuleFor(query => query.Code)
            .Must(code => CodeInput.Parse(code, null).Code is { Length: > 0 and <= 8 } clean && clean.All(char.IsAsciiLetterOrDigit))
            .When(query => !string.IsNullOrWhiteSpace(query.Code))
            .WithMessage("A billing code is up to 8 letters and digits, for example 45378, J0585 or 0490.");

        RuleFor(query => query.Kind)
            .Must(kind => Kinds.Contains(kind))
            .When(query => query.Kind is not null)
            .WithMessage($"Kind must be one of: {string.Join(", ", Kinds)}.");

        RuleFor(query => query.MinChangePct)
            .InclusiveBetween(0, MaxChangePct)
            .When(query => query.MinChangePct is not null)
            .WithMessage($"Minimum change is a percentage from 0 to {MaxChangePct:N0}.");

        RuleFor(query => query.Sort)
            .Must(sort => Sorts.Contains(sort))
            .When(query => query.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", Sorts)}.");

        RuleFor(query => query.Page).InclusiveBetween(1, SearchRatesQueryValidator.MaxPage)
            .WithMessage($"Page is between 1 and {SearchRatesQueryValidator.MaxPage:N0}.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).WithMessage("Page size is between 1 and 100.");
    }
}
