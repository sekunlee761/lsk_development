using System;
using System;

namespace Core
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ManagerAttribute : Attribute
    {
        // Lower Order means higher priority during initialization
        public int Order { get; set; } = 100;

        // Types that this manager depends on (they will be initialized before this manager)
        public Type[] DependsOn { get; set; } = new Type[0];

        public ManagerAttribute()
        {
        }
    }
}
