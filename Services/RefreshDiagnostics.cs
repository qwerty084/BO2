using System;
using System.Diagnostics;

namespace BO2.Services
{
    internal static class RefreshDiagnostics
    {
        public static long Start()
        {
#if DEBUG
            return Stopwatch.GetTimestamp();
#else
            return 0;
#endif
        }

        [Conditional("DEBUG")]
        public static void WriteElapsed(string operationName, long startedAt)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            Debug.WriteLine($"BO2 {operationName}: {elapsed.TotalMilliseconds:0.0} ms");
        }
    }
}
