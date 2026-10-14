// ===========================================================================
// YxMod5 C# 模组层 —— 自研 INI 读写（roadmap 1.5）
// ---------------------------------------------------------------------------
// 精简 INI 实现：UTF-8 无 BOM、分节（[Section]）、key=value 注释（; / #）。
// 保留顺序与注释不保证（本实现为键值映射式，够用且稳定）。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using YxMod5.Runtime;

namespace YxMod5.Config
{
    /// <summary>自研 INI 文件读写。</summary>
    public sealed class IniFile
    {
        private readonly string _path;
        private readonly object _sync = new object();
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public IniFile(string path)
        {
            _path = path;
        }

        /// <summary>文件路径。</summary>
        public string Path => _path;

        /// <summary>加载（不存在则返回空）。</summary>
        public bool Load()
        {
            return Safe.Run(() =>
            {
                lock (_sync)
                {
                    _sections.Clear();
                    if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
                    {
                        return false;
                    }

                    string current = string.Empty;
                    foreach (string rawLine in File.ReadAllLines(_path, Utf8NoBom))
                    {
                        string line = rawLine.Trim();
                        if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                        {
                            continue;
                        }

                        if (line[0] == '[' && line[line.Length - 1] == ']')
                        {
                            current = line.Substring(1, line.Length - 2).Trim();
                            if (!_sections.ContainsKey(current))
                            {
                                _sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            }
                            continue;
                        }

                        int eq = line.IndexOf('=');
                        if (eq <= 0)
                        {
                            continue;
                        }

                        string key = line.Substring(0, eq).Trim();
                        string value = line.Substring(eq + 1).Trim();
                        SetValue(current, key, value);
                    }
                    return true;
                }
            }, false, "IniFile.Load");
        }

        /// <summary>保存（UTF-8 无 BOM，自动建目录）。</summary>
        public bool Save()
        {
            return Safe.Run(() =>
            {
                lock (_sync)
                {
                    if (string.IsNullOrEmpty(_path))
                    {
                        return false;
                    }

                    string dir = System.IO.Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    StringBuilder sb = new StringBuilder();
                    foreach (KeyValuePair<string, Dictionary<string, string>> section in _sections)
                    {
                        sb.Append('[').Append(section.Key).AppendLine("]");
                        foreach (KeyValuePair<string, string> pair in section.Value)
                        {
                            sb.Append(pair.Key).Append('=').AppendLine(pair.Value);
                        }
                        sb.AppendLine();
                    }
                    File.WriteAllText(_path, sb.ToString(), Utf8NoBom);
                    return true;
                }
            }, false, "IniFile.Save");
        }

        // ---- 读取 ---------------------------------------------------------

        public string Get(string section, string key, string fallback = null)
        {
            lock (_sync)
            {
                if (_sections.TryGetValue(section ?? string.Empty, out Dictionary<string, string> map) &&
                    map.TryGetValue(key, out string value))
                {
                    return value;
                }
            }
            return fallback;
        }

        public int GetInt(string section, string key, int fallback = 0)
        {
            string value = Get(section, key);
            return int.TryParse(value, out int result) ? result : fallback;
        }

        public bool GetBool(string section, string key, bool fallback = false)
        {
            string value = Get(section, key);
            if (value == null)
            {
                return fallback;
            }
            if (bool.TryParse(value, out bool result))
            {
                return result;
            }
            if (int.TryParse(value, out int num))
            {
                return num != 0;
            }
            return fallback;
        }

        public float GetFloat(string section, string key, float fallback = 0f)
        {
            string value = Get(section, key);
            return float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result)
                ? result
                : fallback;
        }

        // ---- 写入 ---------------------------------------------------------

        public void Set(string section, string key, string value)
        {
            lock (_sync)
            {
                SetValue(section ?? string.Empty, key, value);
            }
        }

        public void SetInt(string section, string key, int value)
        {
            Set(section, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void SetBool(string section, string key, bool value)
        {
            Set(section, key, value ? "true" : "false");
        }

        public void SetFloat(string section, string key, float value)
        {
            Set(section, key, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        private void SetValue(string section, string key, string value)
        {
            if (!_sections.TryGetValue(section, out Dictionary<string, string> map))
            {
                map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections[section] = map;
            }
            map[key] = value ?? string.Empty;
        }
    }
}
