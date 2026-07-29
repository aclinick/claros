namespace Claros.Internal;

/// <summary>
/// Detects concurrent use of an operation that must not run twice at once.
/// </summary>
/// <remarks>
/// Several engines here wrap a native runtime that is not thread safe, and until
/// now that requirement was only documented. This gate turns a violation into an
/// immediate, explanatory <see cref="InvalidOperationException"/> instead of a
/// race inside native code, where the symptom is a crash or corrupted audio far
/// from the cause. It deliberately fails rather than queueing: silently
/// serializing would hide the caller's bug and can deadlock when the second call
/// is made from a callback of the first.
/// </remarks>
internal sealed class SingleFlightGate
{
    private int _busy;

    /// <summary>
    /// Marks the operation as in flight, throwing if it already was.
    /// </summary>
    public void Enter(string owner, string operation)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                $"{owner} is already running {operation}. This type is thread hostile: it drives " +
                "a native engine that is not safe to call concurrently. Use one instance per " +
                "caller, or serialize your calls.");
        }
    }

    /// <summary>Marks the operation as finished. Always call from a finally.</summary>
    public void Exit() => Volatile.Write(ref _busy, 0);
}
