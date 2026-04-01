using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Core
{
    // File handler interface: 각 확장자별로 구현
    public interface IFileHandler
    {
        bool CanHandle(string extension);
        byte[] ReadBytes(string path);
        void WriteBytes(string path, byte[] data);
        string ReadText(string path, Encoding encoding = null);
        void WriteText(string path, string text, Encoding encoding = null);
    }

    // 텍스트 파일 핸들러: json, txt 등
    public class TextFileHandler : IFileHandler
    {
        private readonly HashSet<string> _exts;

        public TextFileHandler(IEnumerable<string> exts)
        {
            _exts = new HashSet<string>(exts.Select(e => e.Trim().ToLowerInvariant()));
        }

        public bool CanHandle(string extension) => _exts.Contains(extension?.Trim().ToLowerInvariant());

        public byte[] ReadBytes(string path) => File.ReadAllBytes(path);

        public void WriteBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

        public string ReadText(string path, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            return File.ReadAllText(path, encoding);
        }

        public void WriteText(string path, string text, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            File.WriteAllText(path, text, encoding);
        }
    }

    // 바이너리 파일 핸들러: 이미지, 엑셀(xlsx) 등
    public class BinaryFileHandler : IFileHandler
    {
        private readonly HashSet<string> _exts;

        public BinaryFileHandler(IEnumerable<string> exts)
        {
            _exts = new HashSet<string>(exts.Select(e => e.Trim().ToLowerInvariant()));
        }

        public bool CanHandle(string extension) => _exts.Contains(extension?.Trim().ToLowerInvariant());

        public byte[] ReadBytes(string path) => File.ReadAllBytes(path);

        public void WriteBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

        // 텍스트 관련 호출은 바이트로 처리
        public string ReadText(string path, Encoding encoding = null)
        {
            var bytes = ReadBytes(path);
            encoding = encoding ?? Encoding.UTF8;
            return encoding.GetString(bytes);
        }

        public void WriteText(string path, string text, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            var bytes = encoding.GetBytes(text);
            WriteBytes(path, bytes);
        }
    }

    // FileManager: 다양한 포맷의 파일을 로드/수정/저장/삭제 관리
    [Manager(DependsOn = new[] { typeof(PathManager) }, Order = 30)]
    public class FileManager : CSingleton<FileManager>
    {
        private readonly List<IFileHandler> _handlers = new List<IFileHandler>();
        private bool _initialized = false;
        private readonly object _lock = new object();

        public bool IsInitialized => _initialized;

        public override void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    // 기본 핸들러 등록
                    // 텍스트 계열: json, txt
                    _handlers.Add(new TextFileHandler(new[] { ".json", ".txt" }));
                    // 이미지 계열: bmp, png
                    _handlers.Add(new BinaryFileHandler(new[] { ".bmp", ".png" }));
                    // 엑셀: xlsx (바이너리로 기본 처리)
                    _handlers.Add(new BinaryFileHandler(new[] { ".xlsx" }));

                    _initialized = true;
                }
                catch (Exception ex)
                {
                    _initialized = false;
                    try { LogManager.Inst.Write("FileManager.Initialize 오류: " + ex); } catch { }
                    throw;
                }
            }
        }

        public override void Shutdown()
        {
            lock (_lock)
            {
                if (!_initialized) return;
                _handlers.Clear();
                _initialized = false;
            }
        }

        // 핸들러 추가 (확장자 우선 등록)
        public void RegisterHandler(IFileHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_lock) { _handlers.Insert(0, handler); }
        }

        private IFileHandler GetHandler(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
            lock (_lock)
            {
                foreach (var h in _handlers)
                {
                    try
                    {
                        if (h.CanHandle(ext)) return h;
                    }
                    catch { }
                }
            }

            return null;
        }

        // 텍스트 읽기 (json/txt 등)
        public string ReadText(string path, Encoding encoding = null)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var handler = GetHandler(path);
            if (handler == null) throw new NotSupportedException($"지원하지 않는 파일 형식: {Path.GetExtension(path)}");
            try
            {
                return handler.ReadText(path, encoding);
            }
            catch (Exception ex)
            {
                LogManager.Inst.Write($"ReadText 예외: {ex}");
                throw;
            }
        }

        // 텍스트 쓰기
        public void WriteText(string path, string text, Encoding encoding = null)
        {
            var handler = GetHandler(path);
            if (handler == null) throw new NotSupportedException($"지원하지 않는 파일 형식: {Path.GetExtension(path)}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                handler.WriteText(path, text, encoding);
            }
            catch (Exception ex)
            {
                LogManager.Inst.Write($"WriteText 예외: {ex}");
                throw;
            }
        }

        // 바이트 읽기
        public byte[] ReadBytes(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var handler = GetHandler(path);
            if (handler == null) throw new NotSupportedException($"지원하지 않는 파일 형식: {Path.GetExtension(path)}");
            try
            {
                return handler.ReadBytes(path);
            }
            catch (Exception ex)
            {
                LogManager.Inst.Write($"ReadBytes 예외: {ex}");
                throw;
            }
        }

        // 바이트 쓰기
        public void WriteBytes(string path, byte[] data)
        {
            var handler = GetHandler(path);
            if (handler == null) throw new NotSupportedException($"지원하지 않는 파일 형식: {Path.GetExtension(path)}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                handler.WriteBytes(path, data);
            }
            catch (Exception ex)
            {
                LogManager.Inst.Write($"WriteBytes 예외: {ex}");
                throw;
            }
        }

        public bool Exists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        public void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                LogManager.Inst.Write($"Delete 예외: {ex}");
                throw;
            }
        }

        // 지원 확장자 목록 반환
        public IEnumerable<string> GetSupportedExtensions()
        {
            lock (_lock)
            {
                var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in _handlers)
                {
                    try
                    {
                        // 반영: 핸들러 유형에 따라 알려진 확장자 제공
                        if (h is TextFileHandler) { list.UnionWith(new[] { ".json", ".txt" }); }
                        else if (h is BinaryFileHandler) { list.UnionWith(new[] { ".bmp", ".png", ".xlsx" }); }
                    }
                    catch { }
                }

                return list.ToArray();
            }
        }
    }
}
