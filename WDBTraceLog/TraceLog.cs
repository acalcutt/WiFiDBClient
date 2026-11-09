using System.Diagnostics;

namespace WDBTraceLog
{
    /// <summary>Optional trace log, written to from any thread. Never pass API keys to it.</summary>
    public sealed class TraceLog : IDisposable
    {
        private readonly object _gate = new();
        private readonly StreamWriter? _file;

        public bool TraceLogEnable { get; }

        public TraceLog(string logFolder, bool traceLogEnable, bool perRunRotate)
        {
            TraceLogEnable = traceLogEnable;
            if (!traceLogEnable) return;

            string name = perRunRotate ? $"Trace-{DateTime.Now:yyyyMMdd-HHmmss}.log" : "Trace.log";
            Directory.CreateDirectory(logFolder);
            _file = new StreamWriter(Path.Combine(logFolder, name), append: true) { AutoFlush = true };
        }

        public void Write(string source, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}][{source}] {message}";
            Debug.WriteLine(line);
            if (_file is null) return;
            lock (_gate)
            {
                _file.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _file?.Dispose();
            }
        }
    }
}
