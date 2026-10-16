namespace DAP.Core.Targets;

public interface ITargetResolver<TContext, TTarget>
{
    TargetRuntime Runtime { get; }

    Task<TargetResolution<TTarget>> ResolveAsync(
        TContext context,
        TargetDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
