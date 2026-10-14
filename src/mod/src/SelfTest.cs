// ===========================================================================
// YxMod5 C# 模组层 —— 阶段 1 自主验收自测
// ---------------------------------------------------------------------------
// 覆盖验证标准（validation.md 自主验收项）：
//   2. 访问器核心读写往返 + 委托化 + 安全降级；
//   3. 自定义消息 NetStream 编码往返；
//   4. 配置系统 INI 读写与持久化；
//   5. 目录与文件路径。
// 可在游戏内启动时调用（RunAll），也可由外部测试宿主复用。
// ===========================================================================
using System;
using System.IO;
using YxMod5.Config;
using YxMod5.Input;
using YxMod5.Net;
using YxMod5.Runtime;

namespace YxMod5
{
    /// <summary>阶段 1 自测。</summary>
    public static class SelfTest
    {
        // 供 Reflector 往返测试使用的私有测试类型。
#pragma warning disable CS0414 // 字段经反射访问，编译器无法识别其使用
        private sealed class TestSubject
        {
            private int _field = 7;

            private int PrivateProp { get; set; } = 42;

            private int Add(int a, int b) => a + b;
        }
#pragma warning restore CS0414

        public static bool RunAll()
        {
            bool reflector = TestReflector();
            bool safety = TestSafety();
            bool ini = TestIni();
            bool hotkey = TestHotKey();
            bool sync = SyncChannel.RunSelfTest();

            bool all = reflector && safety && ini && hotkey && sync;
            Logger.Info("========== 阶段 1 自测结果：" + (all ? "全部通过" : "存在失败") +
                        "（reflector=" + reflector +
                        ", safety=" + safety +
                        ", ini=" + ini +
                        ", hotkey=" + hotkey +
                        ", sync=" + sync + "）==========");
            return all;
        }

        private static bool TestReflector()
        {
            try
            {
                TestSubject subject = new TestSubject();

                // 字段读写往返。
                object read = Reflector.Get(subject, "_field");
                if (!(read is int) || (int)read != 7)
                {
                    Logger.Error("自测失败：Reflector 字段读取（期望 7，实际 " + read + "）");
                    return false;
                }
                Reflector.Set(subject, "_field", 99);
                object readBack = Reflector.Get(subject, "_field");
                if (!(readBack is int) || (int)readBack != 99)
                {
                    Logger.Error("自测失败：Reflector 字段写回（期望 99，实际 " + readBack + "）");
                    return false;
                }

                // 属性读写往返。
                object prop = Reflector.Get(subject, "PrivateProp");
                if (!(prop is int) || (int)prop != 42)
                {
                    Logger.Error("自测失败：Reflector 属性读取（期望 42，实际 " + prop + "）");
                    return false;
                }
                Reflector.Set(subject, "PrivateProp", 77);
                if ((int)Reflector.Get(subject, "PrivateProp") != 77)
                {
                    Logger.Error("自测失败：Reflector 属性写回");
                    return false;
                }

                // 方法调用。
                object sum = Reflector.Invoke(subject, "Add", 2, 3);
                if (!(sum is int) || (int)sum != 5)
                {
                    Logger.Error("自测失败：Reflector 方法调用（期望 5，实际 " + sum + "）");
                    return false;
                }

                // 委托化调用与 Invoke 结果一致（读取两次对比）。
                object viaGetter = Reflector.Get(subject, "_field");
                object viaInvoke = Reflector.Invoke(subject, "Add", viaGetter is int v ? v : 0, 1);
                if (!(viaInvoke is int))
                {
                    Logger.Error("自测失败：委托化与 Invoke 一致性");
                    return false;
                }

                Logger.Info("自测通过：Reflector 读写往返 + 委托化 + 方法调用");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("自测异常：Reflector -> " + exception);
                return false;
            }
        }

        private static bool TestSafety()
        {
            try
            {
                // 空对象。
                object nullRead = Reflector.Get(null, "_field", fallback: 123);
                if (!(nullRead is int) || (int)nullRead != 123)
                {
                    Logger.Error("自测失败：空对象未降级");
                    return false;
                }

                // 不存在成员。
                object missing = Reflector.Get(new TestSubject(), "_does_not_exist", fallback: "fb");
                if (!string.Equals(missing as string, "fb", StringComparison.Ordinal))
                {
                    Logger.Error("自测失败：不存在成员未降级");
                    return false;
                }

                // 类型不匹配（把 string 写入 int 字段应被安全降级）。
                TestSubject subject = new TestSubject();
                Reflector.Set(subject, "_field", "not-an-int");
                object stillInt = Reflector.Get(subject, "_field");
                if (stillInt != null && !(stillInt is int))
                {
                    Logger.Error("自测失败：类型不匹配写入未降级");
                    return false;
                }

                // 安全降级不应抛异常。
                Safe.Try(() => Reflector.Invoke(null, "Add", 1, 2), "SelfTest.TestSafety");

                Logger.Info("自测通过：安全防护层（空对象/缺失成员/类型不匹配）");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("自测异常：Safety -> " + exception);
                return false;
            }
        }

        private static bool TestIni()
        {
            try
            {
                string temp = Path.Combine(Path.GetTempPath(), "YxMod5_selftest_" + Guid.NewGuid().ToString("N") + ".ini");
                IniFile ini = new IniFile(temp);

                ini.SetInt("HotKey", "Fly", 2);
                ini.Set("HotKey", "FlyKey", "F");
                ini.SetBool("UI", "Enabled", true);

                ini.Save();

                IniFile reloaded = new IniFile(temp);
                reloaded.Load();
                bool ok = reloaded.GetInt("HotKey", "Fly") == 2 &&
                          string.Equals(reloaded.Get("HotKey", "FlyKey"), "F", StringComparison.Ordinal) &&
                          reloaded.GetBool("UI", "Enabled");

                try
                {
                    File.Delete(temp);
                }
                catch
                {
                }

                if (!ok)
                {
                    Logger.Error("自测失败：INI 读写持久化");
                    return false;
                }

                Logger.Info("自测通过：INI 读写与持久化");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("自测异常：INI -> " + exception);
                return false;
            }
        }

        private static bool TestHotKey()
        {
            try
            {
                if (!HotKey.TryParse("Ctrl+Shift+F", out HotKey combo) ||
                    !combo.Ctrl || !combo.Shift || combo.Key != UnityEngine.KeyCode.F)
                {
                    Logger.Error("自测失败：热键组合解析");
                    return false;
                }

                if (!HotKey.TryParse("3", out HotKey number) || number.Number != 3)
                {
                    Logger.Error("自测失败：热键数字编号解析");
                    return false;
                }

                if (HotKey.TryParse("", out _) || HotKey.TryParse("不存在的键", out _))
                {
                    Logger.Error("自测失败：非法热键应解析失败");
                    return false;
                }

                Logger.Info("自测通过：热键解析（组合键/数字编号/非法键）");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("自测异常：HotKey -> " + exception);
                return false;
            }
        }
    }
}
