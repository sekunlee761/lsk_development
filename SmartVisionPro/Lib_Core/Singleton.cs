using System;
using System.Threading;

namespace Core
{
    // Thread-safe generic singleton base
    // Managers and Core should inherit from this class.
    public abstract class CSingleton<T> where T : CSingleton<T>, new()
    {
        private static volatile Lazy<T> _lazyInstance = null;
        private static readonly object _lock = new object();

        protected CSingleton()
        {
        }

        // 인스턴스 접근 (스레드 안전)
        public static T Inst
        {
            get
            {
                if (_lazyInstance == null)
                {
                    lock (_lock)
                    {
                        if (_lazyInstance == null)
                        {
                            // LazyThreadSafetyMode.ExecutionAndPublication ensures thread-safety
                            _lazyInstance = new Lazy<T>(() => new T(), LazyThreadSafetyMode.ExecutionAndPublication);
                        }
                    }
                }

                return _lazyInstance.Value;
            }
        }

        // 인스턴스가 생성되었는지 확인
        public static bool Exists()
        {
            return _lazyInstance != null && _lazyInstance.IsValueCreated;
        }

        // 인스턴스 초기화 이력 제거 (테스트 또는 재시작용)
        public static void ClearInstance()
        {
            lock (_lock)
            {
                _lazyInstance = null;
            }
        }

        // 서브클래스는 초기화/종료 로직을 오버라이드
        public virtual void Initialize()
        {
        }

        public virtual void Shutdown()
        {
        }
    }

    // 기존 CSingletonLazy는 호환성을 위해 남겨둡니다.
    public class CSingletonLazy<T> where T : CSingletonLazy<T>, new()
    {
        private static Lazy<T> m_lazyInst = null;
        private static readonly object _lock = new object();

        public CSingletonLazy()
        {
        }

        public static T Inst
        {
            get
            {
                lock (_lock)
                {
                    if (Exists() == false)
                    {
                        var instance = new T();
                        m_lazyInst = new Lazy<T>(() => instance);
                    }
                }

                return m_lazyInst.Value;
            }
        }

        //인스턴스가 만들어졌는지 체크합니다.
        public static bool Exists()
        {
            return m_lazyInst != null && m_lazyInst.IsValueCreated;
        }

        //인스턴스 생성이력을 초기화 할때 사용합니다.
        public static void ClearInstance()
        {
            m_lazyInst = null;
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ManagerAttribute : Attribute
    {
        // 낮은 값일수록 높은 우선순위를 가집니다.
        public int Order { get; set; } = 100;

        // 해당 매니저가 의존하는 매니저 타입 목록
        public Type[] DependsOn { get; set; } = null;

        public ManagerAttribute()
        {
        }
    }

}
