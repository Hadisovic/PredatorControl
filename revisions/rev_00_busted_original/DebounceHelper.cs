using System.Collections.Concurrent;

namespace PredatorControlApp
{
    public static class DebounceHelper
    {
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();
        private static readonly ConcurrentDictionary<string, Action> _pendingActions = new();
        private static readonly object _syncLock = new();

        /// <summary>
        /// Debounces the specified action by key, ensuring it executes after the given delay in milliseconds
        /// without subsequent triggers within the window.
        /// </summary>
        public static void Debounce(string key, Action action, int delayMs = 300)
        {
            lock (_syncLock)
            {
                if (_timers.TryRemove(key, out var existingCts))
                {
                    try { existingCts.Cancel(); existingCts.Dispose(); } catch { }
                }

                _pendingActions[key] = action;

                var cts = new CancellationTokenSource();
                _timers[key] = cts;

                _ = Task.Delay(delayMs, cts.Token).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && !cts.Token.IsCancellationRequested)
                    {
                        lock (_syncLock)
                        {
                            _timers.TryRemove(key, out _);
                            if (_pendingActions.TryRemove(key, out var act))
                            {
                                try { act(); } catch { }
                            }
                        }
                    }
                }, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Flushes and executes all pending debounced actions immediately. Useful on application exit.
        /// </summary>
        public static void FlushAll()
        {
            lock (_syncLock)
            {
                foreach (var kvp in _timers)
                {
                    try { kvp.Value.Cancel(); kvp.Value.Dispose(); } catch { }
                }
                _timers.Clear();

                foreach (var kvp in _pendingActions)
                {
                    try { kvp.Value(); } catch { }
                }
                _pendingActions.Clear();
            }
        }
    }
}
