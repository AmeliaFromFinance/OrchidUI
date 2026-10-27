using System;
using System.Collections.Generic;
using Xunit;

namespace OrchidUI.Tests
{
    // Environment variables are process-wide, so tests that set them run one at a time.
    [CollectionDefinition("environment", DisableParallelization = true)]
    public sealed class EnvironmentCollection { }

    public sealed class Env : IDisposable
    {
        private readonly Dictionary<string, string> _old = new Dictionary<string, string>();

        private Env() { }

        public static Env Set(params string[] pairs)
        {
            var e = new Env();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                if (!e._old.ContainsKey(pairs[i])) e._old[pairs[i]] = Environment.GetEnvironmentVariable(pairs[i]);
                Environment.SetEnvironmentVariable(pairs[i], pairs[i + 1]);
            }
            return e;
        }

        public static Env Clear(params string[] names)
        {
            var e = new Env();
            foreach (var n in names)
            {
                e._old[n] = Environment.GetEnvironmentVariable(n);
                Environment.SetEnvironmentVariable(n, null);
            }
            return e;
        }

        public void Dispose()
        {
            foreach (var kv in _old) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
    }
}
