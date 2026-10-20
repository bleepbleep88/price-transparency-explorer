using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.OutOfNetwork;
using PriceExplorer.Api.Core.Queries.Rates;

namespace PriceExplorer.Api.Core.Queries.OutOfNetwork;

/// <summary>What the plan allowed out of network for one billing code, per product and fee type.</summary>
/// <param name="Code">Required. CPT/HCPCS/revenue code as typed ("0450", "450", "RC 450", "74177").</param>
/// <param name="CodeType">Optional: CPT, HCPCS, RC... Needed only when a short number is ambiguous.</param>
/// <param name="Snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
public sealed record GetOutOfNetworkQuery(string? Code, string? CodeType = null, int? Snapshot = null)
    : IRequest<OutOfNetworkResult>, IRequiresData;

/// <summary>The search's code rules and messages, so a code the search takes is taken here too.</summary>
public sealed class GetOutOfNetworkQueryValidator : AbstractValidator<GetOutOfNetworkQuery>
{
    public GetOutOfNetworkQueryValidator()
    {
        RuleFor(query => query.Code)
            .Must(code => CodeInput.Clean(code).Length > 0)
            .WithMessage("Enter a billing code, for example 45378 (colonoscopy) or 0490 (ambulatory surgery).");

        RuleFor(query => query)
            .Must(query => CodeInput.Parse(query.Code, query.CodeType).Code is { Length: > 0 and <= 8 } clean && clean.All(char.IsAsciiLetterOrDigit))
            .When(query => CodeInput.Clean(query.Code).Length > 0)
            .OverridePropertyName(nameof(GetOutOfNetworkQuery.Code))
            .WithMessage("A billing code is up to 8 letters and digits, for example 45378, J0585 or 0490.");

        RuleFor(query => query.CodeType)
            .Must(type => SearchRatesQueryValidator.CodeTypes.Contains(type!.Trim().ToUpperInvariant()))
            .When(query => !string.IsNullOrWhiteSpace(query.CodeType))
            .WithMessage($"Code type must be one of: {string.Join(", ", SearchRatesQueryValidator.CodeTypes)}.");

        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null)
            .WithMessage("Snapshot id must be a positive number.");
    }
}
