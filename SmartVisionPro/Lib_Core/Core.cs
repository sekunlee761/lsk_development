using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Core
{
    // Core singleton: 전체 매니저들의 초기화/종료를 중앙에서 관리합니다.
    public class Core : CSingleton<Core>
    {
        private bool _initialized = false;
        private readonly object _initLock = new object();
        // 저장된 초기화 순서 (성공적으로 초기화되었을 때만 채워짐)
        private List<Type> _managerInitOrder = null;

        public override void Initialize()
        {
            lock (_initLock)
            {
                if (_initialized) return;

                var initializedManagers = new List<Action>();

                try
                {
                    // 자동으로 Lib_Core 어셈블리 내의 CSingleton<T>를 상속하는 매니저들을 찾아 초기화
                    var asm = typeof(Core).Assembly;
                    var allManagerTypes = asm.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract && t.BaseType != null
                                    && t.BaseType.IsGenericType
                                    && t.BaseType.GetGenericTypeDefinition() == typeof(CSingleton<>))
                        .Where(t => t != typeof(Core))
                        .ToList();

                    // Build dependency graph using ManagerAttribute (if present)
                    var attrMap = new Dictionary<Type, ManagerAttribute>();
                    foreach (var t in allManagerTypes)
                    {
                        var attr = t.GetCustomAttribute<ManagerAttribute>(false) ?? new ManagerAttribute();
                        attrMap[t] = attr;
                    }

                    // adjacency: dep -> t
                    var adj = new Dictionary<Type, List<Type>>();
                    var indegree = new Dictionary<Type, int>();
                    foreach (var t in allManagerTypes)
                    {
                        adj[t] = new List<Type>();
                        indegree[t] = 0;
                    }

                    foreach (var t in allManagerTypes)
                    {
                        var deps = attrMap[t].DependsOn ?? new Type[0];
                        foreach (var d in deps)
                        {
                            if (d == null) continue;
                            if (!adj.ContainsKey(d)) continue; // ignore external deps
                            adj[d].Add(t);
                            indegree[t] = indegree[t] + 1;
                        }
                    }

                    // Kahn's algorithm with priority (Order)
                    var ready = new List<Type>();
                    foreach (var kv in indegree)
                    {
                        if (kv.Value == 0) ready.Add(kv.Key);
                    }
                    // sort ready by Order then name
                    ready = ready.OrderBy(x => attrMap[x].Order).ThenBy(x => x.Name).ToList();

                    var managerTypes = new List<Type>();
                    while (ready.Count > 0)
                    {
                        var n = ready[0];
                        ready.RemoveAt(0);
                        managerTypes.Add(n);
                        foreach (var m in adj[n])
                        {
                            indegree[m] = indegree[m] - 1;
                            if (indegree[m] == 0)
                            {
                                ready.Add(m);
                            }
                        }
                        ready = ready.OrderBy(x => attrMap[x].Order).ThenBy(x => x.Name).ToList();
                    }

                    // If cycle detected (not all nodes processed), fallback to order by Order then name
                    if (managerTypes.Count != allManagerTypes.Count)
                    {
                        SafeLog("Manager 의존성에 순환 또는 누락이 감지되어 단순 우선순위 정렬로 대체합니다.");
                        managerTypes = allManagerTypes.OrderBy(t => attrMap[t].Order).ThenBy(t => t.Name).ToList();
                    }

                    // Save order for shutdown
                    _managerInitOrder = new List<Type>(managerTypes);

                    foreach (var t in managerTypes)
                    {
                        try
                        {
                            var baseType = typeof(CSingleton<>).MakeGenericType(t);
                            var instProp = baseType.GetProperty("Inst", BindingFlags.Public | BindingFlags.Static);
                            var inst = instProp.GetValue(null);

                            // 인스턴스의 Initialize 호출
                            var initMethod = t.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
                            initMethod?.Invoke(inst, null);

                            // 정상 초기화되면 종료 액션을 스택에 추가
                            var shutdownMethod = t.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Instance);
                            var name = t.Name;
                            var localInst = inst;
                            initializedManagers.Add(() => SafeShutdown(() => shutdownMethod?.Invoke(localInst, null), name));
                        }
                        catch (Exception ex)
                        {
                            SafeLog($"{t.Name}.Initialize 실패: " + ex);
                            throw;
                        }
                    }

                    _initialized = true;
                }
                catch (Exception ex)
                {
                    // 초기화 도중 실패한 경우, 이미 초기화된 매니저들을 역순으로 종료
                    SafeLog("Core.Initialize 실패: " + ex + ". 이미 초기화된 매니저 종료 시도...");
                    for (int i = initializedManagers.Count - 1; i >= 0; i--)
                    {
                        try { initializedManagers[i].Invoke(); }
                        catch (Exception e) { SafeLog("매니저 종료 중 예외: " + e); }
                    }

                    // Clear all CSingleton<T> 인스턴스 기록을 제거
                    try
                    {
                        var asm = typeof(Core).Assembly;
                        var allManagerTypes = asm.GetTypes()
                            .Where(t => t.IsClass && !t.IsAbstract && t.BaseType != null
                                        && t.BaseType.IsGenericType
                                        && t.BaseType.GetGenericTypeDefinition() == typeof(CSingleton<>))
                            .Where(t => t != typeof(Core))
                            .ToList();

                        foreach (var t in allManagerTypes)
                        {
                            try
                            {
                                var baseType = typeof(CSingleton<>).MakeGenericType(t);
                                var clearMethod = baseType.GetMethod("ClearInstance", BindingFlags.Public | BindingFlags.Static);
                                clearMethod?.Invoke(null, null);
                            }
                            catch (Exception e)
                            {
                                SafeLog($"ClearInstance({t.Name}) 실패: " + e);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        SafeLog("ClearInstance 전체 수행 중 예외: " + e);
                    }

                    _initialized = false;
                }
            }
        }

        public override void Shutdown()
        {
            lock (_initLock)
            {
                if (!_initialized)
                {
                    // 그래도 인스턴스 정리는 시도
                    SafeLog("Core.Shutdown: 이미 종료된 상태입니다. 인스턴스 정리 시도");
                }
                // Use saved initialization order if available, otherwise discover managers
                List<Type> shutdownOrder = null;
                if (_managerInitOrder != null && _managerInitOrder.Count > 0)
                {
                    // shutdown should be reverse of init order
                    shutdownOrder = new List<Type>(_managerInitOrder);
                    shutdownOrder.Reverse();
                }
                else
                {
                    try
                    {
                        var asm = typeof(Core).Assembly;
                        shutdownOrder = asm.GetTypes()
                            .Where(t => t.IsClass && !t.IsAbstract && t.BaseType != null
                                        && t.BaseType.IsGenericType
                                        && t.BaseType.GetGenericTypeDefinition() == typeof(CSingleton<>))
                            .Where(t => t != typeof(Core))
                            .OrderBy(t => t.Name == "LogManager" ? 0 : 1).ThenBy(t => t.Name)
                            .ToList();
                        shutdownOrder.Reverse();
                    }
                    catch (Exception ex)
                    {
                        SafeLog("매니저 목록 수집 중 예외: " + ex);
                        shutdownOrder = new List<Type>();
                    }
                }

                // Call Shutdown in determined order
                try
                {
                    foreach (var t in shutdownOrder)
                    {
                        try
                        {
                            var baseType = typeof(CSingleton<>).MakeGenericType(t);
                            var instProp = baseType.GetProperty("Inst", BindingFlags.Public | BindingFlags.Static);
                            var inst = instProp.GetValue(null);
                            var shutdownMethod = t.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Instance);
                            SafeShutdown(() => shutdownMethod?.Invoke(inst, null), t.Name);
                        }
                        catch (Exception ex)
                        {
                            SafeLog($"{t.Name}.Shutdown 호출 중 예외: " + ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    SafeLog("매니저 종료 호출 중 예외: " + ex);
                }

                // Clear instances in original init order if available, otherwise clear discovered types
                try
                {
                    if (_managerInitOrder != null && _managerInitOrder.Count > 0)
                    {
                        foreach (var t in _managerInitOrder)
                        {
                            try
                            {
                                var baseType = typeof(CSingleton<>).MakeGenericType(t);
                                var clearMethod = baseType.GetMethod("ClearInstance", BindingFlags.Public | BindingFlags.Static);
                                clearMethod?.Invoke(null, null);
                            }
                            catch (Exception ex)
                            {
                                SafeLog($"ClearInstance({t.Name}) 실패: " + ex);
                            }
                        }
                    }
                    else
                    {
                        var asm = typeof(Core).Assembly;
                        var allManagerTypes = asm.GetTypes()
                            .Where(t => t.IsClass && !t.IsAbstract && t.BaseType != null
                                        && t.BaseType.IsGenericType
                                        && t.BaseType.GetGenericTypeDefinition() == typeof(CSingleton<>))
                            .Where(t => t != typeof(Core))
                            .ToList();

                        foreach (var t in allManagerTypes)
                        {
                            try
                            {
                                var baseType = typeof(CSingleton<>).MakeGenericType(t);
                                var clearMethod = baseType.GetMethod("ClearInstance", BindingFlags.Public | BindingFlags.Static);
                                clearMethod?.Invoke(null, null);
                            }
                            catch (Exception ex)
                            {
                                SafeLog($"ClearInstance({t.Name}) 실패: " + ex);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    SafeLog("인스턴스 정리 중 예외: " + ex);
                }

                try { CSingleton<Core>.ClearInstance(); } catch (Exception ex) { SafeLog("ClearInstance(Core) 실패: " + ex); }

                _initialized = false;
            }
        }

        private void SafeShutdown(Action action, string name)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                SafeLog($"{name}.Shutdown 중 예외: " + ex);
            }
        }

        private void SafeLog(string message)
        {
            try
            {
                if (LogManager.Exists() && LogManager.Inst != null && LogManager.Inst.IsInitialized)
                {
                    LogManager.Inst.Write(message);
                }
                else
                {
                    Console.WriteLine(message);
                }
            }
            catch
            {
                // 로그 기록 자체에서 예외가 나더라도 무시
                try { Console.WriteLine(message); } catch { }
            }
        }
    }
}
