// ===========================================================================
// YxMod5 C# 模组层 —— 反射访问器核心（roadmap 1.1 / 1.4）
// ---------------------------------------------------------------------------
// 目标：
//   * 集中缓存 Type / FieldInfo / PropertyInfo / MethodInfo；
//   * 将「读 / 写 / 调用」委托化（Expression 编译 + Delegate.CreateDelegate），
//     避免每帧 MethodInfo.Invoke 开销；
//   * 所有访问统一走 Safe 防护，单点故障不拖垮模组。
//
// 说明：
//   本层是自研「反射访问器层」，游戏版本差异集中收敛到这一层与 Accessors 层。
//   不依赖 Harmony / Mono.Cecil / MonoMod 等第三方库。
// ===========================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace YxMod5.Runtime
{
    /// <summary>字段 / 属性访问的成员缓存键。</summary>
    internal readonly struct MemberKey : IEquatable<MemberKey>
    {
        public readonly Type Type;
        public readonly string Name;

        public MemberKey(Type type, string name)
        {
            Type = type;
            Name = name;
        }

        public bool Equals(MemberKey other) => Type == other.Type && Name == other.Name;

        public override bool Equals(object obj) => obj is MemberKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Type != null ? Type.GetHashCode() : 0) * 397) ^
                       (Name != null ? Name.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    /// 反射访问器核心：缓存 + 委托化 + 安全降级。
    /// </summary>
    public static class Reflector
    {
        private static readonly object Sync = new object();

        private static readonly Dictionary<string, Type> TypeCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        private static readonly Dictionary<MemberKey, Func<object, object>> GetterCache =
            new Dictionary<MemberKey, Func<object, object>>();

        private static readonly Dictionary<MemberKey, Action<object, object>> SetterCache =
            new Dictionary<MemberKey, Action<object, object>>();

        private static readonly Dictionary<MethodKey, MethodInfo> MethodCache =
            new Dictionary<MethodKey, MethodInfo>();

        private readonly struct MethodKey : IEquatable<MethodKey>
        {
            public readonly Type Type;
            public readonly string Name;
            public readonly int ParamCount;

            public MethodKey(Type type, string name, int paramCount)
            {
                Type = type;
                Name = name;
                ParamCount = paramCount;
            }

            public bool Equals(MethodKey other) =>
                Type == other.Type && Name == other.Name && ParamCount == other.ParamCount;

            public override bool Equals(object obj) => obj is MethodKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Type != null ? Type.GetHashCode() : 0;
                    hash = (hash * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                    hash = (hash * 397) ^ ParamCount;
                    return hash;
                }
            }
        }

        // ---- 类型查找 ------------------------------------------------------

        /// <summary>按程序集限定名 / 全名查找类型（缓存）。</summary>
        public static Type FindType(string fullName, bool throwOnMissing = false)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            lock (Sync)
            {
                if (TypeCache.TryGetValue(fullName, out Type cached))
                {
                    return cached;
                }

                Type result = Type.GetType(fullName, throwOnError: false);
                if (result == null)
                {
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            result = assembly.GetType(fullName, throwOnError: false);
                            if (result != null)
                            {
                                break;
                            }
                        }
                        catch
                        {
                            // 某些程序集反射访问会失败，忽略。
                        }
                    }
                }

                if (result == null && throwOnMissing)
                {
                    throw new MissingMemberException("未找到类型：" + fullName);
                }

                TypeCache[fullName] = result;
                return result;
            }
        }

        /// <summary>在指定类型上查找字段（继承链查找，缓存由调用方负责）。</summary>
        public static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            if (type == null)
            {
                return null;
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags);
                if (field != null)
                {
                    return field;
                }
            }
            return null;
        }

        /// <summary>在指定类型上查找属性（继承链查找）。</summary>
        public static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
        {
            if (type == null)
            {
                return null;
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, flags);
                if (property != null)
                {
                    return property;
                }
            }
            return null;
        }

        /// <summary>在指定类型上查找方法（继承链查找，支持按参数个数匹配）。</summary>
        public static MethodInfo FindMethod(Type type, string name, BindingFlags flags, int paramCount = -1)
        {
            if (type == null)
            {
                return null;
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo[] methods = current.GetMethods(flags);
                foreach (MethodInfo method in methods)
                {
                    if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (paramCount < 0 || method.GetParameters().Length == paramCount)
                    {
                        return method;
                    }
                }
            }
            return null;
        }

        // ---- 委托化读取 / 写入 -------------------------------------------

        /// <summary>获取成员读取委托（字段或属性）。</summary>
        public static Func<object, object> GetGetter(Type type, string member, bool nonPublic = true)
        {
            MemberKey key = new MemberKey(type, member);
            lock (Sync)
            {
                if (GetterCache.TryGetValue(key, out Func<object, object> getter))
                {
                    return getter;
                }
            }

            Func<object, object> compiled = BuildGetter(type, member, nonPublic);
            lock (Sync)
            {
                GetterCache[key] = compiled;
            }
            return compiled;
        }

        /// <summary>获取成员写入委托（字段或属性）。</summary>
        public static Action<object, object> GetSetter(Type type, string member, bool nonPublic = true)
        {
            MemberKey key = new MemberKey(type, member);
            lock (Sync)
            {
                if (SetterCache.TryGetValue(key, out Action<object, object> setter))
                {
                    return setter;
                }
            }

            Action<object, object> compiled = BuildSetter(type, member, nonPublic);
            lock (Sync)
            {
                SetterCache[key] = compiled;
            }
            return compiled;
        }

        // ---- 缓存方法（供 Invoke / 委托化调用） ---------------------------

        /// <summary>按名称 + 参数个数缓存方法。</summary>
        public static MethodInfo CacheMethod(Type type, string name, int paramCount = -1,
                                             BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        {
            MethodKey key = new MethodKey(type, name, paramCount);
            lock (Sync)
            {
                if (MethodCache.TryGetValue(key, out MethodInfo cached))
                {
                    return cached;
                }
            }

            MethodInfo method = FindMethod(type, name, flags, paramCount);
            lock (Sync)
            {
                MethodCache[key] = method;
            }
            return method;
        }

        // ---- 安全访问（内部已套 Safe） -----------------------------------

        /// <summary>读取成员值（空对象 / 缺失 / 异常安全降级）。</summary>
        public static object Get(object target, string member, object fallback = null)
        {
            return Safe.Run(() =>
            {
                if (target == null)
                {
                    return fallback;
                }
                Func<object, object> getter = GetGetter(target.GetType(), member);
                return getter(target);
            }, fallback, "Reflector.Get(" + member + ")");
        }

        /// <summary>读取成员值并转换为指定类型。</summary>
        public static T Get<T>(object target, string member, T fallback = default(T))
        {
            return Safe.Run(() =>
            {
                if (target == null)
                {
                    return fallback;
                }
                Func<object, object> getter = GetGetter(target.GetType(), member);
                object value = getter(target);
                return value is T typed ? typed : fallback;
            }, fallback, "Reflector.Get<" + typeof(T).Name + ">(" + member + ")");
        }

        /// <summary>写入成员值（空对象 / 缺失 / 异常安全降级）。</summary>
        public static void Set(object target, string member, object value)
        {
            Safe.Run(() =>
            {
                if (target == null)
                {
                    return;
                }
                Action<object, object> setter = GetSetter(target.GetType(), member);
                setter(target, value);
            }, "Reflector.Set(" + member + ")");
        }

        /// <summary>反射调用方法（按名称 + 参数匹配），安全降级。</summary>
        public static object Invoke(object target, string method, params object[] args)
        {
            return Safe.Run(() =>
            {
                if (target == null)
                {
                    return null;
                }

                MethodInfo methodInfo = CacheMethod(target.GetType(), method,
                    args != null ? args.Length : 0);
                if (methodInfo == null)
                {
                    Logger.Warn("Reflector.Invoke 未找到方法：" + target.GetType().Name + "." + method);
                    return null;
                }
                return methodInfo.Invoke(target, args);
            }, null, "Reflector.Invoke(" + method + ")");
        }

        /// <summary>反射调用静态方法（按类型全名 + 方法名 + 参数个数）。</summary>
        public static object InvokeStatic(string typeFullName, string method, params object[] args)
        {
            return Safe.Run(() =>
            {
                Type type = FindType(typeFullName, throwOnMissing: false);
                if (type == null)
                {
                    Logger.Warn("Reflector.InvokeStatic 未找到类型：" + typeFullName);
                    return null;
                }

                MethodInfo methodInfo = CacheMethod(type, method,
                    args != null ? args.Length : 0,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (methodInfo == null)
                {
                    Logger.Warn("Reflector.InvokeStatic 未找到方法：" + typeFullName + "." + method);
                    return null;
                }
                return methodInfo.Invoke(null, args);
            }, null, "Reflector.InvokeStatic(" + typeFullName + "." + method + ")");
        }

        // ---- 委托构造 ------------------------------------------------------

        private static Func<object, object> BuildGetter(Type type, string member, bool nonPublic)
        {
            if (type == null)
            {
                return _ => null;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
            if (nonPublic)
            {
                flags |= BindingFlags.NonPublic;
            }

            // 优先字段，其次属性。
            FieldInfo field = FindField(type, member, flags);
            if (field != null)
            {
                ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
                Expression body = Expression.Field(
                    field.IsStatic ? null : Expression.Convert(instance, field.DeclaringType), field);
                if (!field.IsStatic)
                {
                    // 实例成员：空对象提前返回 null（避免 NullReferenceException）。
                    Expression converted = Expression.Convert(body, typeof(object));
                    return Expression.Lambda<Func<object, object>>(converted, instance).Compile();
                }
                Expression staticConverted = Expression.Convert(body, typeof(object));
                return Expression.Lambda<Func<object, object>>(staticConverted, instance).Compile();
            }

            PropertyInfo property = FindProperty(type, member, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
                // 属性访问走 GetMethod，避免 set-only 属性异常。
                MethodInfo getter = property.GetGetMethod(nonPublic);
                if (getter == null)
                {
                    return _ => null;
                }
                Expression access = getter.IsStatic
                    ? Expression.Call(getter)
                    : Expression.Call(Expression.Convert(instance, property.DeclaringType), getter);
                return Expression.Lambda<Func<object, object>>(
                    Expression.Convert(access, typeof(object)), instance).Compile();
            }

            return _ => null;
        }

        private static Action<object, object> BuildSetter(Type type, string member, bool nonPublic)
        {
            if (type == null)
            {
                return (_, __) => { };
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
            if (nonPublic)
            {
                flags |= BindingFlags.NonPublic;
            }

            FieldInfo field = FindField(type, member, flags);
            if (field != null && !field.IsInitOnly && !field.IsLiteral)
            {
                ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
                ParameterExpression value = Expression.Parameter(typeof(object), "value");
                Expression target = field.IsStatic ? null : Expression.Convert(instance, field.DeclaringType);
                Expression assign = Expression.Assign(
                    Expression.Field(target, field),
                    Expression.Convert(value, field.FieldType));
                return Expression.Lambda<Action<object, object>>(assign, instance, value).Compile();
            }

            PropertyInfo property = FindProperty(type, member, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                MethodInfo setter = property.GetSetMethod(nonPublic);
                if (setter == null)
                {
                    return (_, __) => { };
                }
                ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
                ParameterExpression value = Expression.Parameter(typeof(object), "value");
                Expression call = setter.IsStatic
                    ? Expression.Call(setter, Expression.Convert(value, property.PropertyType))
                    : Expression.Call(
                        Expression.Convert(instance, property.DeclaringType),
                        setter,
                        Expression.Convert(value, property.PropertyType));
                return Expression.Lambda<Action<object, object>>(call, instance, value).Compile();
            }

            return (_, __) => { };
        }
    }
}
