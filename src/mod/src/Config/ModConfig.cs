// ===========================================================================
// YxMod5 C# 模组层 —— 配置系统（roadmap 1.5）
// ---------------------------------------------------------------------------
// 分层约定：
//   * INI（<游戏根目录>\YxMod5\YxMod5.ini）：热键、显示等设置；
//   * PlayerPrefs：功能状态（按 Unity 默认 Company/Product 作用域隔离）。
//
// 对外提供统一的 Get<T> / Set<T>，按 ConfigScope 选择后端。
// ===========================================================================
using System;
using UnityEngine;
using YxMod5.Runtime;

namespace YxMod5.Config
{
    /// <summary>配置后端。</summary>
    public enum ConfigScope
    {
        /// <summary>INI 文件（热键 / 显示等）。</summary>
        Ini,

        /// <summary>PlayerPrefs（功能状态）。</summary>
        PlayerPrefs
    }

    /// <summary>统一配置访问入口。</summary>
    public static class ModConfig
    {
        private const string DefaultSection = "YxMod5";

        private static IniFile _ini;
        private static readonly object Sync = new object();

        /// <summary>当前 INI 实例（懒加载，自动 Load）。</summary>
        public static IniFile Ini
        {
            get
            {
                lock (Sync)
                {
                    if (_ini == null)
                    {
                        ModPaths.EnsureCreated();
                        _ini = new IniFile(ModPaths.IniPath);
                        _ini.Load();
                    }
                    return _ini;
                }
            }
        }

        // ---- 泛型读 -------------------------------------------------------

        public static T Get<T>(ConfigScope scope, string key, T fallback = default(T))
        {
            return Safe.Run(() =>
            {
                if (scope == ConfigScope.Ini)
                {
                    return GetIni(DefaultSection, key, fallback);
                }
                return GetPrefs(key, fallback);
            }, fallback, "ModConfig.Get(" + scope + "." + key + ")");
        }

        public static T Get<T>(ConfigScope scope, string section, string key, T fallback = default(T))
        {
            return Safe.Run(() =>
            {
                if (scope == ConfigScope.Ini)
                {
                    return GetIni(section, key, fallback);
                }
                return GetPrefs(key, fallback);
            }, fallback, "ModConfig.Get(" + scope + "." + key + ")");
        }

        // ---- 泛型写 -------------------------------------------------------

        public static void Set<T>(ConfigScope scope, string key, T value)
        {
            Safe.Run(() =>
            {
                if (scope == ConfigScope.Ini)
                {
                    SetIni(DefaultSection, key, value);
                }
                else
                {
                    SetPrefs(key, value);
                }
            }, "ModConfig.Set(" + scope + "." + key + ")");
        }

        public static void Set<T>(ConfigScope scope, string section, string key, T value)
        {
            Safe.Run(() =>
            {
                if (scope == ConfigScope.Ini)
                {
                    SetIni(section, key, value);
                }
                else
                {
                    SetPrefs(key, value);
                }
            }, "ModConfig.Set(" + scope + "." + key + ")");
        }

        /// <summary>持久化 INI（PlayerPrefs 由 Unity 自动持久化）。</summary>
        public static void Save()
        {
            Safe.Run(() => Ini.Save(), "ModConfig.Save");
        }

        // ---- 内部 ---------------------------------------------------------

        private static T GetIni<T>(string section, string key, T fallback)
        {
            Type type = typeof(T);
            object result;
            if (type == typeof(bool))
            {
                result = Ini.GetBool(section, key, (bool)(object)fallback);
            }
            else if (type == typeof(int))
            {
                result = Ini.GetInt(section, key, (int)(object)fallback);
            }
            else if (type == typeof(float))
            {
                result = Ini.GetFloat(section, key, (float)(object)fallback);
            }
            else if (type == typeof(string))
            {
                result = Ini.Get(section, key, (string)(object)fallback);
            }
            else
            {
                result = Ini.Get(section, key, fallback != null ? fallback.ToString() : null);
            }
            return result is T typed ? typed : fallback;
        }

        private static void SetIni<T>(string section, string key, T value)
        {
            Type type = typeof(T);
            if (type == typeof(bool))
            {
                Ini.SetBool(section, key, (bool)(object)value);
            }
            else if (type == typeof(int))
            {
                Ini.SetInt(section, key, (int)(object)value);
            }
            else if (type == typeof(float))
            {
                Ini.SetFloat(section, key, (float)(object)value);
            }
            else
            {
                Ini.Set(section, key, value != null ? value.ToString() : string.Empty);
            }
        }

        private static T GetPrefs<T>(string key, T fallback)
        {
            Type type = typeof(T);
            object result;
            if (type == typeof(bool))
            {
                result = PlayerPrefs.GetInt(key, (bool)(object)fallback ? 1 : 0) != 0;
            }
            else if (type == typeof(int))
            {
                result = PlayerPrefs.GetInt(key, (int)(object)fallback);
            }
            else if (type == typeof(float))
            {
                result = PlayerPrefs.GetFloat(key, (float)(object)fallback);
            }
            else if (type == typeof(string))
            {
                result = PlayerPrefs.GetString(key, (string)(object)fallback);
            }
            else
            {
                result = fallback;
            }
            return result is T typed ? typed : fallback;
        }

        private static void SetPrefs<T>(string key, T value)
        {
            Type type = typeof(T);
            if (type == typeof(bool))
            {
                PlayerPrefs.SetInt(key, (bool)(object)value ? 1 : 0);
            }
            else if (type == typeof(int))
            {
                PlayerPrefs.SetInt(key, (int)(object)value);
            }
            else if (type == typeof(float))
            {
                PlayerPrefs.SetFloat(key, (float)(object)value);
            }
            else if (type == typeof(string))
            {
                PlayerPrefs.SetString(key, (string)(object)value);
            }
        }
    }
}
