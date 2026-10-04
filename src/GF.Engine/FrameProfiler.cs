using System.Diagnostics;

namespace GF.Engine;

/// <summary>
/// Medidor sencillo de tiempos por sección (media móvil, en ms). Solo para el hilo principal.
///     using (profiler.Measure("mallado")) renderer.Update(...);
///     texto = profiler.Report();
/// </summary>
public sealed class FrameProfiler
{
    private readonly Dictionary<string, double> _ema = new();
    private readonly List<string> _order = new();

    public Scope Measure(string name) => new(this, name, Stopwatch.GetTimestamp());

    private void Record(string name, double ms)
    {
        if (_ema.TryGetValue(name, out double v)) v += (ms - v) * 0.1;
        else { _order.Add(name); v = ms; }
        _ema[name] = v;
    }

    /// <summary>"seccion1 1.20  seccion2 0.35 ..." en el orden en que se midieron por primera vez.</summary>
    public string Report() => string.Join("  ", _order.Select(n => $"{n} {_ema[n]:0.00}"));

    public readonly struct Scope : IDisposable
    {
        private readonly FrameProfiler _owner;
        private readonly string _name;
        private readonly long _start;

        internal Scope(FrameProfiler owner, string name, long start)
        {
            _owner = owner; _name = name; _start = start;
        }

        public void Dispose() => _owner.Record(_name, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }
}
