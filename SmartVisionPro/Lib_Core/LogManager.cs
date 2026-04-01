using System;

namespace Core
{
    [Manager(Order = 0)]
    public class LogManager : CSingleton<LogManager>
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
                    // 간단한 초기화 코드 예시
                    Console.WriteLine("로그 매니저 초기화");
                    // 실제 로그 백엔드 초기화 로직을 여기에 추가
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    // 초기화 실패시 상태 정리 후 예외 재던짐
                    _initialized = false;
                    try { Console.WriteLine("LogManager.Initialize 예외: " + ex); } catch { }
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
                    Console.WriteLine("로그 매니저 종료");
                    // 실제 자원 해제 로직을 여기에 추가
                    _initialized = false;
                }
                catch (Exception ex)
                {
                    try { Console.WriteLine("LogManager.Shutdown 예외: " + ex); } catch { }
                    _initialized = false;
                    throw;
                }
            }
        }

        // 로그 출력 예시 메서드
        public void Write(string message)
        {
            try
            {
                Console.WriteLine($"[로그] {message}");
            }
            catch
            {
                // 로그 출력 실패는 무시
            }
        }
    }
}
