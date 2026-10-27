namespace DynamiteUniverse.Shared.IO;

public readonly record struct RequestLoadResult<TKey, TValue>(TKey Key, TValue? Value, Exception? Error);

/// <summary>
/// Runs at most one blocking load at a time and exposes only the latest request's result.
/// Workers never call UI objects; the owner consumes results on its own thread.
/// </summary>
public sealed class LatestRequestLoader<TKey, TValue> : IDisposable
{
    private sealed record Request(TKey Key, CancellationTokenSource Cancellation);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Request? _current;
    private RequestLoadResult<TKey, TValue>? _completed;
    private int _jobs;
    private bool _disposed;

    public Task Start(TKey key, Func<TValue> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        Request request;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelCurrent();
            request = new Request(key, new CancellationTokenSource());
            _current = request;
            _jobs++;
        }
        return RunAsync(request, load);
    }

    public void Cancel()
    {
        lock (_sync)
            CancelCurrent();
    }

    public bool TryTakeCompleted(out RequestLoadResult<TKey, TValue> result)
    {
        lock (_sync)
        {
            if (_completed is not { } completed)
            {
                result = default;
                return false;
            }
            _completed = null;
            result = completed;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CancelCurrent();
            if (_jobs == 0)
                _gate.Dispose();
        }
    }

    private void CancelCurrent()
    {
        _current?.Cancellation.Cancel();
        _current = null;
        _completed = null;
    }

    private async Task RunAsync(Request request, Func<TValue> load)
    {
        var token = request.Cancellation.Token;
        TValue? value = default;
        Exception? error = null;
        try
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                value = await Task.Run(load, token).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            lock (_sync)
            {
                if (!_disposed && ReferenceEquals(_current, request) && !token.IsCancellationRequested)
                {
                    _completed = new(request.Key, value, error);
                    _current = null;
                }
                request.Cancellation.Dispose();
                if (--_jobs == 0 && _disposed)
                    _gate.Dispose();
            }
        }
    }
}
