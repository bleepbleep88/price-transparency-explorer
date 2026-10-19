using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Models.Snapshots;

namespace PriceExplorer.Api.Core.Queries.Snapshots;

/// <summary>Every configured snapshot (index file), newest first, with its products' rate files and load results.</summary>
public sealed record ListSnapshotsQuery : IRequest<IReadOnlyList<SnapshotSummary>>;

/// <summary>Plans covered by a snapshot, grouped by product.</summary>
public sealed record ListPlansQuery(int SnapshotId) : IRequest<SnapshotPlans>;

public sealed class ListPlansQueryValidator : AbstractValidator<ListPlansQuery>
{
    public ListPlansQueryValidator()
    {
        RuleFor(query => query.SnapshotId).GreaterThan(0).WithMessage("Snapshot id must be a positive number.");
    }
}
