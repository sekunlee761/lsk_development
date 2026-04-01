using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;

namespace Core
{
    // PathManager provides simple directory and path utilities used by the application.
    // Public methods are thread-safe for initialization and basic helpers.
    [Manager(Order = 10)]
    public class PathManager : CSingleton<PathManager>
    {
        private bool _initialized = false;
        private readonly object _lock = new object();

        public bool IsInitialized => _initialized;

        // Base and commonly used directories
        public string BaseDirectory { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string LogDirectory { get; private set; }
        public string DataDirectory { get; private set; }

        public override void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    // Use executable (entry assembly) location as root when possible.
                    // Fallback order: EntryAssembly.Location -> Process.MainModule.FileName -> AppDomain.BaseDirectory -> CurrentDirectory
                    string exePath = null;
                    try { exePath = Assembly.GetEntryAssembly()?.Location; } catch { exePath = null; }
                    if (string.IsNullOrEmpty(exePath))
                    {
                        try { exePath = Process.GetCurrentProcess()?.MainModule?.FileName; } catch { exePath = null; }
                    }

                    if (!string.IsNullOrEmpty(exePath))
                    {
                        try { BaseDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory; } catch { BaseDirectory = AppDomain.CurrentDomain.BaseDirectory; }
                    }
                    else
                    {
                        BaseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
                    }

                    ConfigDirectory = Path.Combine(BaseDirectory, "Config");
                    LogDirectory = Path.Combine(BaseDirectory, "Log");
                    DataDirectory = Path.Combine(BaseDirectory, "Data");

                    EnsureDirectory(ConfigDirectory);
                    EnsureDirectory(LogDirectory);
                    EnsureDirectory(DataDirectory);

                    _initialized = true;
                }
                catch (Exception ex)
                {
                    _initialized = false;
                    try { Console.WriteLine("PathManager.Initialize 오류: " + ex); } catch { }
                    throw;
                }
            }
        }

        public override void Shutdown()
        {
            lock (_lock)
            {
                if (!_initialized) return;
                // No persistent resources to release currently
                _initialized = false;
            }
        }

        // Ensure directory exists, swallow non-fatal exceptions and report to console
        private void EnsureDirectory(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                try { Console.WriteLine("디렉터리 생성 실패: " + path + " / " + ex); } catch { }
            }
        }

        // Helpers to get full paths for common areas
        public string GetConfigPath(string fileName)
        {
            var dir = ConfigDirectory ?? BaseDirectory ?? string.Empty;
            return string.IsNullOrEmpty(fileName) ? dir : Path.Combine(dir, fileName);
        }

        public string GetLogPath(string fileName)
        {
            var dir = LogDirectory ?? BaseDirectory ?? string.Empty;
            return string.IsNullOrEmpty(fileName) ? dir : Path.Combine(dir, fileName);
        }

        public string GetDataPath(string fileName)
        {
            var dir = DataDirectory ?? BaseDirectory ?? string.Empty;
            return string.IsNullOrEmpty(fileName) ? dir : Path.Combine(dir, fileName);
        }

        // Generic combine helper
        public string Combine(params string[] parts)
        {
            try
            {
                if (parts == null || parts.Length == 0) return string.Empty;
                return Path.Combine(parts);
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool ExistsDirectory(string path)
        {
            try { return Directory.Exists(path); } catch { return false; }
        }
    }
}
