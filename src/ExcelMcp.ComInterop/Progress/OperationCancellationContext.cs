namespace Sbroenne.ExcelMcp.ComInterop;

/// <summary>
/// Carries the client cancellation token from the transport layer into synchronous
/// Core and COM operations without adding a token parameter to every command contract.
/// </summary>
public static class OperationCancellationContext
{
    private static readonly AsyncLocal<CancellationToken> CurrentValue = new();

    /// <summary>Gets the cancellation token for the current request.</summary>
    public static CancellationToken Current => CurrentValue.Value;

    /// <summary>Temporarily installs a cancellation token for the current async flow.</summary>
    public static IDisposable Push(CancellationToken cancellationToken) =>
        new Scope(cancellationToken);

    private sealed class Scope : IDisposable
    {
        private readonly CancellationToken _previous;
        private bool _disposed;

        public Scope(CancellationToken cancellationToken)
        {
            _previous = CurrentValue.Value;
            CurrentValue.Value = cancellationToken;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentValue.Value = _previous;
            _disposed = true;
        }
    }
}
