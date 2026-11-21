using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Captured source + monotonically invalidated request slot. Cancels only page-owned transport;
/// detached tasks have their exceptions observed but never invoke a callback or touch a Godot node.</summary>
internal sealed class StoreRequest<T> : IDisposable
{
    private CancellationTokenSource? _cancel;
    private Task<T>? _task;
    private CatalogAccess? _source;
    private int _generation, _requested;
    public bool Pending => _task is not null;
    public void Start(CatalogAccess access, Func<CancellationToken, Task<T>> issue)
    {
        Clear(); _cancel = new(); _source = access; _requested = _generation;
        _task = issue(_cancel.Token);
    }
    public void Clear()
    {
        _generation++; _source = null; _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
        if (_task is { } stale) _ = stale.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _task = null;
    }
    public bool TryTake(CatalogAccess current, out T? result, out string? error)
    {
        result = default; error = null;
        if (_task is not { IsCompleted: true } done) return false;
        _task = null; _cancel?.Dispose(); _cancel = null;
        var cause = done.Exception?.GetBaseException();
        if (!ReferenceEquals(_source, current) || _requested != _generation) return false;
        if (done.IsCompletedSuccessfully) result = done.Result;
        else error = cause is OnlineException online ? online.Code : done.IsCanceled ? "cancelled" : "connection_failed";
        return true;
    }
    public void Dispose() => Clear();
}
