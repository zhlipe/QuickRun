// QuickRun —— 把 exe / 快捷方式拖进来，自动生成 Win+R 别名
// 编译：见 build.ps1（.NET Framework 4.x、无需管理员权限）
// 运行：双击 QuickRun.exe；快捷方式目录会自动加入当前用户 PATH。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("QuickRun")]
[assembly: AssemblyDescription("拖入 exe / 快捷方式，自动生成 Win+R 别名并维护 PATH")]
[assembly: AssemblyProduct("QuickRun")]
[assembly: AssemblyCompany("QuickRun")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace QuickRun
{
    // =================================================================
    // 主题
    // =================================================================
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(244, 246, 250);
        public static readonly Color Card = Color.White;
        public static readonly Color Text = Color.FromArgb(31, 41, 55);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Border = Color.FromArgb(229, 231, 235);
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color PrimaryDark = Color.FromArgb(29, 78, 216);
        public static readonly Color PrimarySoft = Color.FromArgb(239, 246, 255);
        public static readonly Color DropBg = Color.FromArgb(243, 248, 255);
        public static readonly Color DropBorder = Color.FromArgb(147, 197, 253);
        public static readonly Color DropHot = Color.FromArgb(219, 234, 254);
        public static readonly Color Success = Color.FromArgb(5, 150, 105);
        public static readonly Color SuccessSoft = Color.FromArgb(220, 252, 231);
        public static readonly Color Warn = Color.FromArgb(180, 83, 9);
        public static readonly Color WarnSoft = Color.FromArgb(254, 243, 199);
        public static readonly Color Danger = Color.FromArgb(185, 28, 28);
        public static readonly Color DangerSoft = Color.FromArgb(254, 226, 226);
        public static readonly Color BannerBg = Color.FromArgb(255, 247, 237);

        public static readonly Font FontUI = MakeFont(9F, FontStyle.Regular);
        public static readonly Font FontSmall = MakeFont(8.25F, FontStyle.Regular);
        public static readonly Font FontBold = MakeFont(9F, FontStyle.Bold);
        public static readonly Font FontTitle = MakeFont(16F, FontStyle.Bold);
        public static readonly Font FontSubtitle = MakeFont(9.5F, FontStyle.Regular);
        public static readonly Font FontDropTitle = MakeFont(13F, FontStyle.Bold);
        public static readonly Font FontDialogTitle = MakeFont(13F, FontStyle.Bold);

        public static Font MakeFont(float size, FontStyle style)
        {
            string[] families = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            foreach (string family in families)
            {
                try
                {
                    Font f = new Font(family, size, style, GraphicsUnit.Point);
                    if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase))
                        return f;
                    f.Dispose();
                }
                catch
                {
                }
            }
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            if (radius < 1) radius = 1;
            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(rect.X, rect.Y, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // =================================================================
    // 应用路径 / 目录选择
    // =================================================================
    internal static class AppEnv
    {
        public const string AppName = "QuickRun";
        public const string Version = "1.0.0";

        public static string Root = "";
        public static string AliasDir = "";
        public static string LegacyAliasDir = "";
        public static string DataDir = "";
        public static string ConfigFile = "";
        public static string LogFile = "";
        public static string SettingsFile = "";
        public static string AliasDirWarning = "";
        public static bool AliasDirFromEnvironment = false;

        public static bool IsFallbackDataDir()
        {
            try
            {
                string expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickRunData");
                return string.Equals(Path.GetFullPath(Root).TrimEnd('\\'), Path.GetFullPath(expected).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void Initialize()
        {
            string explicitRoot = Environment.GetEnvironmentVariable("QUICKRUN_HOME");
            string explicitAlias = Environment.GetEnvironmentVariable("QUICKRUN_ALIAS_DIR");
            AliasDirWarning = "";
            AliasDirFromEnvironment = false;

            // 1) 配置与日志目录（固定放在用户目录，避免污染快捷方式目录）
            List<string> dataCandidates = new List<string>();
            if (!string.IsNullOrEmpty(explicitRoot))
                dataCandidates.Add(explicitRoot);
            dataCandidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "QuickRun"));
            dataCandidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickRun"));
            dataCandidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickRunData"));

            Exception lastError = null;
            bool dataReady = false;
            foreach (string candidate in dataCandidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                try
                {
                    string root = Path.GetFullPath(candidate);
                    string dataDir = Path.Combine(root, "data");
                    Directory.CreateDirectory(root);
                    Directory.CreateDirectory(dataDir);
                    string probe = Path.Combine(dataDir, ".write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    Root = root;
                    DataDir = dataDir;
                    ConfigFile = Path.Combine(dataDir, "config.json");
                    LogFile = Path.Combine(dataDir, "quickrun.log");
                    SettingsFile = Path.Combine(dataDir, "settings.json");
                    dataReady = true;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
            if (!dataReady)
                throw new IOException("找不到可写的 QuickRun 数据目录。请检查 %USERPROFILE% 的写权限，或设置 QUICKRUN_HOME 指向一个可写目录。",
                    lastError);

            // 2) 快捷方式目录：优先读取用户设置；默认 D:\Quick；测试/绿色版可用环境变量覆盖
            AppSettings settings = new AppSettings();
            string settingsWarning;
            try
            {
                settings = SettingsStore.Load(DataDir, out settingsWarning);
            }
            catch (Exception ex)
            {
                settingsWarning = ex.Message;
            }
            if (!string.IsNullOrEmpty(settingsWarning))
            {
                AliasDirWarning = settingsWarning;
                Log.Warn("读取设置失败：" + settingsWarning);
            }

            string configuredAlias = settings == null || settings.AliasDir == null ? "" : settings.AliasDir.Trim();
            if (!string.IsNullOrEmpty(explicitAlias))
            {
                configuredAlias = explicitAlias.Trim();
                AliasDirFromEnvironment = true;
            }

            List<string> aliasCandidates = new List<string>();
            if (!string.IsNullOrEmpty(configuredAlias))
                aliasCandidates.Add(configuredAlias);
            if (string.IsNullOrEmpty(explicitRoot) && string.IsNullOrEmpty(configuredAlias))
                aliasCandidates.Add(@"D:\Quick");
            aliasCandidates.Add(Path.Combine(Root, "aliases"));
            aliasCandidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickRunData", "aliases"));

            bool aliasReady = false;
            Exception configuredError = null;
            foreach (string candidate in aliasCandidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                try
                {
                    string aliasDir = Path.GetFullPath(candidate);
                    Directory.CreateDirectory(aliasDir);
                    string probe = Path.Combine(aliasDir, ".quickrun-write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    AliasDir = aliasDir;
                    aliasReady = true;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (configuredError == null && !string.IsNullOrEmpty(configuredAlias) &&
                        SameDirectory(candidate, configuredAlias))
                        configuredError = ex;
                }
            }
            if (!aliasReady)
            {
                // 保留一个可展示的候选路径，启动后由界面提示无法写入。
                try { AliasDir = Path.GetFullPath(Path.Combine(Root, "aliases")); }
                catch { AliasDir = Path.Combine(Root, "aliases"); }
            }
            else if (!string.IsNullOrEmpty(configuredAlias) && !SameDirectory(AliasDir, configuredAlias))
            {
                AliasDirWarning = "已保存的快捷方式目录不可用：" + configuredAlias +
                    "（" + (configuredError != null ? configuredError.Message : "无法写入") + "），已临时改用 " + AliasDir + "。";
                Log.Warn(AliasDirWarning);
            }

            LegacyAliasDir = Path.Combine(Root, "aliases");
        }

        public static bool SameDirectory(string a, string b)
        {
            try
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
                string na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsWritableDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // =================================================================
    // 日志
    // =================================================================
    internal static class Log
    {
        private static readonly object Sync = new object();

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }

        public static void Error(string message, Exception ex)
        {
            if (ex == null) Write("ERROR", message);
            else Write("ERROR", message + " | " + ex.GetType().Name + ": " + ex.Message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    if (string.IsNullOrEmpty(AppEnv.LogFile)) return;
                    Directory.CreateDirectory(AppEnv.DataDir);
                    FileInfo fi = new FileInfo(AppEnv.LogFile);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = AppEnv.LogFile + ".old";
                        try
                        {
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(AppEnv.LogFile, old);
                        }
                        catch
                        {
                        }
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level.PadRight(5) + "] " + message + "\r\n";
                    File.AppendAllText(AppEnv.LogFile, line, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }
    }

    // =================================================================
    // 数据模型 / 配置
    // =================================================================
    public sealed class AliasEntry
    {
        public string Name { get; set; }
        public string Target { get; set; }
        public string Note { get; set; }
        public string CreatedAt { get; set; }

        public AliasEntry()
        {
            Name = "";
            Target = "";
            Note = "";
            CreatedAt = "";
        }

        public AliasEntry Clone()
        {
            AliasEntry c = new AliasEntry();
            c.Name = Name;
            c.Target = Target;
            c.Note = Note;
            c.CreatedAt = CreatedAt;
            return c;
        }
    }

    public sealed class AppConfig
    {
        public int Version { get; set; }
        public List<AliasEntry> Aliases { get; set; }

        public AppConfig()
        {
            Version = 1;
            Aliases = new List<AliasEntry>();
        }
    }

    internal static class ConfigStore
    {
        public static string Serialize(AppConfig config)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 4 * 1024 * 1024;
            return js.Serialize(config);
        }

        public static AppConfig Deserialize(string json)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 4 * 1024 * 1024;
            AppConfig config = js.Deserialize<AppConfig>(json);
            if (config == null) config = new AppConfig();
            if (config.Aliases == null) config.Aliases = new List<AliasEntry>();
            foreach (AliasEntry e in config.Aliases)
            {
                if (e.Name == null) e.Name = "";
                if (e.Target == null) e.Target = "";
                if (e.Note == null) e.Note = "";
                if (e.CreatedAt == null) e.CreatedAt = "";
            }
            return config;
        }

        public static AppConfig Load(out string warning)
        {
            warning = null;
            try
            {
                if (string.IsNullOrEmpty(AppEnv.ConfigFile) || !File.Exists(AppEnv.ConfigFile))
                    return new AppConfig();
                string json = File.ReadAllText(AppEnv.ConfigFile, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    return new AppConfig();
                return Deserialize(json);
            }
            catch (Exception ex)
            {
                warning = "配置文件读取失败：" + ex.Message;
                try
                {
                    string backup = AppEnv.ConfigFile + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                    File.Move(AppEnv.ConfigFile, backup);
                    warning += "（原文件已备份为 " + Path.GetFileName(backup) + "）";
                }
                catch
                {
                }
                Log.Error("读取配置失败", ex);
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            if (string.IsNullOrEmpty(AppEnv.ConfigFile))
                throw new InvalidOperationException("配置目录尚未初始化。");
            Directory.CreateDirectory(AppEnv.DataDir);
            string json = Serialize(config);
            string tmp = AppEnv.ConfigFile + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(AppEnv.ConfigFile))
            {
                try
                {
                    File.Replace(tmp, AppEnv.ConfigFile, null);
                }
                catch
                {
                    File.Copy(tmp, AppEnv.ConfigFile, true);
                    File.Delete(tmp);
                }
            }
            else
            {
                File.Move(tmp, AppEnv.ConfigFile);
            }
        }
    }

    // =================================================================
    // 用户设置（快捷方式存放目录）
    // =================================================================
    public sealed class AppSettings
    {
        public string AliasDir { get; set; }

        public AppSettings()
        {
            AliasDir = "";
        }
    }

    internal static class SettingsStore
    {
        public const string FileName = "settings.json";

        public static AppSettings Load(string dataDir, out string warning)
        {
            warning = null;
            AppSettings settings = new AppSettings();
            try
            {
                if (string.IsNullOrEmpty(dataDir)) return settings;
                string path = Path.Combine(dataDir, FileName);
                if (!File.Exists(path)) return settings;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return settings;
                JavaScriptSerializer js = new JavaScriptSerializer();
                AppSettings loaded = js.Deserialize<AppSettings>(json);
                if (loaded != null && loaded.AliasDir != null)
                    settings.AliasDir = loaded.AliasDir.Trim();
            }
            catch (Exception ex)
            {
                warning = "设置文件读取失败：" + ex.Message;
            }
            return settings;
        }

        public static void Save(AppSettings settings, string dataDir)
        {
            if (string.IsNullOrEmpty(dataDir))
                throw new InvalidOperationException("设置目录尚未初始化。");
            Directory.CreateDirectory(dataDir);
            JavaScriptSerializer js = new JavaScriptSerializer();
            string json = js.Serialize(settings);
            string path = Path.Combine(dataDir, FileName);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                }
                catch
                {
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }

    // =================================================================
    // 别名规则
    // =================================================================
    internal static class AliasRules
    {
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "CLOCK$"
        };

        public static bool IsReserved(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string baseName = name;
            int dot = baseName.IndexOf('.');
            if (dot >= 0) baseName = baseName.Substring(0, dot);
            return Reserved.Contains(baseName.Trim());
        }

        public static string FromTarget(string target)
        {
            string name = "";
            try
            {
                if (Directory.Exists(target))
                {
                    string trimmed = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    name = Path.GetFileName(trimmed);
                }
                else
                {
                    name = Path.GetFileNameWithoutExtension(target);
                }
            }
            catch
            {
            }
            if (string.IsNullOrWhiteSpace(name)) name = "app";
            return Normalize(name);
        }

        public static string Normalize(string raw)
        {
            if (raw == null) raw = "";
            StringBuilder sb = new StringBuilder();
            bool lastDash = true;
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    lastDash = false;
                }
                else if (c == ' ' || c == '\t' || c == '-' || c == '_')
                {
                    if (!lastDash && sb.Length > 0)
                    {
                        sb.Append('-');
                        lastDash = true;
                    }
                }
            }
            string result = sb.ToString().Trim('-');
            if (result.Length > 36)
            {
                result = result.Substring(0, 36).TrimEnd('-');
            }
            if (result.Length == 0) result = "app";
            if (!char.IsLetterOrDigit(result[0])) result = "app-" + result;
            if (IsReserved(result)) result = "run-" + result;
            if (result.Length > 40) result = result.Substring(0, 40).TrimEnd('-');
            return result;
        }

        public static bool Validate(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "别名不能为空。";
                return false;
            }
            name = name.Trim();
            if (name.Length > 40)
            {
                error = "别名最多 40 个字符。";
                return false;
            }
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                {
                    error = "别名只能包含字母、数字、短横线（-）和下划线（_）。";
                    return false;
                }
            }
            if (!char.IsLetterOrDigit(name[0]))
            {
                error = "别名必须以字母或数字开头。";
                return false;
            }
            if (IsReserved(name))
            {
                error = "“" + name + "”是 Windows 保留名，不能用作别名。";
                return false;
            }
            return true;
        }

        public static string TrimAlias(string preferred, string suffix)
        {
            int max = 40 - suffix.Length;
            if (max < 1) max = 1;
            string root = preferred;
            if (root.Length > max) root = root.Substring(0, max);
            root = root.TrimEnd('-', '_');
            if (root.Length == 0) root = "app";
            return root + suffix;
        }

        public static bool NameExists(string name, IEnumerable<AliasEntry> entries)
        {
            foreach (AliasEntry e in entries)
            {
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static string MakeUnique(string preferred, IList<AliasEntry> entries, bool checkExternalCommand)
        {
            return MakeUnique(preferred, entries, checkExternalCommand, null);
        }

        public static string MakeUnique(string preferred, IList<AliasEntry> entries, bool checkExternalCommand, string targetPath)
        {
            string baseName = Normalize(preferred);
            if (checkExternalCommand && ExternalCommandConflicts(baseName, targetPath))
                baseName = TrimAlias(baseName, "-run");

            string candidate = baseName;
            for (int i = 2; i < 10000; i++)
            {
                bool taken = NameExists(candidate, entries) || AliasFile.AnyFileExists(candidate);
                if (!taken && checkExternalCommand && ExternalCommandConflicts(candidate, targetPath))
                    taken = true;
                if (!taken) return candidate;
                candidate = TrimAlias(baseName, "-" + i.ToString());
            }
            return TrimAlias(baseName, "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        }

        public static bool ExternalCommandConflicts(string name, string targetPath)
        {
            string commandPath = ExternalCommandPath(name);
            if (string.IsNullOrEmpty(commandPath)) return false;
            if (!string.IsNullOrEmpty(targetPath))
            {
                string commandNorm = PathManager.NormalizeEntry(commandPath);
                string targetNorm = PathManager.NormalizeEntry(targetPath);
                if (commandNorm.Length > 0 && string.Equals(commandNorm, targetNorm, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        public static bool ExternalCommandExists(string name)
        {
            return !string.IsNullOrEmpty(ExternalCommandPath(name));
        }

        public static string ExternalCommandPath(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return null;
                string path = Environment.GetEnvironmentVariable("Path");
                if (string.IsNullOrEmpty(path)) return null;
                List<string> exts = new List<string>();
                string pathExt = Environment.GetEnvironmentVariable("PATHEXT");
                if (string.IsNullOrEmpty(pathExt)) pathExt = ".COM;.EXE;.BAT;.CMD";
                foreach (string ext in pathExt.Split(';'))
                {
                    string trimmedExt = ext.Trim();
                    if (trimmedExt.Length == 0) continue;
                    if (!trimmedExt.StartsWith(".", StringComparison.Ordinal)) trimmedExt = "." + trimmedExt;
                    exts.Add(trimmedExt);
                }
                string aliasNorm = PathManager.NormalizeEntry(AppEnv.AliasDir);
                foreach (string rawDir in path.Split(';'))
                {
                    string dir = PathManager.NormalizeEntry(rawDir);
                    if (dir.Length == 0) continue;
                    if (string.Equals(dir, aliasNorm, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string ext in exts)
                    {
                        try
                        {
                            string candidate = Path.Combine(dir, name + ext);
                            if (File.Exists(candidate)) return candidate;
                        }
                        catch
                        {
                        }
                    }
                    try
                    {
                        string plain = Path.Combine(dir, name);
                        if (File.Exists(plain)) return plain;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return null;
        }
    }

    // =================================================================
    // 快捷方式解析
    // =================================================================
    internal static class Shortcuts
    {
        public static string ResolveIfLink(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return path;
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                if (shortcut == null) return path;
                object target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                string resolved = Convert.ToString(target);
                if (string.IsNullOrWhiteSpace(resolved)) return path;
                return Environment.ExpandEnvironmentVariables(resolved);
            }
            catch
            {
                return path;
            }
            finally
            {
                if (shortcut != null)
                {
                    try { Marshal.FinalReleaseComObject(shortcut); }
                    catch { }
                }
                if (shell != null)
                {
                    try { Marshal.FinalReleaseComObject(shell); }
                    catch { }
                }
            }
        }
    }

    // =================================================================
    // 别名快捷方式（.lnk）
    // =================================================================
    internal static class AliasFile
    {
        public const string Extension = ".lnk";

        public static string PathFor(AliasEntry entry)
        {
            return PathForIn(entry, AppEnv.AliasDir);
        }

        public static string PathForIn(AliasEntry entry, string aliasDir)
        {
            return Path.Combine(aliasDir, entry.Name + Extension);
        }

        public static string PathForName(string name)
        {
            return PathForNameIn(name, AppEnv.AliasDir);
        }

        public static string PathForNameIn(string name, string aliasDir)
        {
            return Path.Combine(aliasDir, name + Extension);
        }

        public static bool Exists(AliasEntry entry)
        {
            try
            {
                return File.Exists(PathFor(entry));
            }
            catch
            {
                return false;
            }
        }

        public static bool AnyFileExists(string name)
        {
            return AnyFileExistsIn(name, AppEnv.AliasDir);
        }

        public static bool AnyFileExistsIn(string name, string aliasDir)
        {
            try
            {
                if (File.Exists(Path.Combine(aliasDir, name + ".lnk"))) return true;
                if (File.Exists(Path.Combine(aliasDir, name + ".bat"))) return true;
                if (File.Exists(Path.Combine(aliasDir, name + ".cmd"))) return true;
            }
            catch
            {
            }
            return false;
        }

        public static string WorkingDirectoryFor(string target)
        {
            try
            {
                if (Directory.Exists(target))
                {
                    string trimmed = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return Path.GetDirectoryName(trimmed);
                }
                return Path.GetDirectoryName(target);
            }
            catch
            {
                return "";
            }
        }

        public static void Write(AliasEntry entry)
        {
            WriteTo(entry, AppEnv.AliasDir);
        }

        public static void WriteTo(AliasEntry entry, string aliasDir)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (string.IsNullOrEmpty(entry.Name)) throw new InvalidOperationException("别名不能为空。");
            if (string.IsNullOrEmpty(entry.Target)) throw new InvalidOperationException("目标不能为空。");
            if (string.IsNullOrEmpty(aliasDir)) throw new InvalidOperationException("快捷方式目录不能为空。");
            if (!File.Exists(entry.Target) && !Directory.Exists(entry.Target))
                throw new FileNotFoundException("目标不存在：" + entry.Target, entry.Target);
            Directory.CreateDirectory(aliasDir);
            string path = Path.Combine(aliasDir, entry.Name + Extension);
            string target = Path.GetFullPath(entry.Target);

            // 拖入的本来就是快捷方式：直接复制，保留参数、起始位置和图标。
            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase))
                    File.Copy(target, path, true);
                return;
            }

            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    throw new InvalidOperationException("系统缺少 WScript.Shell，无法创建 .lnk 快捷方式。");
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                if (shortcut == null)
                    throw new InvalidOperationException("创建快捷方式失败：" + path);

                shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                string workdir = WorkingDirectoryFor(target);
                if (!string.IsNullOrEmpty(workdir))
                    shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workdir });
                shortcut.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, shortcut,
                    new object[] { "QuickRun 快捷方式：" + entry.Name });
                shortcut.GetType().InvokeMember("WindowStyle", BindingFlags.SetProperty, null, shortcut, new object[] { 1 });
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("创建快捷方式失败：" + ex.Message, ex);
            }
            finally
            {
                if (shortcut != null)
                {
                    try { Marshal.FinalReleaseComObject(shortcut); }
                    catch { }
                }
                if (shell != null)
                {
                    try { Marshal.FinalReleaseComObject(shell); }
                    catch { }
                }
            }

            if (!File.Exists(path))
                throw new InvalidOperationException("快捷方式没有生成：" + path);
        }

        public static void Delete(AliasEntry entry)
        {
            DeleteByName(entry.Name);
        }

        public static void DeleteIn(string aliasDir, string name)
        {
            File.Delete(Path.Combine(aliasDir, name + ".lnk"));
            File.Delete(Path.Combine(aliasDir, name + ".bat"));
            File.Delete(Path.Combine(aliasDir, name + ".cmd"));
        }

        public static void DeleteByName(string name)
        {
            try
            {
                DeleteIn(AppEnv.AliasDir, name);
                DeleteLegacyByName(name);
            }
            catch (Exception ex)
            {
                Log.Error("删除快捷方式失败：" + name, ex);
            }
        }

        public static void DeleteLegacyByName(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(AppEnv.LegacyAliasDir)) return;
                if (string.Equals(Path.GetFullPath(AppEnv.LegacyAliasDir).TrimEnd('\\'),
                    Path.GetFullPath(AppEnv.AliasDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                File.Delete(Path.Combine(AppEnv.LegacyAliasDir, name + ".bat"));
                File.Delete(Path.Combine(AppEnv.LegacyAliasDir, name + ".cmd"));
            }
            catch
            {
            }
        }
    }

    // =================================================================
    // 用户 PATH 维护
    // =================================================================
    public sealed class PathCheckResult
    {
        public bool Contains;
        public bool WriteBlocked;
        public string Error;
        public string RawPath;
        public string Message;
    }

    internal static class PathManager
    {
        private const string EnvironmentKey = @"Environment";
        private const string PathValueName = "Path";
        private const uint WM_SETTINGCHANGE = 0x001A;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        public static string NormalizeEntry(string entry)
        {
            if (entry == null) return "";
            string value = entry.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2).Trim();
            value = Environment.ExpandEnvironmentVariables(value);
            try
            {
                if (value.Length > 0 && !Path.IsPathRooted(value))
                    value = Path.GetFullPath(value);
            }
            catch
            {
            }
            value = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return value;
        }

        public static bool ContainsEntry(string rawPath, string dir)
        {
            if (string.IsNullOrEmpty(rawPath) || string.IsNullOrEmpty(dir)) return false;
            string want = NormalizeEntry(dir);
            if (want.Length == 0) return false;
            foreach (string part in rawPath.Split(';'))
            {
                string normalized = NormalizeEntry(part);
                if (normalized.Length == 0) continue;
                if (string.Equals(normalized, want, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static string AddEntry(string rawPath, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return rawPath;
            if (ContainsEntry(rawPath, dir)) return rawPath;
            string cleaned = rawPath == null ? "" : rawPath.Trim();
            cleaned = cleaned.TrimEnd(';');
            string entry = dir;
            if (entry.IndexOf(' ') >= 0 && !(entry.StartsWith("\"", StringComparison.Ordinal) && entry.EndsWith("\"", StringComparison.Ordinal)))
                entry = "\"" + entry + "\"";
            if (cleaned.Length == 0) return entry;
            return cleaned + ";" + entry;
        }

        public static string RemoveEntry(string rawPath, string dir)
        {
            if (string.IsNullOrEmpty(rawPath)) return rawPath == null ? "" : rawPath;
            string want = NormalizeEntry(dir);
            List<string> kept = new List<string>();
            foreach (string part in rawPath.Split(';'))
            {
                if (part.Length == 0) continue;
                string normalized = NormalizeEntry(part);
                if (normalized.Length > 0 && string.Equals(normalized, want, StringComparison.OrdinalIgnoreCase))
                    continue;
                kept.Add(part.Trim());
            }
            return string.Join(";", kept.ToArray());
        }

        public static PathCheckResult Check()
        {
            return CheckFor(AppEnv.AliasDir);
        }

        public static PathCheckResult CheckFor(string dir)
        {
            PathCheckResult result = new PathCheckResult();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, false))
                {
                    string raw = "";
                    if (key != null)
                    {
                        object value = key.GetValue(PathValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (value != null) raw = Convert.ToString(value);
                    }
                    result.RawPath = raw;
                    result.Contains = ContainsEntry(raw, dir);
                    result.Message = result.Contains ? "用户 PATH 已包含快捷方式目录。" : "用户 PATH 未包含快捷方式目录。";
                }
            }
            catch (Exception ex)
            {
                result.Contains = false;
                result.WriteBlocked = true;
                result.Error = ex.Message;
                result.Message = "无法读取用户 PATH：" + ex.Message;
            }
            return result;
        }

        public static PathCheckResult EnsureAdded(bool broadcast)
        {
            return EnsureAddedFor(AppEnv.AliasDir, broadcast);
        }

        public static PathCheckResult EnsureAddedFor(string dir, bool broadcast)
        {
            PathCheckResult result = CheckFor(dir);
            if (result.Contains)
            {
                if (broadcast) BroadcastChange();
                SyncProcessPath(dir);
                return result;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(EnvironmentKey))
                {
                    if (key == null) throw new UnauthorizedAccessException("无法打开 HKCU\\Environment。");
                    object current = key.GetValue(PathValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    string raw = current == null ? "" : Convert.ToString(current);
                    RegistryValueKind kind = RegistryValueKind.String;
                    if (current != null)
                    {
                        try { kind = key.GetValueKind(PathValueName); }
                        catch { }
                    }
                    if (kind != RegistryValueKind.ExpandString && kind != RegistryValueKind.String)
                        kind = RegistryValueKind.String;
                    string updated = AddEntry(raw, dir);
                    key.SetValue(PathValueName, updated, kind);
                    result.RawPath = updated;
                }

                PathCheckResult verify = CheckFor(dir);
                if (!verify.Contains)
                {
                    result.Contains = false;
                    result.WriteBlocked = true;
                    result.Error = "写入后重新读取用户 PATH 仍找不到快捷方式目录。";
                    result.Message = result.Error;
                }
                else
                {
                    result.Contains = true;
                    result.RawPath = verify.RawPath;
                    result.Message = "已把快捷方式目录加入用户 PATH。";
                }
            }
            catch (Exception ex)
            {
                result.Contains = false;
                result.WriteBlocked = true;
                result.Error = ex.Message;
                result.Message = "无法自动写入用户 PATH：" + ex.Message;
            }

            if (broadcast) BroadcastChange();
            SyncProcessPath(dir);
            return result;
        }

        public static PathCheckResult Remove(bool broadcast)
        {
            return RemoveFor(AppEnv.AliasDir, broadcast);
        }

        public static PathCheckResult RemoveFor(string dir, bool broadcast)
        {
            PathCheckResult result = CheckFor(dir);
            if (result.Error != null && result.RawPath == null)
                return result;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, true))
                {
                    if (key == null) throw new UnauthorizedAccessException("无法打开 HKCU\\Environment。");
                    object current = key.GetValue(PathValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (current != null)
                    {
                        string raw = Convert.ToString(current);
                        RegistryValueKind kind = RegistryValueKind.String;
                        try { kind = key.GetValueKind(PathValueName); }
                        catch { }
                        if (kind != RegistryValueKind.ExpandString && kind != RegistryValueKind.String)
                            kind = RegistryValueKind.String;
                        string updated = RemoveEntry(raw, dir);
                        key.SetValue(PathValueName, updated, kind);
                        result.RawPath = updated;
                    }
                }
                PathCheckResult verify = CheckFor(dir);
                result.Contains = verify.Contains;
                result.Message = verify.Contains ? "未能从用户 PATH 中移除快捷方式目录。" : "已从用户 PATH 移除快捷方式目录。";
            }
            catch (Exception ex)
            {
                result.WriteBlocked = true;
                result.Error = ex.Message;
                result.Message = "无法修改用户 PATH：" + ex.Message;
            }
            if (broadcast) BroadcastChange();
            return result;
        }

        public static void BroadcastChange()
        {
            try
            {
                UIntPtr unused;
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment",
                    SMTO_ABORTIFHUNG, 1200, out unused);
            }
            catch
            {
            }
        }

        private static void SyncProcessPath(string dir)
        {
            try
            {
                string processPath = Environment.GetEnvironmentVariable("Path");
                if (processPath == null) return;
                if (!ContainsEntry(processPath, dir))
                    Environment.SetEnvironmentVariable("Path", AddEntry(processPath, dir), EnvironmentVariableTarget.Process);
            }
            catch
            {
            }
        }
    }

    // =================================================================
    // 自绘控件
    // =================================================================
    internal enum ButtonKind
    {
        Primary,
        Secondary,
        Danger,
        Ghost
    }

    internal sealed class RoundedButton : Button
    {
        private readonly int _radius = 8;
        private bool _hover;
        private bool _pressed;
        private Color _fill = Theme.Card;
        private Color _fillHover = Color.FromArgb(243, 244, 246);
        private Color _fillPressed = Color.FromArgb(229, 231, 235);
        private Color _fore = Theme.Text;
        private bool _outline = true;

        public RoundedButton(string text, ButtonKind kind)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Font = Theme.FontUI;
            Height = 34;
            Cursor = Cursors.Hand;
            TabStop = true;
            UseVisualStyleBackColor = false;
            Padding = new Padding(2, 0, 2, 0);
            ApplyKind(kind);
        }

        public void ApplyKind(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Primary:
                    _fill = Theme.Primary;
                    _fillHover = Theme.PrimaryDark;
                    _fillPressed = Color.FromArgb(30, 64, 175);
                    _fore = Color.White;
                    _outline = false;
                    break;
                case ButtonKind.Danger:
                    _fill = Theme.Card;
                    _fillHover = Theme.DangerSoft;
                    _fillPressed = Color.FromArgb(252, 205, 205);
                    _fore = Theme.Danger;
                    _outline = true;
                    break;
                case ButtonKind.Ghost:
                    _fill = Color.Transparent;
                    _fillHover = Color.FromArgb(243, 244, 246);
                    _fillPressed = Color.FromArgb(229, 231, 235);
                    _fore = Theme.Text;
                    _outline = false;
                    break;
                default:
                    _fill = Theme.Card;
                    _fillHover = Color.FromArgb(249, 250, 251);
                    _fillPressed = Color.FromArgb(243, 244, 246);
                    _fore = Theme.Text;
                    _outline = true;
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundRect(rect, _radius))
            {
                Color fill = !Enabled ? Color.FromArgb(243, 244, 246) : (_pressed ? _fillPressed : (_hover ? _fillHover : _fill));
                if (fill.A > 0)
                {
                    using (SolidBrush brush = new SolidBrush(fill))
                        g.FillPath(brush, path);
                }
                if (_outline)
                {
                    using (Pen pen = new Pen(Enabled ? Theme.Border : Color.FromArgb(229, 231, 235), 1f))
                        g.DrawPath(pen, path);
                }
            }

            Color textColor = Enabled ? _fore : Theme.Muted;
            TextRenderer.DrawText(g, Text, Font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class StatusPill : Control
    {
        private string _statusText = "";
        private Color _fill = Theme.SuccessSoft;
        private Color _fore = Theme.Success;

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.FontBold;
            Height = 28;
            Width = 104;
            Cursor = Cursors.Hand;
        }

        public void SetStatus(string text, Color fill, Color fore)
        {
            _statusText = text == null ? "" : text;
            _fill = fill;
            _fore = fore;
            int measured = TextRenderer.MeasureText(_statusText, Font).Width;
            Width = measured + 24;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Theme.Card : Parent.BackColor);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundRect(rect, Height / 2))
            using (SolidBrush brush = new SolidBrush(_fill))
            {
                g.FillPath(brush, path);
            }
            TextRenderer.DrawText(g, _statusText, Font, rect, _fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            try
            {
                if (Width > 4 && Height > 4)
                {
                    if (Region != null) Region.Dispose();
                    Region = new Region(Theme.RoundRect(new Rectangle(0, 0, Width, Height), 12));
                }
            }
            catch
            {
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundRect(rect, 12))
            using (Pen pen = new Pen(Theme.Border, 1f))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class DropZone : Panel
    {
        private bool _hot;
        private Label _title;
        private Label _subtitle;

        public event EventHandler BrowseRequested;

        public DropZone()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.DropBg;
            Cursor = Cursors.Hand;

            _title = new Label();
            _title.Text = "把 exe / 快捷方式拖到这里";
            _title.Font = Theme.FontDropTitle;
            _title.ForeColor = Theme.Text;
            _title.AutoSize = true;
            _title.BackColor = Color.Transparent;
            _title.Click += delegate { RaiseBrowse(); };
            Controls.Add(_title);

            _subtitle = new Label();
            _subtitle.Text = "自动生成 .lnk 快捷方式 · 自动加入 PATH · 也可以点这里选择文件";
            _subtitle.Font = Theme.FontUI;
            _subtitle.ForeColor = Theme.Muted;
            _subtitle.AutoSize = true;
            _subtitle.BackColor = Color.Transparent;
            _subtitle.Click += delegate { RaiseBrowse(); };
            Controls.Add(_subtitle);

            Click += delegate { RaiseBrowse(); };
        }

        private void RaiseBrowse()
        {
            if (BrowseRequested != null) BrowseRequested(this, EventArgs.Empty);
        }

        public void SetHot(bool hot)
        {
            if (_hot == hot) return;
            _hot = hot;
            BackColor = hot ? Theme.DropHot : Theme.DropBg;
            Invalidate();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            try
            {
                if (Width > 4 && Height > 4)
                {
                    if (Region != null) Region.Dispose();
                    Region = new Region(Theme.RoundRect(new Rectangle(0, 0, Width, Height), 14));
                }
            }
            catch
            {
            }
            LayoutLabels();
        }

        private void LayoutLabels()
        {
            if (_title == null || _subtitle == null) return;
            int titleW = _title.PreferredWidth;
            int subW = _subtitle.PreferredWidth;
            int y = (Height - (_title.PreferredHeight + 6 + _subtitle.PreferredHeight)) / 2;
            if (y < 8) y = 8;
            _title.Location = new Point(Math.Max(8, (Width - titleW) / 2), y);
            _subtitle.Location = new Point(Math.Max(8, (Width - subW) / 2), y + _title.PreferredHeight + 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = Theme.RoundRect(rect, 13))
            using (Pen pen = new Pen(_hot ? Theme.Primary : Theme.DropBorder, _hot ? 2f : 1.5f))
            {
                pen.DashStyle = _hot ? DashStyle.Solid : DashStyle.Dash;
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class BannerPanel : Panel
    {
        private Label _label;
        private RoundedButton _retry;
        private RoundedButton _copy;

        public event EventHandler RetryRequested;
        public event EventHandler CopyRequested;

        public BannerPanel()
        {
            BackColor = Theme.BannerBg;
            Height = 46;
            Padding = new Padding(12, 6, 8, 6);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 3;
            layout.RowCount = 1;
            layout.BackColor = Theme.BannerBg;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(layout);

            _label = new Label();
            _label.Dock = DockStyle.Fill;
            _label.TextAlign = ContentAlignment.MiddleLeft;
            _label.ForeColor = Theme.Warn;
            _label.Font = Theme.FontUI;
            _label.AutoEllipsis = true;
            _label.BackColor = Theme.BannerBg;
            _label.Text = "用户 PATH 未配置成功：Win+R 暂时找不到别名。点「重试」或复制目录手动添加。";
            layout.Controls.Add(_label, 0, 0);

            _retry = new RoundedButton("重试", ButtonKind.Secondary);
            _retry.Width = 54;
            _retry.Height = 28;
            _retry.Margin = new Padding(6, 6, 0, 0);
            _retry.Click += delegate { if (RetryRequested != null) RetryRequested(this, EventArgs.Empty); };
            layout.Controls.Add(_retry, 1, 0);

            _copy = new RoundedButton("复制目录", ButtonKind.Secondary);
            _copy.Width = 76;
            _copy.Height = 28;
            _copy.Margin = new Padding(6, 6, 0, 0);
            _copy.Click += delegate { if (CopyRequested != null) CopyRequested(this, EventArgs.Empty); };
            layout.Controls.Add(_copy, 2, 0);
        }

        public void SetText(string text, string tooltip)
        {
            _label.Text = text;
            ToolTip tip = new ToolTip();
            tip.SetToolTip(_label, tooltip == null ? text : tooltip);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.FromArgb(253, 230, 138), 1f))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // =================================================================
    // 主窗口
    // =================================================================
    public sealed class MainForm : Form
    {
        private readonly bool _preview;
        private AppConfig _config;
        private string _configWarning;
        private PathCheckResult _lastPathCheck;

        private CardPanel _header;
        private PictureBox _iconBox;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private StatusPill _pathPill;
        private RoundedButton _btnHelp;
        private RoundedButton _btnMore;

        private BannerPanel _banner;
        private DropZone _drop;
        private ListView _list;
        private Panel _emptyPanel;
        private Label _emptyTitle;
        private Label _emptySub;
        private RoundedButton _emptyButton;
        private Panel _listCard;

        private RoundedButton _btnAdd;
        private RoundedButton _btnEdit;
        private RoundedButton _btnRun;
        private RoundedButton _btnDelete;
        private RoundedButton _btnRepair;
        private RoundedButton _btnOpen;
        private RoundedButton _btnMoreTools;

        private Label _hintLabel;
        private LinkLabel _quickRunLink;
        private System.Windows.Forms.Timer _statusTimer;
        private ToolTip _toolTip;
        private ContextMenuStrip _contextMenu;
        private ContextMenuStrip _moreMenu;
        private string _defaultHint;
        private bool _pathReady;
        private string _pathError;

        public MainForm(bool preview)
        {
            _preview = preview;
            _defaultHint = "Win+R 输入别名 → 回车 · 双击条目可编辑别名";

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Text = "QuickRun · Win+R 快捷启动";
            Font = Theme.FontUI;
            BackColor = Theme.Bg;
            ClientSize = new Size(830, 650);
            MinimumSize = new Size(730, 580);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = LoadApplicationIcon();
            KeyPreview = true;

            _toolTip = new ToolTip();
            _toolTip.AutoPopDelay = 8000;
            _toolTip.InitialDelay = 350;

            BuildUi();
            if (_preview)
            {
                _config = CreateDemoConfig();
                _pathReady = true;
                ApplyPreviewState();
            }
            else
            {
                _config = ConfigStore.Load(out _configWarning);
                EnsureEnvironmentOnStartup();
            }
            BuildContextMenus();
            RefreshList();

            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 6000;
            _statusTimer.Tick += delegate
            {
                _statusTimer.Stop();
                if (_hintLabel != null)
                {
                    _hintLabel.Text = _defaultHint;
                    _hintLabel.ForeColor = Theme.Muted;
                }
            };

            Shown += delegate
            {
                if (!_preview && _config.Aliases.Count == 0)
                {
                    _drop.Focus();
                }
                if (!string.IsNullOrEmpty(_configWarning))
                    ShowStatus(_configWarning, Theme.Danger, 12000);
                if (!_preview && !string.IsNullOrEmpty(AppEnv.AliasDirWarning))
                    ShowStatus(AppEnv.AliasDirWarning, Theme.Warn, 12000);
                if (!_preview)
                    Log.Info("界面启动；QuickRun=" + AppEnv.Root + "；快捷方式目录=" + AppEnv.AliasDir + "；条目=" + _config.Aliases.Count);
                if (!_preview && !_pathReady && AppEnv.IsFallbackDataDir())
                    BeginInvoke(new MethodInvoker(ShowRestrictedEnvironmentHint));
            };
            FormClosing += delegate { if (!_preview) Log.Info("界面关闭"); };
        }

        private Icon LoadApplicationIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        // -------------------------------------------------------------
        // UI 构建
        // -------------------------------------------------------------
        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.BackColor = Theme.Bg;
            root.Padding = new Padding(20, 14, 20, 8);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            Controls.Add(root);

            BuildHeader(root);
            BuildBanner(root);
            BuildDropZone(root);
            BuildToolbar(root);
            BuildList(root);
            BuildFooter(root);

            AttachDropTargets(root);
            AllowDrop = true;
            DragEnter += DropDragEnter;
            DragLeave += DropDragLeave;
            DragDrop += DropDragDrop;

            root.Resize += delegate { LayoutHeader(); };
        }

        private void BuildHeader(TableLayoutPanel root)
        {
            _header = new CardPanel();
            _header.Dock = DockStyle.Fill;
            _header.Margin = new Padding(0, 0, 0, 10);
            _header.Padding = new Padding(0);
            root.Controls.Add(_header, 0, 0);
            _header.Resize += delegate { LayoutHeader(); };

            _iconBox = new PictureBox();
            _iconBox.Size = new Size(44, 44);
            _iconBox.SizeMode = PictureBoxSizeMode.Zoom;
            _iconBox.BackColor = Color.Transparent;
            try
            {
                Icon icon = Icon;
                if (icon != null) _iconBox.Image = icon.ToBitmap();
            }
            catch
            {
            }
            _header.Controls.Add(_iconBox);

            _titleLabel = new Label();
            _titleLabel.Text = "QuickRun";
            _titleLabel.Font = Theme.FontTitle;
            _titleLabel.ForeColor = Theme.Text;
            _titleLabel.AutoSize = true;
            _titleLabel.BackColor = Color.Transparent;
            _header.Controls.Add(_titleLabel);

            _subtitleLabel = new Label();
            _subtitleLabel.Text = "拖进 exe / 快捷方式，自动生成 Win+R 别名并维护 PATH";
            _subtitleLabel.Font = Theme.FontSubtitle;
            _subtitleLabel.ForeColor = Theme.Muted;
            _subtitleLabel.AutoSize = true;
            _subtitleLabel.BackColor = Color.Transparent;
            _header.Controls.Add(_subtitleLabel);

            _pathPill = new StatusPill();
            _pathPill.SetStatus("PATH 准备中", Theme.PrimarySoft, Theme.Primary);
            _pathPill.Click += delegate { ShowDiagnose(); };
            _toolTip.SetToolTip(_pathPill, "点击打开环境自检");
            _header.Controls.Add(_pathPill);

            _btnHelp = new RoundedButton("?", ButtonKind.Secondary);
            _btnHelp.Size = new Size(32, 32);
            _btnHelp.Font = Theme.FontBold;
            _btnHelp.Click += delegate { ShowHelp(); };
            _toolTip.SetToolTip(_btnHelp, "使用说明");
            _header.Controls.Add(_btnHelp);

            _btnMore = new RoundedButton("⋯", ButtonKind.Secondary);
            _btnMore.Size = new Size(32, 32);
            _btnMore.Font = Theme.FontBold;
            _toolTip.SetToolTip(_btnMore, "更多工具");
            _btnMore.Click += delegate { ShowMoreMenu(_btnMore); };
            _header.Controls.Add(_btnMore);
        }

        private void LayoutHeader()
        {
            if (_header == null || _iconBox == null) return;
            int h = _header.ClientSize.Height;
            _iconBox.Location = new Point(16, (h - _iconBox.Height) / 2);
            _titleLabel.Location = new Point(76, 14);
            _subtitleLabel.Location = new Point(77, 43);

            int right = _header.ClientSize.Width - 14;
            _btnMore.Location = new Point(right - _btnMore.Width, (h - _btnMore.Height) / 2);
            _btnHelp.Location = new Point(_btnMore.Left - 6 - _btnHelp.Width, (h - _btnHelp.Height) / 2);
            _pathPill.Location = new Point(_btnHelp.Left - 10 - _pathPill.Width, (h - _pathPill.Height) / 2);
        }

        private void BuildBanner(TableLayoutPanel root)
        {
            _banner = new BannerPanel();
            _banner.Dock = DockStyle.Fill;
            _banner.Margin = new Padding(0, 0, 0, 8);
            _banner.Visible = false;
            _banner.RetryRequested += delegate { RepairEnvironment(); };
            _banner.CopyRequested += delegate
            {
                try
                {
                    Clipboard.SetText(AppEnv.AliasDir);
                    ShowStatus("已复制快捷方式目录路径", Theme.Success);
                }
                catch
                {
                }
            };
            root.Controls.Add(_banner, 0, 1);
        }

        private void BuildDropZone(TableLayoutPanel root)
        {
            _drop = new DropZone();
            _drop.Dock = DockStyle.Fill;
            _drop.Margin = new Padding(0, 0, 0, 10);
            _drop.BrowseRequested += delegate { AddFilesDialog(); };
            root.Controls.Add(_drop, 0, 2);
        }

        private void AttachDropTargets(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += DropDragEnter;
            control.DragLeave += DropDragLeave;
            control.DragDrop += DropDragDrop;
            foreach (Control child in control.Controls)
                AttachDropTargets(child);
        }

        private void BuildToolbar(TableLayoutPanel root)
        {
            TableLayoutPanel toolbar = new TableLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.ColumnCount = 2;
            toolbar.RowCount = 1;
            toolbar.BackColor = Theme.Bg;
            toolbar.Margin = new Padding(0, 0, 0, 2);
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            root.Controls.Add(toolbar, 0, 3);

            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.FlowDirection = FlowDirection.LeftToRight;
            left.WrapContents = false;
            left.BackColor = Theme.Bg;
            left.Margin = new Padding(0);
            toolbar.Controls.Add(left, 0, 0);

            _btnAdd = new RoundedButton("+  添加文件", ButtonKind.Primary);
            _btnAdd.Width = 118;
            _btnAdd.Margin = new Padding(0, 3, 8, 3);
            left.Controls.Add(_btnAdd);

            _btnEdit = new RoundedButton("编辑", ButtonKind.Secondary);
            _btnEdit.Width = 68;
            _btnEdit.Margin = new Padding(0, 3, 8, 3);
            left.Controls.Add(_btnEdit);

            _btnRun = new RoundedButton("立即运行", ButtonKind.Secondary);
            _btnRun.Width = 88;
            _btnRun.Margin = new Padding(0, 3, 8, 3);
            left.Controls.Add(_btnRun);

            _btnDelete = new RoundedButton("删除", ButtonKind.Danger);
            _btnDelete.Width = 64;
            _btnDelete.Margin = new Padding(0, 3, 8, 3);
            left.Controls.Add(_btnDelete);

            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.FlowDirection = FlowDirection.RightToLeft;
            right.WrapContents = false;
            right.BackColor = Theme.Bg;
            right.Margin = new Padding(0);
            toolbar.Controls.Add(right, 1, 0);

            _btnMoreTools = new RoundedButton("更多", ButtonKind.Secondary);
            _btnMoreTools.Width = 62;
            _btnMoreTools.Margin = new Padding(0, 3, 0, 3);
            right.Controls.Add(_btnMoreTools);

            _btnOpen = new RoundedButton("打开目录", ButtonKind.Secondary);
            _btnOpen.Width = 88;
            _btnOpen.Margin = new Padding(0, 3, 8, 3);
            right.Controls.Add(_btnOpen);

            _btnRepair = new RoundedButton("修复环境", ButtonKind.Secondary);
            _btnRepair.Width = 88;
            _btnRepair.Margin = new Padding(0, 3, 8, 3);
            right.Controls.Add(_btnRepair);

            _btnAdd.Click += delegate { AddFilesDialog(); };
            _btnEdit.Click += delegate { EditSelected(); };
            _btnRun.Click += delegate { RunSelected(); };
            _btnDelete.Click += delegate { DeleteSelected(); };
            _btnRepair.Click += delegate { RepairEnvironment(); };
            _btnOpen.Click += delegate { OpenFolder(AppEnv.AliasDir); };
            _btnMoreTools.Click += delegate { ShowMoreMenu(_btnMoreTools); };
        }

        private void BuildList(TableLayoutPanel root)
        {
            _listCard = new CardPanel();
            _listCard.Dock = DockStyle.Fill;
            _listCard.Margin = new Padding(0, 0, 0, 8);
            _listCard.Padding = new Padding(12, 8, 12, 10);
            root.Controls.Add(_listCard, 0, 4);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.GridLines = false;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Theme.Card;
            _list.ForeColor = Theme.Text;
            _list.Font = Theme.FontUI;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.ShowItemToolTips = true;
            _list.AllowColumnReorder = false;
            _list.SmallImageList = CreateRowHeightImageList();
            _list.Columns.Add("别名", 158, HorizontalAlignment.Left);
            _list.Columns.Add("类型", 82, HorizontalAlignment.Left);
            _list.Columns.Add("目标", 330, HorizontalAlignment.Left);
            _list.Columns.Add("状态", 92, HorizontalAlignment.Left);
            _list.Resize += delegate { UpdateListColumns(); };
            _list.SelectedIndexChanged += delegate { UpdateActionButtons(); };
            _list.DoubleClick += delegate { EditSelected(); };
            _list.KeyDown += ListKeyDown;
            _listCard.Controls.Add(_list);

            _emptyPanel = new Panel();
            _emptyPanel.Dock = DockStyle.Fill;
            _emptyPanel.BackColor = Theme.Card;
            _emptyPanel.Visible = false;
            _emptyPanel.Resize += delegate { LayoutEmptyState(); };
            _listCard.Controls.Add(_emptyPanel);
            _emptyPanel.BringToFront();

            _emptyTitle = new Label();
            _emptyTitle.Text = "还没有别名";
            _emptyTitle.Font = Theme.MakeFont(14F, FontStyle.Bold);
            _emptyTitle.ForeColor = Theme.Text;
            _emptyTitle.AutoSize = true;
            _emptyTitle.BackColor = Theme.Card;
            _emptyPanel.Controls.Add(_emptyTitle);

            _emptySub = new Label();
            _emptySub.Text = "把 exe 或快捷方式拖进上面的蓝色区域，\r\n会自动生成一个 .lnk 快捷方式，加入 PATH 后 Win+R 就能启动。";
            _emptySub.Font = Theme.FontUI;
            _emptySub.ForeColor = Theme.Muted;
            _emptySub.AutoSize = true;
            _emptySub.TextAlign = ContentAlignment.MiddleCenter;
            _emptySub.BackColor = Theme.Card;
            _emptyPanel.Controls.Add(_emptySub);

            _emptyButton = new RoundedButton("选择文件…", ButtonKind.Primary);
            _emptyButton.Width = 110;
            _emptyButton.Height = 34;
            _emptyButton.Click += delegate { AddFilesDialog(); };
            _emptyPanel.Controls.Add(_emptyButton);
        }

        private ImageList CreateRowHeightImageList()
        {
            ImageList list = new ImageList();
            list.ImageSize = new Size(1, 30);
            list.ColorDepth = ColorDepth.Depth32Bit;
            Bitmap blank = new Bitmap(1, 30);
            using (Graphics g = Graphics.FromImage(blank))
                g.Clear(Color.Transparent);
            list.Images.Add("blank", blank);
            return list;
        }

        private void BuildFooter(TableLayoutPanel root)
        {
            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 3;
            footer.RowCount = 1;
            footer.BackColor = Theme.Bg;
            footer.Margin = new Padding(0);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(footer, 0, 5);

            _hintLabel = new Label();
            _hintLabel.Text = _defaultHint;
            _hintLabel.Dock = DockStyle.Fill;
            _hintLabel.TextAlign = ContentAlignment.MiddleLeft;
            _hintLabel.ForeColor = Theme.Muted;
            _hintLabel.Font = Theme.FontSmall;
            _hintLabel.AutoEllipsis = true;
            _hintLabel.BackColor = Theme.Bg;
            footer.Controls.Add(_hintLabel, 0, 0);

            Label author = new Label();
            author.Text = "作者：zhlipe";
            author.AutoSize = true;
            author.Anchor = AnchorStyles.Right;
            author.TextAlign = ContentAlignment.MiddleRight;
            author.ForeColor = Theme.Muted;
            author.Font = Theme.FontSmall;
            author.BackColor = Theme.Bg;
            author.Margin = new Padding(0, 4, 16, 0);
            _toolTip.SetToolTip(author, "作者：zhlipe");
            footer.Controls.Add(author, 1, 0);

            _quickRunLink = new LinkLabel();
            _quickRunLink.Text = "打开快捷方式目录";
            _quickRunLink.AutoSize = true;
            _quickRunLink.Anchor = AnchorStyles.Right;
            _quickRunLink.TextAlign = ContentAlignment.MiddleRight;
            _quickRunLink.Font = Theme.FontSmall;
            _quickRunLink.LinkColor = Theme.Muted;
            _quickRunLink.ActiveLinkColor = Theme.Primary;
            _quickRunLink.VisitedLinkColor = Theme.Muted;
            _quickRunLink.LinkBehavior = LinkBehavior.HoverUnderline;
            _quickRunLink.BackColor = Theme.Bg;
            _quickRunLink.Margin = new Padding(0, 4, 0, 0);
            _quickRunLink.Click += delegate { OpenFolder(AppEnv.AliasDir); };
            _toolTip.SetToolTip(_quickRunLink, AppEnv.AliasDir);
            footer.Controls.Add(_quickRunLink, 2, 0);
        }

        private void LayoutEmptyState()
        {
            if (_emptyPanel == null || _emptyTitle == null) return;
            int centerX = _emptyPanel.ClientSize.Width / 2;
            int centerY = _emptyPanel.ClientSize.Height / 2;
            int totalH = _emptyTitle.PreferredHeight + 10 + _emptySub.PreferredHeight + 18 + _emptyButton.Height;
            int top = Math.Max(10, centerY - totalH / 2);
            _emptyTitle.Location = new Point(centerX - _emptyTitle.PreferredWidth / 2, top);
            _emptySub.Location = new Point(centerX - _emptySub.PreferredWidth / 2, top + _emptyTitle.PreferredHeight + 10);
            _emptyButton.Location = new Point(centerX - _emptyButton.Width / 2, top + _emptyTitle.PreferredHeight + 10 + _emptySub.PreferredHeight + 18);
        }

        // -------------------------------------------------------------
        // 拖放
        // -------------------------------------------------------------
        private void DropDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                _drop.SetHot(true);
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void DropDragLeave(object sender, EventArgs e)
        {
            _drop.SetHot(false);
        }

        private void DropDragDrop(object sender, DragEventArgs e)
        {
            _drop.SetHot(false);
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            AddFiles(files);
        }

        // -------------------------------------------------------------
        // 环境
        // -------------------------------------------------------------
        private void EnsureEnvironmentOnStartup()
        {
            List<string> failed = new List<string>();
            foreach (AliasEntry entry in _config.Aliases)
            {
                try
                {
                    AliasFile.Write(entry);
                    AliasFile.DeleteLegacyByName(entry.Name);
                }
                catch (Exception ex)
                {
                    failed.Add(entry.Name + "：" + ex.Message);
                    Log.Error("启动时生成快捷方式失败：" + entry.Name, ex);
                }
            }

            try
            {
                PathCheckResult check = PathManager.EnsureAdded(true);
                ApplyPathCheck(check);
            }
            catch (Exception ex)
            {
                _pathReady = false;
                _pathError = ex.Message;
                Log.Error("启动时维护 PATH 失败", ex);
                ShowPathState("PATH 未配置", Theme.WarnSoft, Theme.Warn, true, ex.Message);
            }

            if (failed.Count > 0)
            {
                ShowStatus("有 " + failed.Count + " 个快捷方式生成失败，点「修复环境」查看日志。", Theme.Danger, 10000);
            }
        }

        private void ApplyPreviewState()
        {
            _pathReady = true;
            _pathError = null;
            ShowPathState("PATH 已就绪", Theme.SuccessSoft, Theme.Success, false, null);
        }

        private void ApplyPathCheck(PathCheckResult check)
        {
            _lastPathCheck = check;
            _pathReady = check != null && check.Contains;
            _pathError = check == null ? null : check.Error;

            if (_pathReady)
            {
                ShowPathState("PATH 已就绪", Theme.SuccessSoft, Theme.Success, false, null);
            }
            else if (!string.IsNullOrEmpty(_pathError))
            {
                ShowPathState("PATH 未配置", Theme.DangerSoft, Theme.Danger, true, _pathError);
            }
            else
            {
                ShowPathState("PATH 未加入", Theme.WarnSoft, Theme.Warn, true, "用户 PATH 中还没有快捷方式目录。");
            }
        }

        private void ShowPathState(string text, Color fill, Color fore, bool showBanner, string error)
        {
            if (_pathPill != null)
            {
                _pathPill.SetStatus(text, fill, fore);
                LayoutHeader();
            }
            if (_banner != null)
            {
                if (showBanner && !_preview)
                {
                    _banner.Visible = true;
                    string message = string.IsNullOrEmpty(error)
                        ? "用户 PATH 未配置成功：Win+R 暂时找不到别名。点「重试」或复制目录手动添加。"
                        : "无法自动写入用户 PATH（" + Shorten(error, 60) + "）。点「重试」，或复制目录手动添加。";
                    _banner.SetText(message, "快捷方式目录：" + AppEnv.AliasDir + "\r\n错误：" + error);
                }
                else
                {
                    _banner.Visible = false;
                }
            }
        }

        private void ShowRestrictedEnvironmentHint()
        {
            Log.Info("检测到受限运行环境：未能使用用户目录，数据回退到 " + AppEnv.Root);
            string text =
                "QuickRun 当前运行在受限环境中，无法使用 %USERPROFILE%\\QuickRun，" +
                "也无法写入当前用户 PATH，因此 Win+R 暂时找不到别名。\r\n\r\n" +
                "常见原因：从沙箱/对话界面直接打开、程序放在只读目录，或被安全策略限制。\r\n\r\n" +
                "请关闭本窗口，把 QuickRun.exe 复制到普通目录（例如 D:\\Tools\\QuickRun 或桌面），" +
                "然后在 Windows 资源管理器中双击运行。正常启动后会自动创建 %USERPROFILE%\\QuickRun 并配置 PATH。\r\n\r\n" +
                "当前回退目录：" + AppEnv.Root;
            MessageBox.Show(this, text, "QuickRun · 运行环境受限",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= max) return value;
            return value.Substring(0, max) + "…";
        }

        private void RepairEnvironment()
        {
            int regenerated = 0;
            List<string> errors = new List<string>();
            foreach (AliasEntry entry in _config.Aliases)
            {
                try
                {
                    AliasFile.Write(entry);
                    AliasFile.DeleteLegacyByName(entry.Name);
                    regenerated++;
                }
                catch (Exception ex)
                {
                    errors.Add(entry.Name + "：" + ex.Message);
                    Log.Error("修复别名失败：" + entry.Name, ex);
                }
            }

            PathCheckResult check = PathManager.EnsureAdded(true);
            ApplyPathCheck(check);
            RefreshList();
            Log.Info("修复环境：重写别名 " + regenerated + " 个；PATH=" + check.Message);

            if (errors.Count > 0)
            {
                MessageBox.Show(this, "部分快捷方式生成失败：\r\n\r\n" + string.Join("\r\n", errors.ToArray()) +
                    "\r\n\r\n日志：" + AppEnv.LogFile, "修复未完全成功", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowStatus("修复完成，但有 " + errors.Count + " 个错误", Theme.Danger, 8000);
            }
            else if (check.Contains)
            {
                ShowStatus("环境已修复：快捷方式与 PATH 均已就绪", Theme.Success, 6000);
            }
            else
            {
                ShowStatus("快捷方式已重新生成，但 PATH 仍需手动加入", Theme.Warn, 10000);
            }
        }

        // -------------------------------------------------------------
        // 列表
        // -------------------------------------------------------------
        private void RefreshList()
        {
            if (_list == null || _config == null) return;
            string selectedName = null;
            AliasEntry selected = SelectedEntry;
            if (selected != null) selectedName = selected.Name;

            List<AliasEntry> sorted = new List<AliasEntry>(_config.Aliases);
            sorted.Sort(delegate (AliasEntry a, AliasEntry b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (AliasEntry entry in sorted)
                {
                    string resolved = Shortcuts.ResolveIfLink(entry.Target);
                    string displayTarget = resolved;
                    if (string.IsNullOrEmpty(displayTarget)) displayTarget = entry.Target;
                    string status;
                    Color statusColor;
                    bool targetOk = TargetExists(entry.Target, resolved);
                    if (_preview)
                    {
                        status = "✓ 就绪";
                        statusColor = Theme.Success;
                    }
                    else if (!targetOk)
                    {
                        status = "目标丢失";
                        statusColor = Theme.Danger;
                    }
                    else if (!AliasFile.Exists(entry))
                    {
                        status = "别名缺失";
                        statusColor = Theme.Warn;
                    }
                    else if (!_pathReady)
                    {
                        status = "PATH 未加入";
                        statusColor = Theme.Warn;
                    }
                    else
                    {
                        status = "✓ 就绪";
                        statusColor = Theme.Success;
                    }

                    ListViewItem item = new ListViewItem(entry.Name);
                    item.Tag = entry;
                    item.ImageKey = "blank";
                    item.Font = Theme.FontBold;
                    item.UseItemStyleForSubItems = false;
                    item.SubItems.Add(KindOf(entry.Target));
                    item.SubItems.Add(displayTarget);
                    item.SubItems.Add(status);
                    item.SubItems[0].ForeColor = Theme.Text;
                    item.SubItems[1].ForeColor = Theme.Muted;
                    item.SubItems[2].ForeColor = Theme.Muted;
                    item.SubItems[3].ForeColor = statusColor;
                    StringBuilder tip = new StringBuilder();
                    tip.Append("别名：").Append(entry.Name).Append("\r\n");
                    tip.Append("目标：").Append(entry.Target).Append("\r\n");
                    if (!string.Equals(resolved, entry.Target, StringComparison.OrdinalIgnoreCase))
                        tip.Append("实际指向：").Append(resolved).Append("\r\n");
                    tip.Append("快捷方式：").Append(AliasFile.PathFor(entry)).Append("\r\n");
                    if (!string.IsNullOrEmpty(entry.Note))
                        tip.Append("备注：").Append(entry.Note);
                    item.ToolTipText = tip.ToString();
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            bool empty = _config.Aliases.Count == 0;
            if (_emptyPanel != null)
            {
                _emptyPanel.Visible = empty;
                if (empty) _emptyPanel.BringToFront();
            }
            if (empty) LayoutEmptyState();
            if (!string.IsNullOrEmpty(selectedName)) SelectAlias(selectedName);
            UpdateListColumns();
            UpdateActionButtons();
        }

        private void UpdateListColumns()
        {
            if (_list == null || _list.Columns.Count < 4) return;
            int fixedWidth = _list.Columns[0].Width + _list.Columns[1].Width + _list.Columns[3].Width;
            int targetWidth = _list.ClientSize.Width - fixedWidth - 4;
            if (targetWidth < 150) targetWidth = 150;
            _list.Columns[2].Width = targetWidth;
        }

        private void UpdateActionButtons()
        {
            bool hasSelection = SelectedEntry != null;
            if (_btnEdit != null) _btnEdit.Enabled = hasSelection;
            if (_btnRun != null) _btnRun.Enabled = hasSelection;
            if (_btnDelete != null) _btnDelete.Enabled = hasSelection;
        }

        private AliasEntry SelectedEntry
        {
            get
            {
                if (_list == null || _list.SelectedItems.Count == 0) return null;
                return _list.SelectedItems[0].Tag as AliasEntry;
            }
        }

        private void SelectAlias(string name)
        {
            if (_list == null || string.IsNullOrEmpty(name)) return;
            foreach (ListViewItem item in _list.Items)
            {
                AliasEntry entry = item.Tag as AliasEntry;
                if (entry != null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                    _list.EnsureVisible(item.Index);
                    return;
                }
            }
        }

        private bool TargetExists(string target, string resolved)
        {
            if (string.IsNullOrEmpty(target)) return false;
            if (!File.Exists(target) && !Directory.Exists(target)) return false;
            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(resolved) &&
                !string.Equals(resolved, target, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(resolved) && !Directory.Exists(resolved)) return false;
            }
            return true;
        }

        private static string KindOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "未知";
            try
            {
                if (Directory.Exists(path)) return "文件夹";
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".lnk") return "快捷方式";
                if (ext == ".exe" || ext == ".com") return "程序";
                if (ext == ".bat" || ext == ".cmd") return "脚本";
                if (ext == ".msi") return "安装包";
            }
            catch
            {
            }
            return "文件";
        }

        // -------------------------------------------------------------
        // 添加 / 编辑 / 删除 / 运行
        // -------------------------------------------------------------
        private void AddFilesDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要加入 QuickRun 的程序或快捷方式";
                dialog.Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.com;*.msi|所有文件|*.*";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AddFiles(dialog.FileNames);
            }
        }

        private void AddFiles(IEnumerable<string> files)
        {
            if (files == null) return;
            List<string> added = new List<string>();
            List<string> skipped = new List<string>();
            List<string> errors = new List<string>();
            string lastAdded = null;

            foreach (string raw in files)
            {
                string full = raw;
                try
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    full = Path.GetFullPath(raw.Trim());
                }
                catch (Exception ex)
                {
                    errors.Add(raw + "：" + ex.Message);
                    continue;
                }

                if (!File.Exists(full) && !Directory.Exists(full))
                {
                    errors.Add(Path.GetFileName(full) + "：目标不存在");
                    continue;
                }

                bool duplicate = false;
                foreach (AliasEntry existing in _config.Aliases)
                {
                    if (string.Equals(existing.Target, full, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate)
                {
                    skipped.Add(Path.GetFileName(full));
                    continue;
                }

                string name = AliasRules.MakeUnique(AliasRules.FromTarget(full), _config.Aliases, true, full);
                AliasEntry entry = new AliasEntry();
                entry.Name = name;
                entry.Target = full;
                entry.Note = "";
                entry.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                try
                {
                    AliasFile.Write(entry);
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(full) + "：" + ex.Message);
                    Log.Error("生成别名失败：" + name, ex);
                    continue;
                }

                _config.Aliases.Add(entry);
                added.Add(name);
                lastAdded = name;
                Log.Info("新增别名：" + name + " -> " + full);
            }

            if (added.Count > 0)
            {
                if (!SaveConfig()) return;
                RefreshList();
                if (lastAdded != null) SelectAlias(lastAdded);
                string message = "已添加 " + added.Count + " 个别名：" + string.Join("、", added.ToArray());
                if (skipped.Count > 0) message += "；跳过 " + skipped.Count + " 个已存在目标";
                ShowStatus(message, Theme.Success, 8000);
            }
            else if (skipped.Count > 0)
            {
                ShowStatus("目标已经存在对应别名，已跳过 " + skipped.Count + " 个", Theme.Warn, 6000);
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this, "有 " + errors.Count + " 个文件未能添加：\r\n\r\n" +
                    string.Join("\r\n", errors.ToArray()), "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EditSelected()
        {
            AliasEntry entry = SelectedEntry;
            if (entry == null)
            {
                ShowStatus("请先在列表里选中一条别名", Theme.Warn);
                return;
            }

            using (AliasEditorDialog dialog = new AliasEditorDialog(entry, _config, _preview))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AliasEntry updated = dialog.Result;
                string oldName = entry.Name;
                bool nameChanged = !string.Equals(oldName, updated.Name, StringComparison.OrdinalIgnoreCase);

                try
                {
                    AliasFile.Write(updated);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "写入快捷方式失败：" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (nameChanged) AliasFile.DeleteByName(oldName);
                entry.Name = updated.Name;
                entry.Target = updated.Target;
                entry.Note = updated.Note;
                if (!SaveConfig()) return;
                RefreshList();
                SelectAlias(entry.Name);
                Log.Info("编辑别名：" + oldName + " -> " + entry.Name + "；目标=" + entry.Target);
                ShowStatus("已更新别名：" + entry.Name, Theme.Success);
            }
        }

        private void DeleteSelected()
        {
            AliasEntry entry = SelectedEntry;
            if (entry == null)
            {
                ShowStatus("请先选中一条别名", Theme.Warn);
                return;
            }
            string message = "删除别名“" + entry.Name + "”？\r\n\r\n快捷方式会被删除，但目标文件不会受影响。\r\n（快捷方式目录仍在 PATH 中。）";
            if (MessageBox.Show(this, message, "删除别名", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            AliasFile.Delete(entry);
            _config.Aliases.Remove(entry);
            if (!SaveConfig()) return;
            RefreshList();
            Log.Info("删除别名：" + entry.Name);
            ShowStatus("已删除别名：" + entry.Name, Theme.Success);
        }

        private void RunSelected()
        {
            AliasEntry entry = SelectedEntry;
            if (entry == null)
            {
                ShowStatus("请先选中一条别名", Theme.Warn);
                return;
            }
            if (!TargetExists(entry.Target, Shortcuts.ResolveIfLink(entry.Target)))
            {
                MessageBox.Show(this, "目标不存在：\r\n" + entry.Target + "\r\n\r\n可以点「编辑」修正路径。", "无法启动",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = entry.Target;
                info.UseShellExecute = true;
                string workdir = AliasFile.WorkingDirectoryFor(entry.Target);
                if (!string.IsNullOrEmpty(workdir) && Directory.Exists(workdir))
                    info.WorkingDirectory = workdir;
                Process.Start(info);
                Log.Info("立即运行：" + entry.Name + " -> " + entry.Target);
                ShowStatus("已启动：" + entry.Name, Theme.Success);
            }
            catch (Exception ex)
            {
                Log.Error("启动失败：" + entry.Target, ex);
                MessageBox.Show(this, "启动失败：" + ex.Message, "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopySelectedAlias()
        {
            AliasEntry entry = SelectedEntry;
            if (entry == null) return;
            try
            {
                Clipboard.SetText(entry.Name);
                ShowStatus("已复制别名：" + entry.Name, Theme.Success);
            }
            catch
            {
            }
        }

        private void ListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2)
            {
                EditSelected();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                RunSelected();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedAlias();
                e.Handled = true;
            }
        }

        // -------------------------------------------------------------
        // 菜单 / 对话框
        // -------------------------------------------------------------
        private void BuildContextMenus()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Items.Add("立即运行", null, delegate { RunSelected(); });
            _contextMenu.Items.Add("编辑别名…", null, delegate { EditSelected(); });
            _contextMenu.Items.Add("复制别名", null, delegate { CopySelectedAlias(); });
            _contextMenu.Items.Add("在资源管理器中显示目标", null, delegate { ShowTargetInExplorer(); });
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("删除别名", null, delegate { DeleteSelected(); });
            _contextMenu.Opening += delegate
            {
                bool has = SelectedEntry != null;
                foreach (ToolStripItem item in _contextMenu.Items)
                    item.Enabled = has;
            };
            _list.ContextMenuStrip = _contextMenu;

            _moreMenu = new ContextMenuStrip();
            _moreMenu.Items.Add("环境自检", null, delegate { ShowDiagnose(); });
            _moreMenu.Items.Add("重新生成全部别名", null, delegate { RepairEnvironment(); });
            _moreMenu.Items.Add(new ToolStripSeparator());
            _moreMenu.Items.Add("从用户 PATH 移除", null, delegate { RemoveFromPath(); });
            _moreMenu.Items.Add("打开数据目录", null, delegate { OpenFolder(AppEnv.DataDir); });
            _moreMenu.Items.Add("打开快捷方式目录", null, delegate { OpenFolder(AppEnv.AliasDir); });
            _moreMenu.Items.Add("更改快捷方式目录…", null, delegate { ChangeAliasDirectory(); });
            _moreMenu.Items.Add(new ToolStripSeparator());
            _moreMenu.Items.Add("使用说明", null, delegate { ShowHelp(); });
            _moreMenu.Items.Add("关于 QuickRun", null, delegate { ShowAbout(); });
        }

        private void ShowMoreMenu(Control anchor)
        {
            if (_moreMenu == null) return;
            if (anchor == null) anchor = _btnMoreTools;
            if (anchor == null) return;
            _moreMenu.Show(anchor, new Point(0, anchor.Height + 4));
        }

        private void ShowHelp()
        {
            using (HelpDialog dialog = new HelpDialog())
                dialog.ShowDialog(this);
        }

        private void ShowDiagnose()
        {
            string report = BuildDiagnoseText();
            using (DiagnoseDialog dialog = new DiagnoseDialog(report))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK || dialog.FixRequested)
                    RepairEnvironment();
            }
        }

        private string BuildDiagnoseText()
        {
            StringBuilder sb = new StringBuilder();
            PathCheckResult pathCheck = PathManager.Check();
            sb.AppendLine("QuickRun 环境自检");
            sb.AppendLine("==================");
            sb.AppendLine("程序版本：" + AppEnv.Version);
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("程序位置：" + Application.ExecutablePath);
            sb.AppendLine();
            sb.AppendLine("QuickRun 数据根目录：" + AppEnv.Root);
            sb.AppendLine("  存在：" + YesNo(Directory.Exists(AppEnv.Root)) + "，可写：" + YesNo(AppEnv.IsWritableDir(AppEnv.Root)));
            sb.AppendLine("快捷方式目录（PATH 项）：" + AppEnv.AliasDir);
            sb.AppendLine("  存在：" + YesNo(Directory.Exists(AppEnv.AliasDir)) + "，可写：" + YesNo(AppEnv.IsWritableDir(AppEnv.AliasDir)));
            sb.AppendLine("数据目录：" + AppEnv.DataDir);
            sb.AppendLine("配置文件：" + AppEnv.ConfigFile);
            sb.AppendLine("  存在：" + YesNo(File.Exists(AppEnv.ConfigFile)));
            sb.AppendLine("设置文件：" + AppEnv.SettingsFile);
            sb.AppendLine("  存在：" + YesNo(File.Exists(AppEnv.SettingsFile)));
            sb.AppendLine("快捷方式目录来源：" + (AppEnv.AliasDirFromEnvironment ? "环境变量 QUICKRUN_ALIAS_DIR" : "用户设置/默认"));
            if (!string.IsNullOrEmpty(AppEnv.AliasDirWarning))
                sb.AppendLine("  目录提示：" + AppEnv.AliasDirWarning);
            sb.AppendLine("日志文件：" + AppEnv.LogFile);
            sb.AppendLine();
            sb.AppendLine("用户 PATH：" + (pathCheck.Contains ? "已包含快捷方式目录" : "未包含快捷方式目录"));
            if (!string.IsNullOrEmpty(pathCheck.Error))
                sb.AppendLine("  PATH 访问错误：" + pathCheck.Error);
            sb.AppendLine("当前进程 PATH 可见快捷方式目录：" + YesNo(PathManager.ContainsEntry(Environment.GetEnvironmentVariable("Path"), AppEnv.AliasDir)));
            sb.AppendLine();
            sb.AppendLine("快捷方式条目（" + _config.Aliases.Count + "）：");
            if (_config.Aliases.Count == 0)
            {
                sb.AppendLine("  （还没有别名）");
            }
            else
            {
                foreach (AliasEntry entry in _config.Aliases)
                {
                    string resolved = Shortcuts.ResolveIfLink(entry.Target);
                    bool targetOk = TargetExists(entry.Target, resolved);
                    bool aliasOk = AliasFile.Exists(entry);
                    string mark = targetOk && aliasOk ? "[OK]" : "[!!]";
                    sb.AppendLine("  " + mark + " " + entry.Name + "  类型=" + KindOf(entry.Target));
                    sb.AppendLine("       快捷方式：" + YesNo(aliasOk) + "  " + AliasFile.PathFor(entry));
                    sb.AppendLine("       目标：" + entry.Target + "  （存在：" + YesNo(targetOk) + "）");
                    if (!string.Equals(resolved, entry.Target, StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine("       实际指向：" + resolved);
                }
            }
            sb.AppendLine();
            int others = CountOtherAliasFiles();
            sb.AppendLine("目录中其它 .lnk / .bat / .cmd（含手动放置，QuickRun 不管理）：" + others + " 个");
            sb.AppendLine();
            sb.AppendLine("说明：");
            sb.AppendLine("  · 每个条目是一个 .lnk 快捷方式，Win+R 通过用户 PATH 找到它（ShellExecute 支持无扩展名匹配）。");
            sb.AppendLine("  · 刚修改 PATH 后，资源管理器/运行框可能需要几秒刷新；必要时注销一次。");
            return sb.ToString();
        }

        private static string YesNo(bool value)
        {
            return value ? "是" : "否";
        }

        private int CountOtherAliasFiles()
        {
            int count = 0;
            try
            {
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (AliasEntry entry in _config.Aliases) names.Add(entry.Name);
                foreach (string file in Directory.GetFiles(AppEnv.AliasDir))
                {
                    string ext = Path.GetExtension(file);
                    if (!string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!names.Contains(name)) count++;
                }
            }
            catch
            {
            }
            return count;
        }

        private void RemoveFromPath()
        {
            if (MessageBox.Show(this,
                "从当前用户的 PATH 中移除快捷方式目录？\r\n\r\n" +
                "目录：" + AppEnv.AliasDir + "\r\n\r\n" +
                "注意：如果这个目录是你自己也在用的（例如 D:\\Quick 里还有手动快捷方式），" +
                "移除后这些快捷方式也会从 Win+R 里消失。快捷方式文件本身不会被删除。",
                "从 PATH 移除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            PathCheckResult check = PathManager.Remove(true);
            ApplyPathCheck(check);
            RefreshList();
            Log.Info("从用户 PATH 移除：" + check.Message);
            if (!check.Contains && string.IsNullOrEmpty(check.Error))
                ShowStatus("已从用户 PATH 移除快捷方式目录", Theme.Warn, 8000);
            else
                MessageBox.Show(this, check.Message + (string.IsNullOrEmpty(check.Error) ? "" : "\r\n\r\n" + check.Error),
                    "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ShowTargetInExplorer()
        {
            AliasEntry entry = SelectedEntry;
            if (entry == null) return;
            try
            {
                string resolved = Shortcuts.ResolveIfLink(entry.Target);
                if (Directory.Exists(resolved))
                    OpenFolder(resolved);
                else if (File.Exists(resolved))
                    Process.Start("explorer.exe", "/select,\"" + resolved + "\"");
                else
                    ShowStatus("目标不存在，无法定位", Theme.Warn);
            }
            catch (Exception ex)
            {
                ShowStatus("打开资源管理器失败：" + ex.Message, Theme.Danger);
            }
        }

        private void ShowAbout()
        {
            using (AboutDialog dialog = new AboutDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        private void ChangeAliasDirectory()
        {
            if (AppEnv.AliasDirFromEnvironment)
            {
                MessageBox.Show(this,
                    "当前快捷方式目录由环境变量 QUICKRUN_ALIAS_DIR 指定：\r\n" +
                    AppEnv.AliasDir + "\r\n\r\n" +
                    "要在这里修改，请先删除该环境变量（或改为你想要的路径），然后重新打开 QuickRun。",
                    "目录由环境变量指定", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 .lnk 快捷方式的存放目录（该目录会自动加入当前用户 PATH）";
                dialog.ShowNewFolderButton = true;
                try { dialog.SelectedPath = AppEnv.AliasDir; }
                catch { }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplyAliasDirectory(dialog.SelectedPath);
            }
        }

        private bool ApplyAliasDirectory(string requestedDir)
        {
            if (string.IsNullOrWhiteSpace(requestedDir))
            {
                MessageBox.Show(this, "目录不能为空。", "更改目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string newDir;
            try
            {
                newDir = Path.GetFullPath(requestedDir);
                string pathRoot = Path.GetPathRoot(newDir);
                if (!string.IsNullOrEmpty(pathRoot) && newDir.Length > pathRoot.Length)
                    newDir = newDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "目录路径无效：" + ex.Message, "更改目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string oldDir = AppEnv.AliasDir;
            if (AppEnv.SameDirectory(newDir, oldDir))
            {
                ShowStatus("快捷方式目录没有变化", Theme.Muted, 4000);
                return false;
            }
            if (File.Exists(newDir))
            {
                MessageBox.Show(this, "所选路径是一个文件，不是文件夹：\r\n" + newDir,
                    "更改目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (AppEnv.SameDirectory(newDir, AppEnv.DataDir) ||
                IsDirectoryInside(newDir, AppEnv.DataDir) ||
                IsDirectoryInside(AppEnv.DataDir, newDir))
            {
                MessageBox.Show(this,
                    "不能把快捷方式目录设为 QuickRun 的数据目录或其上级/下级目录。\r\n\r\n" +
                    "数据目录：" + AppEnv.DataDir + "\r\n请选择另一个独立目录，例如 D:\\Quick。",
                    "更改目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!AppEnv.IsWritableDir(newDir))
            {
                MessageBox.Show(this, "这个目录不可写，无法保存快捷方式：\r\n" + newDir +
                    "\r\n\r\n请选择有写权限的目录。", "更改目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            List<string> collisions = new List<string>();
            foreach (AliasEntry entry in _config.Aliases)
            {
                if (AliasFile.AnyFileExistsIn(entry.Name, newDir))
                    collisions.Add(entry.Name);
            }
            if (collisions.Count > 0)
            {
                string list = string.Join("、", collisions.ToArray());
                MessageBox.Show(this,
                    "目标目录里已经有同名的快捷方式/脚本，为避免覆盖你的文件，不能直接切换：\r\n\r\n" +
                    list + "\r\n\r\n请清空这些同名文件、换一个目录，或先在列表里给相关别名改名。",
                    "目录里有同名文件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            DialogResult confirm = MessageBox.Show(this,
                "把快捷方式目录改为：\r\n" + newDir + "\r\n\r\n" +
                "现有别名会被迁移到新目录；旧目录中不属于 QuickRun 的文件不会被删除。\r\n" +
                "新目录会自动加入当前用户 PATH。\r\n\r\n是否继续？",
                "更改快捷方式目录", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return false;

            List<string> written = new List<string>();
            try
            {
                foreach (AliasEntry entry in _config.Aliases)
                {
                    AliasFile.WriteTo(entry, newDir);
                    written.Add(AliasFile.PathForIn(entry, newDir));
                }
            }
            catch (Exception ex)
            {
                foreach (string path in written)
                {
                    try { File.Delete(path); }
                    catch { }
                }
                MessageBox.Show(this, "生成新快捷方式失败，已保留原目录设置：\r\n" + ex.Message,
                    "更改目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            try
            {
                string unusedWarning;
                AppSettings settings = SettingsStore.Load(AppEnv.DataDir, out unusedWarning);
                settings.AliasDir = newDir;
                SettingsStore.Save(settings, AppEnv.DataDir);
            }
            catch (Exception ex)
            {
                foreach (string path in written)
                {
                    try { File.Delete(path); }
                    catch { }
                }
                MessageBox.Show(this, "保存目录设置失败，已保留原目录设置：\r\n" + ex.Message,
                    "更改目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            AppEnv.AliasDir = newDir;
            AppEnv.AliasDirWarning = "";

            if (!AppEnv.SameDirectory(oldDir, newDir))
            {
                foreach (AliasEntry entry in _config.Aliases)
                {
                    try { AliasFile.DeleteIn(oldDir, entry.Name); }
                    catch (Exception ex) { Log.Warn("切换目录后清理旧快捷方式失败：" + entry.Name + "：" + ex.Message); }
                }
            }

            PathCheckResult check = PathManager.EnsureAdded(true);
            ApplyPathCheck(check);
            RefreshList();
            if (_quickRunLink != null) _toolTip.SetToolTip(_quickRunLink, AppEnv.AliasDir);
            Log.Info("更改快捷方式目录：" + oldDir + " -> " + newDir + "；PATH=" + check.Message +
                (string.IsNullOrEmpty(check.Error) ? "" : "；错误=" + check.Error));

            if (check.Contains)
                ShowStatus("快捷方式目录已改为：" + newDir, Theme.Success, 10000);
            else
                ShowStatus("目录已改为：" + newDir + "，但 PATH 未就绪，请点「修复环境」", Theme.Warn, 12000);

            AskRemoveEmptyOldDirectory(oldDir, newDir);
            return true;
        }

        private static bool IsDirectoryInside(string path, string parent)
        {
            try
            {
                string p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string q = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(p, q, StringComparison.OrdinalIgnoreCase)) return false;
                return p.StartsWith(q + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void AskRemoveEmptyOldDirectory(string oldDir, string newDir)
        {
            if (AppEnv.SameDirectory(oldDir, newDir) || !Directory.Exists(oldDir)) return;
            try
            {
                if (Directory.GetFileSystemEntries(oldDir).Length != 0) return;
            }
            catch
            {
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                "旧快捷方式目录已经空了：\r\n" + oldDir + "\r\n\r\n" +
                "是否同时把它从当前用户 PATH 中移除？\r\n" +
                "（目录本身不会删除。）",
                "清理旧 PATH 项", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            try
            {
                PathCheckResult remove = PathManager.RemoveFor(oldDir, true);
                if (!remove.Contains && string.IsNullOrEmpty(remove.Error))
                    ShowStatus("已同时从 PATH 移除旧目录", Theme.Success, 8000);
                else
                    MessageBox.Show(this, remove.Message + (string.IsNullOrEmpty(remove.Error) ? "" : "\r\n\r\n" + remove.Error),
                        "旧 PATH 项未移除", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "移除旧 PATH 项失败：" + ex.Message, "操作未完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开目录失败：" + ex.Message + "\r\n" + path, "打开失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool SaveConfig()
        {
            try
            {
                ConfigStore.Save(_config);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败", ex);
                MessageBox.Show(this, "保存配置失败：" + ex.Message + "\r\n\r\n日志：" + AppEnv.LogFile,
                    "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ShowStatus(string message, Color color)
        {
            ShowStatus(message, color, 6000);
        }

        private void ShowStatus(string message, Color color, int milliseconds)
        {
            if (_hintLabel == null) return;
            _hintLabel.Text = message;
            _hintLabel.ForeColor = color;
            if (_statusTimer != null)
            {
                _statusTimer.Stop();
                _statusTimer.Interval = Math.Max(1000, milliseconds);
                _statusTimer.Start();
            }
        }

        private static AppConfig CreateDemoConfig()
        {
            AppConfig config = new AppConfig();
            AddDemo(config, "edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "浏览器");
            AddDemo(config, "notepad", @"C:\Windows\System32\notepad.exe", "记事本");
            AddDemo(config, "paint", @"C:\Windows\System32\mspaint.exe", "画图");
            AddDemo(config, "powershell", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", "命令行");
            return config;
        }

        private static void AddDemo(AppConfig config, string name, string target, string note)
        {
            AliasEntry entry = new AliasEntry();
            entry.Name = name;
            entry.Target = target;
            entry.Note = note;
            entry.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            config.Aliases.Add(entry);
        }
    }

    // =================================================================
    // 编辑别名对话框
    // =================================================================
    internal sealed class AliasEditorDialog : Form
    {
        private readonly AliasEntry _original;
        private readonly AliasEntry _sourceEntry;
        private readonly AppConfig _config;
        private readonly string _originalName;

        private TextBox _nameBox;
        private TextBox _targetBox;
        private TextBox _noteBox;
        private Label _kindLabel;
        private Label _previewLabel;
        private Label _errorLabel;
        private Label _shadowLabel;
        private RoundedButton _saveButton;
        private RoundedButton _browseButton;
        private LinkLabel _autoLink;

        public AliasEntry Result { get; private set; }

        public AliasEditorDialog(AliasEntry entry, AppConfig config, bool preview)
        {
            _sourceEntry = entry;
            _original = entry.Clone();
            _config = config;
            _originalName = entry.Name;
            Result = entry.Clone();

            Text = "编辑别名 · " + entry.Name;
            Font = Theme.FontUI;
            BackColor = Theme.Bg;
            ClientSize = new Size(590, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = SystemIcons.Application;

            BuildUi();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.BackColor = Theme.Bg;
            root.Padding = new Padding(20, 16, 20, 14);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            Controls.Add(root);

            Label title = new Label();
            title.Text = "编辑别名";
            title.Font = Theme.FontDialogTitle;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.BackColor = Theme.Bg;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(title, 0, 0);

            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(22, 14, 22, 14);
            root.Controls.Add(card, 0, 1);

            TableLayoutPanel form = new TableLayoutPanel();
            form.Dock = DockStyle.Fill;
            form.ColumnCount = 2;
            form.RowCount = 9;
            form.BackColor = Theme.Card;
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            form.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            card.Controls.Add(form);

            Label nameLabel = MakeLabel("别名");
            form.Controls.Add(nameLabel, 0, 0);

            _nameBox = new TextBox();
            _nameBox.Dock = DockStyle.Fill;
            _nameBox.Font = Theme.MakeFont(10F, FontStyle.Regular);
            _nameBox.Text = _original.Name;
            _nameBox.Margin = new Padding(0, 8, 0, 0);
            _nameBox.TextChanged += delegate { UpdatePreview(); };
            form.Controls.Add(_nameBox, 1, 0);

            Label nameHint = MakeHint("Win+R 里输入这个名字；建议用短英文，避免和系统命令重名。");
            form.Controls.Add(nameHint, 1, 1);

            Label targetLabel = MakeLabel("目标");
            form.Controls.Add(targetLabel, 0, 2);

            TableLayoutPanel targetRow = new TableLayoutPanel();
            targetRow.Dock = DockStyle.Fill;
            targetRow.ColumnCount = 2;
            targetRow.RowCount = 1;
            targetRow.BackColor = Theme.Card;
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));
            targetRow.Margin = new Padding(0, 8, 0, 0);
            form.Controls.Add(targetRow, 1, 2);

            _targetBox = new TextBox();
            _targetBox.Dock = DockStyle.Fill;
            _targetBox.Font = Theme.MakeFont(9F, FontStyle.Regular);
            _targetBox.Text = _original.Target;
            _targetBox.TextChanged += delegate { UpdatePreview(); };
            targetRow.Controls.Add(_targetBox, 0, 0);

            _browseButton = new RoundedButton("浏览…", ButtonKind.Secondary);
            _browseButton.Dock = DockStyle.Fill;
            _browseButton.Margin = new Padding(8, 0, 0, 0);
            _browseButton.Click += delegate { BrowseTarget(); };
            targetRow.Controls.Add(_browseButton, 1, 0);

            _kindLabel = MakeHint("类型：—");
            form.Controls.Add(_kindLabel, 1, 3);

            Label noteLabel = MakeLabel("备注");
            form.Controls.Add(noteLabel, 0, 4);

            _noteBox = new TextBox();
            _noteBox.Dock = DockStyle.Fill;
            _noteBox.Font = Theme.FontUI;
            _noteBox.Margin = new Padding(0, 6, 0, 0);
            _noteBox.Text = _original.Note;
            form.Controls.Add(_noteBox, 1, 4);

            Label fileLabel = MakeLabel("生成文件");
            form.Controls.Add(fileLabel, 0, 5);

            _previewLabel = new Label();
            _previewLabel.Dock = DockStyle.Fill;
            _previewLabel.TextAlign = ContentAlignment.MiddleLeft;
            _previewLabel.ForeColor = Theme.Muted;
            _previewLabel.Font = Theme.FontUI;
            _previewLabel.AutoEllipsis = true;
            _previewLabel.BackColor = Theme.Card;
            form.Controls.Add(_previewLabel, 1, 5);

            _autoLink = new LinkLabel();
            _autoLink.Text = "按目标文件名自动生成别名";
            _autoLink.AutoSize = true;
            _autoLink.Font = Theme.FontSmall;
            _autoLink.LinkColor = Theme.Primary;
            _autoLink.BackColor = Theme.Card;
            _autoLink.LinkBehavior = LinkBehavior.HoverUnderline;
            _autoLink.Margin = new Padding(0, 6, 0, 0);
            _autoLink.Click += delegate
            {
                string target = _targetBox.Text.Trim();
                if (target.Length == 0) return;
                _nameBox.Text = AliasRules.FromTarget(target);
                _nameBox.Focus();
                _nameBox.SelectAll();
            };
            form.Controls.Add(_autoLink, 1, 6);

            _shadowLabel = new Label();
            _shadowLabel.Dock = DockStyle.Fill;
            _shadowLabel.TextAlign = ContentAlignment.MiddleLeft;
            _shadowLabel.ForeColor = Theme.Warn;
            _shadowLabel.Font = Theme.FontSmall;
            _shadowLabel.AutoEllipsis = true;
            _shadowLabel.BackColor = Theme.Card;
            form.Controls.Add(_shadowLabel, 1, 7);

            _errorLabel = new Label();
            _errorLabel.Dock = DockStyle.Fill;
            _errorLabel.TextAlign = ContentAlignment.TopLeft;
            _errorLabel.ForeColor = Theme.Danger;
            _errorLabel.Font = Theme.FontUI;
            _errorLabel.AutoEllipsis = true;
            _errorLabel.BackColor = Theme.Card;
            form.Controls.Add(_errorLabel, 0, 8);
            form.SetColumnSpan(_errorLabel, 2);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.ColumnCount = 3;
            buttons.RowCount = 1;
            buttons.BackColor = Theme.Bg;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            root.Controls.Add(buttons, 0, 2);

            RoundedButton cancel = new RoundedButton("取消", ButtonKind.Secondary);
            cancel.Dock = DockStyle.Fill;
            cancel.Margin = new Padding(0, 5, 8, 5);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel, 1, 0);

            _saveButton = new RoundedButton("保存", ButtonKind.Primary);
            _saveButton.Dock = DockStyle.Fill;
            _saveButton.Margin = new Padding(0, 5, 0, 5);
            _saveButton.Click += delegate { SaveAndClose(); };
            buttons.Controls.Add(_saveButton, 2, 0);

            AcceptButton = _saveButton;
            CancelButton = cancel;
            UpdatePreview();
        }

        private static Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Theme.Text;
            label.Font = Theme.FontUI;
            label.BackColor = Theme.Card;
            return label;
        }

        private static Label MakeHint(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Theme.Muted;
            label.Font = Theme.FontSmall;
            label.AutoEllipsis = true;
            label.BackColor = Theme.Card;
            return label;
        }

        private void BrowseTarget()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择目标程序或快捷方式";
                dialog.Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.com;*.msi|所有文件|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _targetBox.Text = dialog.FileName;
                    string previewName = AliasRules.FromTarget(dialog.FileName);
                    if (!string.IsNullOrEmpty(previewName)) _nameBox.Text = previewName;
                }
            }
        }

        private void UpdatePreview()
        {
            if (_previewLabel == null) return;
            string name = _nameBox.Text.Trim();
            if (name.Length == 0) name = "<别名>";
            string fileName = name + AliasFile.Extension;
            _previewLabel.Text = Path.Combine(AppEnv.AliasDir, fileName);
            if (_kindLabel != null)
                _kindLabel.Text = "类型：" + KindOf(_targetBox.Text.Trim());

            if (_shadowLabel != null)
            {
                string validationError;
                bool validName = name.Length > 0 && AliasRules.Validate(name, out validationError);
                bool shadowed = validName && AliasRules.ExternalCommandConflicts(name, _targetBox.Text.Trim());
                if (shadowed && !string.Equals(name, _originalName, StringComparison.OrdinalIgnoreCase))
                {
                    _shadowLabel.Text = "提示：PATH 中已有同名命令，Win+R 可能会优先启动它。";
                }
                else if (shadowed)
                {
                    _shadowLabel.Text = "提示：PATH 中已有同名命令；如需避免冲突，建议换一个别名。";
                }
                else
                {
                    _shadowLabel.Text = "";
                }
            }
        }

        private static string KindOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "—";
            try
            {
                if (Directory.Exists(path)) return "文件夹";
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".lnk") return "快捷方式";
                if (ext == ".exe" || ext == ".com") return "程序";
                if (ext == ".bat" || ext == ".cmd") return "脚本";
                if (ext == ".msi") return "安装包";
            }
            catch
            {
            }
            return "文件";
        }

        private void SaveAndClose()
        {
            _errorLabel.Text = "";
            string name = _nameBox.Text.Trim();
            string target = _targetBox.Text.Trim();

            string error;
            if (!AliasRules.Validate(name, out error))
            {
                _errorLabel.Text = error;
                _nameBox.Focus();
                return;
            }

            if (target.Length == 0)
            {
                _errorLabel.Text = "目标不能为空。";
                _targetBox.Focus();
                return;
            }

            try
            {
                target = Path.GetFullPath(target);
            }
            catch (Exception ex)
            {
                _errorLabel.Text = "目标路径无效：" + ex.Message;
                return;
            }

            foreach (AliasEntry entry in _config.Aliases)
            {
                if (ReferenceEquals(entry, _sourceEntry)) continue;
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    _errorLabel.Text = "别名“" + name + "”已经被另一条记录使用。";
                    _nameBox.Focus();
                    return;
                }
            }

            if (!string.Equals(name, _originalName, StringComparison.OrdinalIgnoreCase) && AliasFile.AnyFileExists(name))
            {
                _errorLabel.Text = "快捷方式目录里已经存在“" + name + ".lnk”，请换一个别名。";
                _nameBox.Focus();
                return;
            }

            if (!File.Exists(target) && !Directory.Exists(target))
            {
                DialogResult choice = MessageBox.Show(this, "目标不存在：\r\n" + target + "\r\n\r\n仍然保存吗？",
                    "目标不存在", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice != DialogResult.Yes) return;
            }

            Result = _original.Clone();
            Result.Name = name;
            Result.Target = target;
            Result.Note = _noteBox.Text.Trim();
            if (string.IsNullOrEmpty(Result.CreatedAt))
                Result.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // =================================================================
    // 说明对话框
    // =================================================================
    internal sealed class HelpDialog : Form
    {
        public HelpDialog()
        {
            Text = "QuickRun 使用说明";
            Font = Theme.FontUI;
            BackColor = Theme.Bg;
            ClientSize = new Size(660, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = SystemIcons.Application;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.BackColor = Theme.Bg;
            root.Padding = new Padding(20, 16, 20, 14);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            Controls.Add(root);

            Label title = new Label();
            title.Text = "QuickRun 使用说明";
            title.Font = Theme.FontDialogTitle;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.BackColor = Theme.Bg;
            root.Controls.Add(title, 0, 0);

            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16, 12, 16, 12);
            root.Controls.Add(card, 0, 1);

            TextBox text = new TextBox();
            text.Dock = DockStyle.Fill;
            text.Multiline = true;
            text.ReadOnly = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.BorderStyle = BorderStyle.None;
            text.BackColor = Theme.Card;
            text.ForeColor = Theme.Text;
            text.Font = Theme.MakeFont(9.5F, FontStyle.Regular);
            text.Text = BuildHelpText();
            card.Controls.Add(text);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.ColumnCount = 3;
            buttons.RowCount = 1;
            buttons.BackColor = Theme.Bg;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            root.Controls.Add(buttons, 0, 2);

            RoundedButton open = new RoundedButton("打开快捷方式目录", ButtonKind.Secondary);
            open.Dock = DockStyle.Fill;
            open.Margin = new Padding(0, 5, 8, 5);
            open.Click += delegate
            {
                try
                {
                    Directory.CreateDirectory(AppEnv.AliasDir);
                    Process.Start("explorer.exe", "\"" + AppEnv.AliasDir + "\"");
                }
                catch
                {
                }
            };
            buttons.Controls.Add(open, 1, 0);

            RoundedButton close = new RoundedButton("关闭", ButtonKind.Primary);
            close.Dock = DockStyle.Fill;
            close.Margin = new Padding(0, 5, 0, 5);
            close.DialogResult = DialogResult.OK;
            buttons.Controls.Add(close, 2, 0);
            AcceptButton = close;
            CancelButton = close;
        }

        private static string BuildHelpText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("一句话");
            sb.AppendLine("------");
            sb.AppendLine("把 exe / 快捷方式拖进窗口，程序自动在快捷方式目录生成一个 .lnk 快捷方式，");
            sb.AppendLine("并把该目录加入当前用户的 PATH。以后按 Win+R，输入别名，回车即可启动。");
            sb.AppendLine();
            sb.AppendLine("三步上手");
            sb.AppendLine("--------");
            sb.AppendLine("1. 双击 QuickRun.exe 打开窗口。");
            sb.AppendLine("2. 把 exe / 快捷方式拖进蓝色区域（支持一次拖多个），也可以点「添加文件」。");
            sb.AppendLine("3. 按 Win+R，输入列表里的别名，回车。不用带 .lnk。");
            sb.AppendLine();
            sb.AppendLine("更改快捷方式存放目录");
            sb.AppendLine("--------------------");
            sb.AppendLine("点右上角「更多」→「更改快捷方式目录…」，选择一个新文件夹即可。");
            sb.AppendLine("现有别名会迁移到新目录，新目录自动加入用户 PATH；");
            sb.AppendLine("旧目录里不属于 QuickRun 的文件不会被删除。设置会保存在数据目录的 settings.json。");
            sb.AppendLine();
            sb.AppendLine("它做了什么");
            sb.AppendLine("------------");
            sb.AppendLine("· 快捷方式目录：");
            sb.AppendLine("  " + AppEnv.AliasDir);
            sb.AppendLine("  每个条目是一个 <别名>.lnk 快捷方式，指向原程序/文件夹。");
            sb.AppendLine("· PATH：自动把上面的快捷方式目录写入当前用户 PATH（HKCU\\Environment），");
            sb.AppendLine("  不需要管理员权限，也不需要手动改环境变量。");
            sb.AppendLine("· 刷新：写入 PATH 后会广播环境变量变更通知。刚添加的别名通常立即可用；");
            sb.AppendLine("  如果 Win+R 还找不到，点「修复环境」，或注销/重启资源管理器。");
            sb.AppendLine();
            sb.AppendLine("别名规则");
            sb.AppendLine("--------");
            sb.AppendLine("· 只能使用字母、数字、短横线（-）、下划线（_），最多 40 个字符；");
            sb.AppendLine("· 建议用短英文，Win+R 输入时不用切换输入法；");
            sb.AppendLine("· 自动命名会避开已存在的快捷方式和 PATH 里的同名命令，重名时自动加 -2、-3 等后缀。");
            sb.AppendLine();
            sb.AppendLine("常见问题");
            sb.AppendLine("--------");
            sb.AppendLine("Q: Win+R 输入别名没反应？");
            sb.AppendLine("A: 先看顶部 PATH 状态，点「修复环境」；仍不行就注销一次，或重启 Windows 资源管理器。");
            sb.AppendLine();
            sb.AppendLine("Q: 为什么这次没有黑色命令行窗口？");
            sb.AppendLine("A: 因为别名已经是 .lnk 快捷方式，由 Shell 直接启动目标，不会再经过 cmd.exe。");
            sb.AppendLine();
            sb.AppendLine("Q: 目标被移动或删除了？");
            sb.AppendLine("A: 列表状态会显示「目标丢失」。选中后点「编辑」，重新选择目标即可。");
            sb.AppendLine();
            sb.AppendLine("Q: 如何卸载？");
            sb.AppendLine("A: 在程序里删掉全部别名，再删除数据目录即可；");
            sb.AppendLine("   如果快捷方式目录里还有你手动放的文件，不要整个删除。");
            sb.AppendLine("   需要时再从用户 PATH 里移除快捷方式目录。");
            sb.AppendLine();
            sb.AppendLine("文件位置");
            sb.AppendLine("--------");
            sb.AppendLine("数据根目录：" + AppEnv.Root);
            sb.AppendLine("快捷方式目录：" + AppEnv.AliasDir);
            sb.AppendLine("配置：" + AppEnv.ConfigFile);
            sb.AppendLine("设置：" + AppEnv.SettingsFile);
            sb.AppendLine("日志：" + AppEnv.LogFile);
            sb.AppendLine();
            sb.AppendLine("整个工具只写当前用户目录、快捷方式目录和 HKCU 的 PATH，不需要管理员权限。");
            sb.AppendLine();
            sb.AppendLine("作者：zhlipe");
            return sb.ToString();
        }
    }

    // =================================================================
    // 关于对话框
    // =================================================================
    internal sealed class AboutDialog : Form
    {
        private const string GitHubUrl = "https://github.com/zhlipe/QuickRun";
        private const string GitHubLabel = "github.com/zhlipe/QuickRun";
        private const string GitHubMarkBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAP9SURBVFhH1VdZS5VRFHUeM+epkAYRNYjAbqGm2YBJLxmV9CQNEpVlkILVD0hJm8BIKzCxNNBKMaiXTM03U1Mje/FBXxX1QRxwWp21/T693vvpvQplLTjw3bP3XnudaZ9zHebn5/tUG96g1kcBY9ggMDcFDGu//zqY+/8SMDs7i5aWFhQWFODC+fM4mZ6OkydOyDf7mpubMTMzo3nbht0C5ubm8KysDPv27oWXhwecHR3h5uICT3d3afxmH20m5VNWWoo5JdYW7BLwq7cXh1JS4OjggE1eXggJCkJocLBho40+9E1JTkbvz58aizFsCmhtbcWWsDC4OTsjdJXERo2zEh4aiq9qyVbCqgJ+9PQIEUcU4OcHb09PIQ0KCJD+4MBABPr7S+M3+2ijD30Zo89Yd3e3xrocKwqYmJjAfpMJHm5usq6XsrLwpbERN3JyJAnX29/XF1vDw6UxGfso5np2Nho/f8aVy5cllhymuDiMj49r7EtYUcC9wkI4qHUMCwmBi5MT3tbWahags7MTVVVV6Pr+HQP9/RgYGEBXVxeqVV97e7vmBdTX18NVLR05yFVw965mWYKhgNHRUUTu2AG/zZtlWklSWVmpWe3Hm+pqiSUHuXZu346R4eVjNRTAkXA6GcjmpNS/eP5cs9qPioqKZTz8fv3qlWZdgKGArIsXF3a9CvLx9kZSYiKmpqY0q/2Ynp7GYXV8uRHJxc3JgmUOKwHqWxIyMYO4/g8fPNCsa8eTkhLh0AdzICFBcuiwEjA2NoZdsbGL60/V79+906xrx4eGBri7ugoXOXfFxEgOHVYCRkZGEB0VJUeMQdxE3EzrBU8PB0EucpKbOXRYCZianMSe3bvh6+MjQS5q4xgdH3tRXFS0uBHJSW7m0GElgDiWmioFhEGsaEePHNEsa0ea4iIHuchJbnMYCriVn7+ompWNU7ieZaipqZGbUr+8yElucxgK4OXB8snA2OhoGQGP0svycnkP2AJ9ypUvS7a+l8hFTsuLyVAA7/6DSUlwUoofP3qEb21t2BYRIeWUxygvN1fuBUuwL/fmTSTEx8vO5/3AMkwBnAlyktschgKITx8/ypRx3ZqamqT+cwQUwVZrdjfoqKurE5teePTG0bMWkNMSKwogrl29KoQ8OkRHRwfy8vJQ+vSp4bNLv0M4cj25fhFlKy4jrCpgUh2X42lpQnAuMxPDFheJJSjAvIaw8WVEDnIZYVUBBKtWxunTIoKEfO/xqWVeTHRQYFRkpMwARTDmzKlTyyqfJWwK0HG/uFjWkqQsKENDQ5plCUODgyKSPnzGMcYW7BZA8OFx5/ZtnM3IMHzdsI82+tDXHqxJwJ/APyNgw/+cbuDf8/m+3wZtKojMf0nHAAAAAElFTkSuQmCC";
        private static Image _githubMark;

        public AboutDialog()
        {
            Text = "关于 QuickRun";
            Font = Theme.FontUI;
            BackColor = Theme.Bg;
            ClientSize = new Size(560, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = SystemIcons.Application;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.BackColor = Theme.Bg;
            root.Padding = new Padding(20, 16, 20, 14);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            Controls.Add(root);

            Label title = new Label();
            title.Text = "关于 QuickRun";
            title.Font = Theme.FontDialogTitle;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.BackColor = Theme.Bg;
            root.Controls.Add(title, 0, 0);

            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(22, 18, 22, 18);
            root.Controls.Add(card, 0, 1);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 1;
            body.RowCount = 5;
            body.BackColor = Theme.Card;
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            card.Controls.Add(body);

            Label appName = new Label();
            appName.Text = "QuickRun  " + AppEnv.Version;
            appName.Font = Theme.FontTitle;
            appName.ForeColor = Theme.Text;
            appName.AutoSize = true;
            appName.TextAlign = ContentAlignment.MiddleLeft;
            appName.BackColor = Theme.Card;
            body.Controls.Add(appName, 0, 0);

            Label description = new Label();
            description.Text = "拖入 exe / 快捷方式 → 自动生成 .lnk 快捷方式 → 自动维护用户 PATH。\r\n" +
                "无需管理员权限，只写当前用户目录、快捷方式目录和 HKCU 的 PATH。";
            description.Font = Theme.FontUI;
            description.ForeColor = Theme.Text;
            description.Dock = DockStyle.Fill;
            description.TextAlign = ContentAlignment.TopLeft;
            description.BackColor = Theme.Card;
            body.Controls.Add(description, 0, 1);

            Label author = new Label();
            author.Text = "作者：zhlipe";
            author.Font = Theme.MakeFont(10F, FontStyle.Bold);
            author.ForeColor = Theme.Text;
            author.AutoSize = true;
            author.TextAlign = ContentAlignment.MiddleLeft;
            author.BackColor = Theme.Card;
            body.Controls.Add(author, 0, 2);

            FlowLayoutPanel githubRow = new FlowLayoutPanel();
            githubRow.FlowDirection = FlowDirection.LeftToRight;
            githubRow.WrapContents = false;
            githubRow.AutoSize = true;
            githubRow.BackColor = Theme.Card;
            githubRow.Margin = new Padding(0);

            PictureBox githubIcon = new PictureBox();
            githubIcon.Image = GetGitHubMark();
            githubIcon.Size = new Size(18, 18);
            githubIcon.SizeMode = PictureBoxSizeMode.Zoom;
            githubIcon.BackColor = Color.Transparent;
            githubIcon.Margin = new Padding(0, 1, 7, 0);
            githubRow.Controls.Add(githubIcon);

            LinkLabel githubLink = new LinkLabel();
            githubLink.Text = GitHubLabel;
            githubLink.AutoSize = true;
            githubLink.Font = Theme.MakeFont(9.5F, FontStyle.Bold);
            githubLink.LinkColor = Theme.Primary;
            githubLink.ActiveLinkColor = Theme.PrimaryDark;
            githubLink.VisitedLinkColor = Theme.Primary;
            githubLink.Links.Add(0, GitHubLabel.Length, GitHubUrl);
            githubLink.LinkClicked += delegate { OpenExternalLink(GitHubUrl); };
            githubRow.Controls.Add(githubLink);
            body.Controls.Add(githubRow, 0, 3);

            Label details = new Label();
            details.Text = "数据目录：" + AppEnv.Root + "\r\n" +
                "快捷方式目录：" + AppEnv.AliasDir + "\r\n" +
                "日志：" + AppEnv.LogFile;
            details.Font = Theme.FontSmall;
            details.ForeColor = Theme.Muted;
            details.AutoSize = true;
            details.MaximumSize = new Size(490, 0);
            details.BackColor = Theme.Card;
            body.Controls.Add(details, 0, 4);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.ColumnCount = 2;
            buttons.RowCount = 1;
            buttons.BackColor = Theme.Bg;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            root.Controls.Add(buttons, 0, 2);

            RoundedButton close = new RoundedButton("关闭", ButtonKind.Secondary);
            close.Dock = DockStyle.Fill;
            close.Margin = new Padding(0, 5, 0, 5);
            close.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(close, 1, 0);
            CancelButton = close;
            AcceptButton = close;
        }

        private static Image GetGitHubMark()
        {
            if (_githubMark == null)
            {
                byte[] bytes = Convert.FromBase64String(GitHubMarkBase64);
                using (MemoryStream stream = new MemoryStream(bytes))
                using (Image image = Image.FromStream(stream))
                {
                    _githubMark = new Bitmap(image);
                }
            }
            return _githubMark;
        }

        private void OpenExternalLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开浏览器失败：" + ex.Message, "打开链接",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // =================================================================
    // 环境自检对话框
    // =================================================================
    internal sealed class DiagnoseDialog : Form
    {
        public bool FixRequested { get; private set; }

        public DiagnoseDialog(string report)
        {
            Text = "QuickRun 环境自检";
            Font = Theme.FontUI;
            BackColor = Theme.Bg;
            ClientSize = new Size(720, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = SystemIcons.Application;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.BackColor = Theme.Bg;
            root.Padding = new Padding(20, 16, 20, 14);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            Controls.Add(root);

            Label title = new Label();
            title.Text = "环境自检";
            title.Font = Theme.FontDialogTitle;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.BackColor = Theme.Bg;
            root.Controls.Add(title, 0, 0);

            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(16, 12, 16, 12);
            root.Controls.Add(card, 0, 1);

            TextBox text = new TextBox();
            text.Dock = DockStyle.Fill;
            text.Multiline = true;
            text.ReadOnly = true;
            text.ScrollBars = ScrollBars.Both;
            text.WordWrap = false;
            text.BorderStyle = BorderStyle.None;
            text.BackColor = Theme.Card;
            text.ForeColor = Theme.Text;
            text.Font = Theme.MakeFont(9F, FontStyle.Regular);
            text.Text = report;
            card.Controls.Add(text);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.ColumnCount = 4;
            buttons.RowCount = 1;
            buttons.BackColor = Theme.Bg;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            root.Controls.Add(buttons, 0, 2);

            RoundedButton open = new RoundedButton("打开日志目录", ButtonKind.Secondary);
            open.Dock = DockStyle.Fill;
            open.Margin = new Padding(0, 5, 8, 5);
            open.Click += delegate
            {
                try
                {
                    Directory.CreateDirectory(AppEnv.DataDir);
                    Process.Start("explorer.exe", "\"" + AppEnv.DataDir + "\"");
                }
                catch
                {
                }
            };
            buttons.Controls.Add(open, 1, 0);

            RoundedButton fix = new RoundedButton("修复环境", ButtonKind.Primary);
            fix.Dock = DockStyle.Fill;
            fix.Margin = new Padding(0, 5, 8, 5);
            fix.Click += delegate
            {
                FixRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(fix, 2, 0);

            RoundedButton close = new RoundedButton("关闭", ButtonKind.Secondary);
            close.Dock = DockStyle.Fill;
            close.Margin = new Padding(0, 5, 0, 5);
            close.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(close, 3, 0);
            CancelButton = close;
        }
    }

    // =================================================================
    // 内部自检
    // =================================================================
    internal static class SelfTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly List<string> Lines = new List<string>();

        public static int Run(string reportPath)
        {
            _pass = 0;
            _fail = 0;
            Lines.Clear();
            Lines.Add("QuickRun 内部自检");
            Lines.Add("==================");
            Lines.Add("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Lines.Add("版本：" + AppEnv.Version);
            Lines.Add("数据根目录：" + AppEnv.Root);
            Lines.Add("");

            try
            {
                Check("目录结构", Directory.Exists(AppEnv.AliasDir) && Directory.Exists(AppEnv.DataDir), AppEnv.AliasDir);
                Check("快捷方式目录可写", AppEnv.IsWritableDir(AppEnv.AliasDir), AppEnv.AliasDir);

                Check("自动命名-空格", AliasRules.Normalize("Google Chrome") == "Google-Chrome", AliasRules.Normalize("Google Chrome"));
                Check("自动命名-连续空格", AliasRules.Normalize("a   b") == "a-b", AliasRules.Normalize("a   b"));
                Check("自动命名-非法字符", AliasRules.Normalize("???") == "app", AliasRules.Normalize("???"));
                Check("自动命名-中文保留", AliasRules.Normalize("微信") == "微信", AliasRules.Normalize("微信"));
                Check("自动命名-保留名", AliasRules.Normalize("con") == "run-con", AliasRules.Normalize("con"));

                string err;
                Check("校验-正常", AliasRules.Validate("chrome", out err), "chrome");
                Check("校验-空格", !AliasRules.Validate("a b", out err), "a b");
                Check("校验-保留名", !AliasRules.Validate("con", out err), "con");
                Check("校验-数字开头可", AliasRules.Validate("1password", out err), "1password");
                Check("校验-开头短横线", !AliasRules.Validate("-x", out err), "-x");
                Check("校验-超长", !AliasRules.Validate(new string('a', 41), out err), "41 字符");

                List<AliasEntry> entries = new List<AliasEntry>();
                string unique1 = AliasRules.MakeUnique("__selftest_name", entries, false);
                AliasEntry tempEntry = new AliasEntry();
                tempEntry.Name = unique1;
                tempEntry.Target = Application.ExecutablePath;
                entries.Add(tempEntry);
                string unique2 = AliasRules.MakeUnique("__selftest_name", entries, false);
                Check("重名规避", unique1 != unique2 && unique2 == unique1 + "-2", unique1 + " / " + unique2);
                entries.Clear();

                Check("PATH-添加", PathManager.AddEntry(@"C:\A;C:\B;", @"D:\QR\aliases") == @"C:\A;C:\B;D:\QR\aliases",
                    PathManager.AddEntry(@"C:\A;C:\B;", @"D:\QR\aliases"));
                Check("PATH-包含（大小写/尾斜杠）", PathManager.ContainsEntry(@"c:\a;D:\QR\aliases\", @"D:\QR\aliases"), "case/trailing");
                Check("PATH-包含（带引号）", PathManager.ContainsEntry("\"D:\\QR\\aliases\";C:\\B", @"D:\QR\aliases"), "quoted");
                Check("PATH-去重", PathManager.AddEntry(@"C:\A;D:\QR\aliases", @"D:\QR\aliases") == @"C:\A;D:\QR\aliases",
                    PathManager.AddEntry(@"C:\A;D:\QR\aliases", @"D:\QR\aliases"));
                Check("PATH-移除", PathManager.RemoveEntry(@"C:\A;D:\QR\aliases;C:\B", @"D:\QR\aliases") == @"C:\A;C:\B",
                    PathManager.RemoveEntry(@"C:\A;D:\QR\aliases;C:\B", @"D:\QR\aliases"));
                Check("PATH-移除不存在项", PathManager.RemoveEntry(@"C:\A;C:\B", @"D:\QR\aliases") == @"C:\A;C:\B", "no-op");

                Check("类型识别-exe", KindOf(Application.ExecutablePath) == "程序", KindOf(Application.ExecutablePath));
                Check("类型识别-目录", KindOf(AppEnv.AliasDir) == "文件夹", KindOf(AppEnv.AliasDir));

                AliasEntry writeEntry = new AliasEntry();
                writeEntry.Name = "__selftest_alias";
                writeEntry.Target = Application.ExecutablePath;
                AliasFile.Delete(writeEntry);
                AliasFile.Write(writeEntry);
                string aliasPath = AliasFile.PathFor(writeEntry);
                bool wrote = File.Exists(aliasPath);
                string resolvedWrite = wrote ? Shortcuts.ResolveIfLink(aliasPath) : "";
                Check("快捷方式-写入文件", wrote && aliasPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase), aliasPath);
                Check("快捷方式-指向正确", string.Equals(resolvedWrite, Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase), resolvedWrite);
                AliasFile.Delete(writeEntry);
                Check("快捷方式-删除文件", !File.Exists(aliasPath), aliasPath);

                // 拖入的 .lnk 应被原样复制为别名快捷方式
                string sourceLink = Path.Combine(AppEnv.DataDir, "__selftest_source.lnk");
                AliasEntry copyEntry = new AliasEntry();
                copyEntry.Name = "__selftest_copy";
                copyEntry.Target = sourceLink;
                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    object shell = Activator.CreateInstance(shellType);
                    object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { sourceLink });
                    shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { @"C:\Windows\System32\notepad.exe" });
                    shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                    Marshal.FinalReleaseComObject(shortcut);
                    Marshal.FinalReleaseComObject(shell);
                    AliasFile.Write(copyEntry);
                    string copyPath = AliasFile.PathFor(copyEntry);
                    Check("快捷方式-复制拖入的 lnk", File.Exists(copyPath) &&
                        string.Equals(Shortcuts.ResolveIfLink(copyPath), @"C:\Windows\System32\notepad.exe", StringComparison.OrdinalIgnoreCase), copyPath);
                    AliasFile.Delete(copyEntry);
                }
                catch (Exception ex)
                {
                    Check("快捷方式-复制拖入的 lnk", false, ex.Message);
                }
                finally
                {
                    try { File.Delete(sourceLink); }
                    catch { }
                }

                AppConfig cfg = new AppConfig();
                AliasEntry cfgEntry = new AliasEntry();
                cfgEntry.Name = "测试alias";
                cfgEntry.Target = @"C:\Apps\测试.exe";
                cfgEntry.Note = "备注";
                cfgEntry.CreatedAt = "2026-01-01 00:00:00";
                cfg.Aliases.Add(cfgEntry);
                string json = ConfigStore.Serialize(cfg);
                AppConfig back = ConfigStore.Deserialize(json);
                Check("配置-序列化往返", back.Aliases.Count == 1 && back.Aliases[0].Name == "测试alias" &&
                    back.Aliases[0].Target == @"C:\Apps\测试.exe" && back.Aliases[0].Note == "备注", json);

                string settingsTestDir = Path.Combine(AppEnv.DataDir, "__selftest_settings");
                try
                {
                    Directory.CreateDirectory(settingsTestDir);
                    AppSettings settings = new AppSettings();
                    settings.AliasDir = Path.Combine(settingsTestDir, "links");
                    SettingsStore.Save(settings, settingsTestDir);
                    string settingsWarning;
                    AppSettings loadedSettings = SettingsStore.Load(settingsTestDir, out settingsWarning);
                    Check("设置-保存读取", loadedSettings != null &&
                        string.Equals(loadedSettings.AliasDir, settings.AliasDir, StringComparison.OrdinalIgnoreCase),
                        string.IsNullOrEmpty(settingsWarning) ? loadedSettings.AliasDir : settingsWarning);
                }
                catch (Exception ex)
                {
                    Check("设置-保存读取", false, ex.Message);
                }
                finally
                {
                    try { Directory.Delete(settingsTestDir, true); }
                    catch { }
                }

                string shortcutPath = Path.Combine(AppEnv.DataDir, "__selftest.lnk");
                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        object shell = Activator.CreateInstance(shellType);
                        object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                        shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { @"C:\Windows\System32\notepad.exe" });
                        shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                        Marshal.FinalReleaseComObject(shortcut);
                        Marshal.FinalReleaseComObject(shell);
                    }
                    string resolved = Shortcuts.ResolveIfLink(shortcutPath);
                    Check("快捷方式-解析", string.Equals(resolved, @"C:\Windows\System32\notepad.exe", StringComparison.OrdinalIgnoreCase), resolved);
                }
                catch (Exception ex)
                {
                    Check("快捷方式-解析", false, ex.Message);
                }
                finally
                {
                    try { File.Delete(shortcutPath); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Check("自检过程未抛异常", false, ex.GetType().Name + ": " + ex.Message);
            }

            Lines.Add("");
            Lines.Add("结果：" + _pass + " 通过 / " + _fail + " 失败");
            Lines.Add("注意：自检不会写入用户 PATH 注册表，避免改动真实系统环境。");
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(reportPath, string.Join("\r\n", Lines.ToArray()) + "\r\n", new UTF8Encoding(false));
            }
            catch
            {
            }
            return _fail == 0 ? 0 : 1;
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++;
            else _fail++;
            Lines.Add((ok ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : "  => " + detail));
        }

        private static string KindOf(string path)
        {
            if (Directory.Exists(path)) return "文件夹";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".exe" || ext == ".com") return "程序";
            if (ext == ".lnk") return "快捷方式";
            return "文件";
        }
    }

    // =================================================================
    // 程序入口
    // =================================================================
    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { SetProcessDPIAware(); }
            catch { }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e)
            {
                HandleFatal(e.Exception, "界面线程异常");
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
            {
                HandleFatal(e.ExceptionObject as Exception, "未处理异常");
            };

            try
            {
                if (HasArg(args, "--selftest"))
                {
                    AppEnv.Initialize();
                    return SelfTest.Run(GetArgValue(args, "--selftest", Path.Combine(AppEnv.Root, "selftest.txt")));
                }

                if (HasArg(args, "--shot"))
                {
                    AppEnv.Initialize();
                    return CaptureScreenshot(GetArgValue(args, "--shot", Path.Combine(AppEnv.Root, "shot.png")));
                }

                AppEnv.Initialize();

                if (HasArg(args, "--repair"))
                    return HeadlessRepair();
                if (HasArg(args, "--remove-path"))
                    return HeadlessRemovePath();

                bool createdNew;
                using (Mutex mutex = new Mutex(true, "Local\\QuickRun_SingleInstance_v1", out createdNew))
                {
                    if (!createdNew)
                    {
                        MessageBox.Show("QuickRun 已经在运行，请在任务栏里找到它的窗口。", "QuickRun",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 0;
                    }
                    Application.Run(new MainForm(false));
                }
                return 0;
            }
            catch (Exception ex)
            {
                HandleFatal(ex, "启动失败");
                return 2;
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetArgValue(string[] args, string name, string fallback)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            return args[i + 1];
                        return fallback;
                    }
                }
            }
            return fallback;
        }

        private static int HeadlessRepair()
        {
            string warning;
            AppConfig config = ConfigStore.Load(out warning);
            int ok = 0;
            int failed = 0;
            foreach (AliasEntry entry in config.Aliases)
            {
                try
                {
                    AliasFile.Write(entry);
                    AliasFile.DeleteLegacyByName(entry.Name);
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error("命令行修复别名失败：" + entry.Name, ex);
                }
            }
            PathCheckResult check = PathManager.EnsureAdded(true);
            Log.Info("命令行修复完成：别名 " + ok + " 成功 / " + failed + " 失败；PATH=" + check.Message +
                (string.IsNullOrEmpty(check.Error) ? "" : "；错误=" + check.Error));
            return check.Contains && failed == 0 ? 0 : 1;
        }

        private static int HeadlessRemovePath()
        {
            PathCheckResult check = PathManager.Remove(true);
            Log.Info("命令行移除 PATH：" + check.Message +
                (string.IsNullOrEmpty(check.Error) ? "" : "；错误=" + check.Error));
            return check.Contains ? 1 : 0;
        }

        private static int CaptureScreenshot(string path)
        {
            try
            {
                using (MainForm form = new MainForm(true))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(80, 80);
                    form.Show();
                    Application.DoEvents();
                    Thread.Sleep(250);
                    Application.DoEvents();
                    Bitmap full = new Bitmap(form.Width, form.Height);
                    try
                    {
                        form.DrawToBitmap(full, new Rectangle(0, 0, form.Width, form.Height));
                        Point clientOrigin = form.PointToScreen(Point.Empty);
                        int offsetX = clientOrigin.X - form.Location.X;
                        int offsetY = clientOrigin.Y - form.Location.Y;
                        Rectangle crop = new Rectangle(offsetX, offsetY, form.ClientSize.Width, form.ClientSize.Height);
                        if (crop.X < 0 || crop.Y < 0 || crop.Right > full.Width || crop.Bottom > full.Height)
                            crop = new Rectangle(0, 0, full.Width, full.Height);
                        using (Bitmap client = full.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        {
                            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            client.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    finally
                    {
                        full.Dispose();
                    }
                    form.Close();
                    Log.Info("界面截图已保存：" + path);
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("界面截图失败", ex);
                return 1;
            }
        }

        private static void HandleFatal(Exception ex, string title)
        {
            try
            {
                Log.Error(title, ex);
            }
            catch
            {
            }
            try
            {
                string message = ex == null ? "未知错误。" : ex.GetType().Name + ": " + ex.Message;
                string log = string.IsNullOrEmpty(AppEnv.LogFile) ? "" : "\r\n\r\n日志：" + AppEnv.LogFile;
                MessageBox.Show(message + log, "QuickRun - " + title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }
    }
}
