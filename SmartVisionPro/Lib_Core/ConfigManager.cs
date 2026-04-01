using System;

namespace Core
{
    [Manager(Order = 20)]
    public class ConfigManager : CSingleton<ConfigManager>
    {
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
                    Console.WriteLine("설정 매니저 초기화");
                    // 설정 로드 등 초기화 코드 작성
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    _initialized = false;
                    try { Console.WriteLine("ConfigManager.Initialize 예외: " + ex); } catch { }
                    throw;
                }
            }
        }

        public override void Shutdown()
        {
            lock (_lock)
            {
                if (!_initialized) return;
                try
                {
                    Console.WriteLine("설정 매니저 종료");
                    _initialized = false;
                }
                catch (Exception ex)
                {
                    try { Console.WriteLine("ConfigManager.Shutdown 예외: " + ex); } catch { }
                    _initialized = false;
                    throw;
                }
            }
        }

        // 설정값 예시 메서드
        public string Get(string key, string defaultValue = "")
        {
            // 예시: 실제 구현은 파일/레지스트리 등에서 읽음
            return defaultValue;
        }
    }
}
