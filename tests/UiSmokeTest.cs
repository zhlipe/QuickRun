// UI 冒烟测试：在进程内加载 QuickRun.exe，模拟“拖入文件 / 编辑别名”的核心路径。
// 仅用于构建验证，不随工具发布。运行：
//   UiSmokeTest.exe <QuickRun.exe> <测试用 QUICKRUN_HOME>
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class UiSmokeTest
{
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: UiSmokeTest.exe <QuickRun.exe> <testRoot>");
            return 2;
        }
        string assemblyPath = args[0];
        string testRoot = args[1];
        Environment.SetEnvironmentVariable("QUICKRUN_HOME", testRoot);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Assembly assembly = Assembly.UnsafeLoadFrom(assemblyPath);
            Type appEnvType = assembly.GetType("QuickRun.AppEnv");
            appEnvType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            string aliasDir = (string)appEnvType.GetField("AliasDir", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            string configFile = (string)appEnvType.GetField("ConfigFile", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Console.WriteLine("ROOT=" + testRoot);
            Console.WriteLine("ALIAS_DIR=" + aliasDir);

            Type formType = assembly.GetType("QuickRun.MainForm");
            using (Form form = (Form)Activator.CreateInstance(formType, new object[] { false }))
            {
                MethodInfo addFiles = formType.GetMethod("AddFiles", BindingFlags.NonPublic | BindingFlags.Instance);
                string[] targets = new string[]
                {
                    @"C:\Windows\System32\notepad.exe",
                    @"C:\Windows\System32\mspaint.exe"
                };
                addFiles.Invoke(form, new object[] { targets });

                Check("配置文件已保存", File.Exists(configFile), configFile);

                object config = formType.GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                IList aliases = (IList)config.GetType().GetProperty("Aliases").GetValue(config, null);
                Check("配置中有 2 条记录", aliases.Count == 2, "count=" + aliases.Count);
                if (aliases.Count < 2)
                {
                    form.Close();
                    return _failures == 0 ? 0 : 1;
                }

                foreach (object entry in aliases)
                {
                    string entryName = (string)entry.GetType().GetProperty("Name").GetValue(entry, null);
                    string entryTarget = (string)entry.GetType().GetProperty("Target").GetValue(entry, null);
                    string lnkPath = Path.Combine(aliasDir, entryName + ".lnk");
                    Check("快捷方式已生成：" + entryName, File.Exists(lnkPath), lnkPath);
                    Check("快捷方式指向正确：" + entryName, string.Equals(ResolveShortcut(lnkPath), entryTarget, StringComparison.OrdinalIgnoreCase),
                        ResolveShortcut(lnkPath));
                    Check("目标保留正确：" + entryName, string.Equals(entryTarget, @"C:\Windows\System32\" + (entryName.StartsWith("notepad") ? "notepad.exe" : "mspaint.exe"), StringComparison.OrdinalIgnoreCase),
                        entryTarget);
                }

                // 3) 模拟拖放：DropDragDrop 应把文件加入配置并生成别名
                DataObject data = new DataObject(DataFormats.FileDrop, new string[] { @"C:\Windows\System32\calc.exe" });
                DragEventArgs enterArgs = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
                formType.GetMethod("DropDragEnter", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { null, enterArgs });
                Check("拖放 Enter 接受文件", enterArgs.Effect == DragDropEffects.Copy, "effect=" + enterArgs.Effect);
                DragEventArgs dropArgs = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
                formType.GetMethod("DropDragDrop", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { null, dropArgs });
                object configAfterDrop = formType.GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                IList aliasesAfterDrop = (IList)configAfterDrop.GetType().GetProperty("Aliases").GetValue(configAfterDrop, null);
                Check("拖放后配置增加 1 条", aliasesAfterDrop.Count == 3, "count=" + aliasesAfterDrop.Count);
                bool calcAliasFound = false;
                foreach (object entry in aliasesAfterDrop)
                {
                    string entryTarget = (string)entry.GetType().GetProperty("Target").GetValue(entry, null);
                    string entryName = (string)entry.GetType().GetProperty("Name").GetValue(entry, null);
                    if (string.Equals(entryTarget, @"C:\Windows\System32\calc.exe", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(Path.Combine(aliasDir, entryName + ".lnk")))
                        calcAliasFound = true;
                }
                Check("拖放生成了 calc 快捷方式", calcAliasFound, "calc alias");

                Type dialogType = assembly.GetType("QuickRun.AliasEditorDialog");
                object entry0 = aliases[0];
                string name0 = (string)entry0.GetType().GetProperty("Name").GetValue(entry0, null);

                // 1) 不改名保存：不应被自己的重名检查挡住
                using (Form dialog = (Form)Activator.CreateInstance(dialogType, new object[] { entry0, config, false }))
                {
                    TextBox nameBox = (TextBox)dialogType.GetField("_nameBox", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dialog);
                    nameBox.Text = name0;
                    dialogType.GetMethod("SaveAndClose", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dialog, null);
                    object result = dialogType.GetProperty("Result").GetValue(dialog, null);
                    string resultName = (string)result.GetType().GetProperty("Name").GetValue(result, null);
                    Label errorLabel = (Label)dialogType.GetField("_errorLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dialog);
                    Check("编辑不改名可保存", string.Equals(resultName, name0, StringComparison.Ordinal) && string.IsNullOrEmpty(errorLabel.Text),
                        "result=" + resultName + " error=" + errorLabel.Text);
                }

                // 2) 改成另一条记录的名字：应被重名检查挡住
                object entry1 = aliases[1];
                using (Form dialog = (Form)Activator.CreateInstance(dialogType, new object[] { entry1, config, false }))
                {
                    TextBox nameBox = (TextBox)dialogType.GetField("_nameBox", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dialog);
                    nameBox.Text = name0;
                    dialogType.GetMethod("SaveAndClose", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dialog, null);
                    Label errorLabel = (Label)dialogType.GetField("_errorLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dialog);
                    Check("编辑改成重名会被拦截", !string.IsNullOrEmpty(errorLabel.Text), errorLabel.Text);
                }

                form.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL " + ex.GetType().Name + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            _failures++;
        }

        Console.WriteLine("RESULT failures=" + _failures);
        return _failures == 0 ? 0 : 1;
    }

    private static string ResolveShortcut(string path)
    {
        try
        {
            if (!File.Exists(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return "";
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            string target = (string)shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            return target;
        }
        catch
        {
            return "";
        }
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (!ok) _failures++;
        Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : " ; " + detail));
    }
}
