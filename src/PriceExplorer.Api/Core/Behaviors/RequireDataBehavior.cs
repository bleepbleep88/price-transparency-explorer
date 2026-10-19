using MediatR;
using PriceExplorer.Api.Core.Exceptions;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Api.Core.Behaviors;

/// <summary>Marks a request that needs loaded rate data. Until data is available it fails fast with 503 + Retry-After.</summary>
public interface IRequiresData;

public sealed class RequireDataBehavior<TRequest, TResponse>(IngestionStatus status) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IRequiresData && !status.Current.DataAvailable)
        {
            throw new IndexNotReadyException($"Rate data is still loading. {status.Current.Message}");
        }

        return next(cancellationToken);
    }
}
