namespace AgTerm.ControlServer;

public interface IControlDispatchScopeFactory
{
    IControlDispatchScope Begin();
}

public interface IControlDispatchScope : IDisposable
{
    Task CompleteAsync(CancellationToken cancellationToken);
}