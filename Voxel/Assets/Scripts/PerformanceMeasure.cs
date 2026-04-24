using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VoxelEngine
{
    public static class PerformanceMeasure
    {
        public readonly struct Scope : IDisposable
        {
            readonly string _name;
            readonly long _startTicks;
             
            public Scope(string name)
            {
                _name = name;
                _startTicks = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                long endTicks = Stopwatch.GetTimestamp();
                long endAllocBytes = GC.GetAllocatedBytesForCurrentThread();

                long elapsedTicks = endTicks - _startTicks;

                AddSample(_name, elapsedTicks);
            }
        }

        struct Stat
        {
            public long TotalTicks;
            public int Count;
        }

        static readonly Dictionary<string, Stat> _stats = new();

        public static Scope Measure(string name)
        {
            return new Scope(name);
        }

        static void AddSample(string name, long elapsedTicks)
        {
            if (_stats.TryGetValue(name, out Stat stat))
            {
                stat.TotalTicks += elapsedTicks;
                stat.Count++;
                _stats[name] = stat;
            }
            else
            {
                Stat newStat = new Stat();
                newStat.TotalTicks = elapsedTicks;
                newStat.Count = 1;
                _stats[name] = newStat;
            }
        }

        public static double GetMilliseconds(string name)
        {
            if (_stats.TryGetValue(name, out Stat stat) == false)
            {
                return 0.0;
            }

            return stat.TotalTicks * 1000.0 / Stopwatch.Frequency;
        }

        public static int GetCount(string name)
        {
            if (_stats.TryGetValue(name, out Stat stat) == false)
            {
                return 0;
            }

            return stat.Count;
        }


        public static void LogSummary()
        {
            foreach (var kv in _stats)
            {
                string name = kv.Key;
                Stat stat = kv.Value;

                double totalMs = stat.TotalTicks * 1000.0 / Stopwatch.Frequency;
                double avgMs = stat.Count > 0 ? totalMs / stat.Count : 0.0;

                UnityEngine.Debug.Log(
                    $"[PerformanceMeasure] {name} | " +
                    $"total {totalMs:F3} ms | " +
                    $"count {stat.Count} | " +
                    $"avg {avgMs:F3} ms | ");
            }
        }

    }
}
