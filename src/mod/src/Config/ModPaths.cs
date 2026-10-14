// ===========================================================================
// YxMod5 C# 模组层 —— 落地目录管理（roadmap 1.5）
// ---------------------------------------------------------------------------
// 统一「所有模组文件放 <游戏根目录>\YxMod5\」约定：
//   * 配置文件 YxMod5.ini
//   * 日志文件 YxMod5.log（由阶段 0 的根目录 YxMod5.log 迁移至此）
// ===========================================================================
using System;
using System.IO;
using YxMod5.Runtime;

namespace YxMod5.Config
{
    /// <summary>模组落地目录与路径。</summary>
    public static class ModPaths
    {
        private static string _root;
        private static bool _rootResolved;

        /// <summary>模组根目录：&lt;游戏根目录&gt;\YxMod5\</summary>
        public static string Root
        {
            get
            {
                Resolve();
                return _root;
            }
        }

        /// <summary>配置文件完整路径。</summary>
        public static string IniPath => Safe.Run(() => Path.Combine(Root, "YxMod5.ini"), null, "ModPaths.IniPath");

        /// <summary>日志文件完整路径。</summary>
        public static string LogPath => Safe.Run(() => Path.Combine(Root, "YxMod5.log"), null, "ModPaths.LogPath");

        /// <summary>确保根目录存在，返回是否成功。</summary>
        public static bool EnsureCreated()
        {
            return Safe.Run(() =>
            {
                string root = Root;
                if (string.IsNullOrEmpty(root))
                {
                    return false;
                }
                Directory.CreateDirectory(root);
                return Directory.Exists(root);
            }, false, "ModPaths.EnsureCreated");
        }

        private static void Resolve()
        {
            if (_rootResolved)
            {
                return;
            }
            _rootResolved = true;

            // 优先使用进程 exe 目录（可从原生注入线程安全调用）。
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string gameDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(gameDir))
                {
                    _root = Path.Combine(gameDir, "YxMod5");
                    return;
                }
            }
            catch
            {
                // 忽略，回退。
            }

            try
            {
                _root = Path.Combine(Directory.GetCurrentDirectory(), "YxMod5");
            }
            catch
            {
                _root = null;
            }
        }
    }
}
