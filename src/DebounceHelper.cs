namespace PredatorControlApp
{
    public static class DebounceHelper
    {
        private sealed class Entry
        {
            public readonly CancellationTokenSource Cts = new();
            public readonly Action Action;
            public Entry(Action action) { Action = action; }
        }

        private static readonly Dictionary<string, Entry> _entries = new();
        private static readonly object _syncLock = new();

        /// <summary>
        /// Debounces the specified action by key, ensuring it executes after the given delay in milliseconds
        /// without subsequent triggers within the window.
        /// </summary>
        public static void Debounce(string key, Action action, int delayMs = 300)
        {
            var entry = new Entry(action);
            // Capture the token before publishing the entry: a later Debounce() call may dispose the CTS.
            var token = entry.Cts.Token;
            Entry? previous;

            lock (_syncLock)
            {
                _entries.TryGetValue(key, out previous);
                _entries[key] = entry;
            }

            if (previous != null)
                CancelAndDispose(previous);

            _ = Task.Delay(delayMs, token).ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully) return;

                Action? toRun = null;
                lock (_syncLock)
                {
                    // Only fire if this exact request is still the current one for the key.
                    if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    {
                        _entries.Remove(key);
                        toRun = entry.Action;
                    }
                }

                if (toRun == null) return; // superseded or flushed; whoever replaced us owns disposal

                try { entry.Cts.Dispose(); } catch { }
                // Run outside the lock so slow actions don't block other debounce calls.
                try { toRun(); } catch { }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Flushes and executes all pending debounced actions immediately. Useful on application exit.
        /// </summary>
        public static void FlushAll()
        {
            List<Entry> pending;
            lock (_syncLock)
            {
                pending = new List<Entry>(_entries.Values);
                _entries.Clear();
            }

            foreach (var e in pending)
            {
                CancelAndDispose(e);
                try { e.Action(); } catch { }
            }
        }

        private static void CancelAndDispose(Entry e)
        {
            try { e.Cts.Cancel(); } catch { }
            try { e.Cts.Dispose(); } catch { }
        }
    }
}
