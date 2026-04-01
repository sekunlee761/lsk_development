using System;
using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace Core
{
    // Serializer that prefers Newtonsoft.Json at runtime (if available) for JSON operations.
    // Falls back to XmlSerializer for XML operations and for Clone when Newtonsoft is not present.
    public static class RecipeSerializer
    {
        // JSON using Newtonsoft if available; otherwise throws InvalidOperationException for JSON operations.
        public static string ToJson<T>(T obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            var s = TryNewtonsoftSerialize(obj);
            if (s != null) return s;
            throw new InvalidOperationException("Newtonsoft.Json 라이브러리가 없습니다. JSON 직렬화를 사용하려면 Newtonsoft.Json 패키지를 설치하세요.");
        }

        // Public helpers for repository use
        public static List<T> TryDeserializeListWithNewtonsoft<T>(string json)
        {
            try { return TryNewtonsoftDeserialize<List<T>>(json); }
            catch { return null; }
        }

        public static string TrySerializeWithNewtonsoftObject(object obj)
        {
            try { return TryNewtonsoftSerialize(obj); }
            catch { return null; }
        }

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentNullException(nameof(json));
            var o = TryNewtonsoftDeserialize<T>(json);
            if (o != null) return o;
            throw new InvalidOperationException("Newtonsoft.Json 라이브러리가 없습니다. JSON 역직렬화를 사용하려면 Newtonsoft.Json 패키지를 설치하세요.");
        }

        // XML using XmlSerializer
        public static string ToXml<T>(T obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            var xs = new XmlSerializer(typeof(T));
            using (var sw = new StringWriter())
            {
                xs.Serialize(sw, obj);
                return sw.ToString();
            }
        }

        public static T FromXml<T>(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) throw new ArgumentNullException(nameof(xml));
            var xs = new XmlSerializer(typeof(T));
            using (var sr = new StringReader(xml))
            {
                return (T)xs.Deserialize(sr);
            }
        }

        // Clone: prefer JSON round-trip if Newtonsoft available, otherwise XML round-trip.
        public static T Clone<T>(T obj)
        {
            if (obj == null) return default(T);
            try
            {
                var json = TryNewtonsoftSerialize(obj);
                if (json != null)
                {
                    var cloned = TryNewtonsoftDeserialize<T>(json);
                    if (cloned != null) return cloned;
                }
            }
            catch { }

            // Fallback to XML round-trip
            var xml = ToXml(obj);
            return FromXml<T>(xml);
        }

        // Reflection-based Newtonsoft interop to avoid compile-time dependency
        private static string TryNewtonsoftSerialize(object obj)
        {
            try
            {
                var t = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json");
                if (t == null) return null;
                var m = t.GetMethod("SerializeObject", new Type[] { typeof(object) });
                if (m == null) return null;
                return m.Invoke(null, new object[] { obj }) as string;
            }
            catch { return null; }
        }

        private static T TryNewtonsoftDeserialize<T>(string json)
        {
            try
            {
                var t = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json");
                if (t == null) return default(T);
                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (var m in methods)
                {
                    if (m.Name != "DeserializeObject") continue;
                    if (!m.IsGenericMethodDefinition) continue;
                    var gm = m.MakeGenericMethod(typeof(T));
                    var obj = gm.Invoke(null, new object[] { json });
                    return (T)obj;
                }
                return default(T);
            }
            catch { return default(T); }
        }
    }
}
