using BepInEx.Logging;
using System.Diagnostics;

namespace BetterBlueprintPinManager
{
    public static class ModProfiler
    {
//#if DEBUG
        private static ManualLogSource _logger;

        //static ModProfiler()
        //{
        //    _logger = Plugin.Logger;
        //}

        public static void Init(ManualLogSource logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Starts a timer and returns the Stopwatch instance.
        /// </summary>
        public static Stopwatch Start()
        {
            return Stopwatch.StartNew();
        }

        /// /// <summary>
        /// Stops the timer and logs execution metrics.
        /// </summary>
        public static void LogAndStop(this Stopwatch timer, string targetName, double thresholdMs = 0.0)
        {
            Log(timer, targetName, thresholdMs, true);
        }
        /// <summary>
        /// logs execution metrics, timer restarts.
        /// </summary>
        public static void LogAndRestart(this Stopwatch timer, string targetName, double thresholdMs = 0.0)
        {
            Log(timer, targetName, thresholdMs, false);
            timer.Restart();
        }
        /// <summary>
        /// just logs execution metrics, timer continues.
        /// </summary>
        public static void Log(this Stopwatch timer, string targetName, double thresholdMs = 0.0, bool shouldStop = false)
        {
            if (timer == null) return;
            if (shouldStop) timer.Stop();

            double elapsedMs = timer.Elapsed.TotalMilliseconds;

            // Optional: filter out spam by only logging calls that cross a performance threshold
            if (elapsedMs >= thresholdMs)
            {
#if DEBUG
                _logger?.LogInfo($"[PERF] {targetName} execution time: {elapsedMs:F3} ms");
#else
                _logger?.LogDebug($"[PERF] {targetName} execution time: {elapsedMs:F3} ms");
#endif

            }
        }
//#else
//        [Conditional("DEBUG")]
//        public static void Init(ManualLogSource logger) { }
//        public static bool Start()
//        {
//            return false;
//        }
//        [Conditional("DEBUG")]
//        public static void LogAndStop(this bool _, string targetName, double thresholdMs = 0.0) { }
//        [Conditional("DEBUG")]
//        public static void LogAndRestart(this bool _, string targetName, double thresholdMs = 0.0) { }
//        [Conditional("DEBUG")]
//        public static void Log(this bool _, string targetName, double thresholdMs = 0.0, bool shouldStop = false) { }

//#endif
    }
}