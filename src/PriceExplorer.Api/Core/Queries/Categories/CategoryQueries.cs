using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Queries.Categories;

/// <summary>The categories of the codes with rates in a snapshot, as a tree with code and rate counts.</summary>
/// <param name="Snapshot">Snapshot id; defaults to the newest loaded snapshot.</param>
public sealed record GetCategoriesQuery(int? Snapshot = null) : IRequest<CategoryTree>, IRequiresData;

public sealed class GetCategoriesQueryValidator : AbstractValidator<GetCategoriesQuery>
{
    public GetCategoriesQueryValidator()
    {
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null).WithMessage("Snapshot id must be a positive number.");
    }
}
