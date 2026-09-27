using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Ams2KoreanBeta
{
    internal sealed class StockFile
    {
        internal string Relative, Action, BeforeSha, BackupName, ShortcutPath;
        internal FileAttributes BeforeAttributes;
    }

    internal static class StockRestore {
        internal static bool AllowedShortcut(string path) {
#if STOCK_RESTORE_TEST
            var folders=new[]{Environment.GetEnvironmentVariable("AMS2_STOCK_TEST_SHORTCUTS")};
#else
            var folders=new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft/Internet Explorer/Quick Launch/User Pinned/TaskBar")};
#endif
            return path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)&&folders.Where(p=>!String.IsNullOrEmpty(p)).Any(p=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(p).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase));
        }
        internal static bool IsPatchShortcut(string file,string root) {
            if(!File.Exists(file))return false;
            Type type=Type.GetTypeFromProgID("WScript.Shell"); if(type==null)return false;
            object shell=Activator.CreateInstance(type), shortcut=null;
            // WScript requires the .lnk suffix even for a backed-up shortcut.
            string temp=Path.Combine(Path.GetDirectoryName(file),Guid.NewGuid().ToString("N")+".lnk");
            try {
                File.Copy(file,temp);
                shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{temp});
                string target=shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null) as string;
                return new[]{"AMS2 Korean Launcher.exe","AMS2 Korean VR Launcher.exe"}.Any(n=>String.Equals(target,SafeTarget(root,n),StringComparison.OrdinalIgnoreCase));
            }finally{if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);if(Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);if(File.Exists(temp))File.Delete(temp);}
        }
        internal static void GuardContentManager(string root)
        {
            string path = SafeTarget(root, "Mods/state.json");
            if (!File.Exists(path)) return;
            try
            {
                var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                var data = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(path));
                object installValue;
                if (!data.TryGetValue("Install", out installValue) || installValue == null) return;
                var install = (Dictionary<string, object>)installValue;
                object modsValue;
                if (install.TryGetValue("Mods", out modsValue) && modsValue != null && ((Dictionary<string, object>)modsValue).Count > 0)
                    throw new InvalidOperationException("CM에서 모드를 먼저 비활성화해주세요. 모드와 백업을 보호하기 위해 아직 파일을 변경하지 않았습니다.");
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception error) { throw new InvalidOperationException("CM 기록을 확인할 수 없습니다. CM에서 모드를 비활성화한 뒤 다시 시도해주세요.", error); }
        }

        internal static string SafeTarget(string root, string relative)
        {
            if (relative.Contains(":") || relative.Split('/', '\\').Any(x => x == "." || x == ".." || x.EndsWith(".") || x.EndsWith(" ")))
                throw new InvalidOperationException("허용되지 않는 파일 경로: " + relative);
            string target = PackageManifest.SafeJoin(root, relative);
            for (string p = target; p != null; p = Path.GetDirectoryName(p))
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("연결된 폴더/파일은 복구 대상으로 사용할 수 없습니다: " + p);
            if (Directory.Exists(target)) throw new InvalidOperationException("파일 위치에 폴더가 있습니다: " + target);
            return target;
        }

        internal static string Target(string root, StockFile file)
        {
            if (file.ShortcutPath == null) return SafeTarget(root, file.Relative);
            string volume = Path.GetPathRoot(file.ShortcutPath);
            return SafeTarget(volume, file.ShortcutPath.Substring(volume.Length));
        }

        internal static void AddShortcuts(string root, List<StockFile> files, Action<string> log)
        {
            var folders = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft/Internet Explorer/Quick Launch/User Pinned/TaskBar")
            };
#if STOCK_RESTORE_TEST
            // Tests may inspect only their disposable shortcut folder, never the real desktop.
            folders = new[] { Environment.GetEnvironmentVariable("AMS2_STOCK_TEST_SHORTCUTS") };
#endif
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders.Where(p => !String.IsNullOrWhiteSpace(p) && Directory.Exists(p)))
                foreach (string path in Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly)) candidates.Add(Path.GetFullPath(path));
            foreach (StockFile record in files.Where(f => f.Relative.EndsWith("/shortcuts.tsv", StringComparison.OrdinalIgnoreCase)))
                foreach (string line in File.ReadAllLines(SafeTarget(root, record.Relative), Encoding.UTF8).Skip(1))
                {
                    string[] row = line.Split('\t');
                    try
                    {
                        if (row.Length == 3 && Path.IsPathRooted(row[1]) && row[1].EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(row[1]))
                            candidates.Add(Path.GetFullPath(row[1]));
                    }
                    catch (ArgumentException) { }
                    catch (NotSupportedException) { }
                    catch (PathTooLongException) { }
                }
            if (candidates.Count == 0) return;
            string normal = SafeTarget(root, "AMS2 Korean Launcher.exe"), vr = SafeTarget(root, "AMS2 Korean VR Launcher.exe");
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Windows 바로가기 확인 기능을 사용할 수 없습니다.");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                foreach (string path in candidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if(!AllowedShortcut(path))continue;
                    object shortcut = null;
                    try
                    {
                        shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                        string target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                        if (!String.Equals(target, normal, StringComparison.OrdinalIgnoreCase) && !String.Equals(target, vr, StringComparison.OrdinalIgnoreCase)) continue;
                        files.Add(new StockFile { Relative = "shortcut/" + files.Count + "/" + Path.GetFileName(path), Action = "remove", ShortcutPath = path });
                    }
                    catch (TargetInvocationException) { log("연결 대상을 읽지 못한 바로가기는 보존했습니다: " + path); }
                    catch (COMException) { log("연결 대상을 읽지 못한 바로가기는 보존했습니다: " + path); }
                    finally { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
                }
            }
            finally { if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        }

        internal static void GuardProcesses()
        {
            foreach (string name in new[] { "AMS2", "AMS2AVX", "AMS2 Korean Launcher", "AMS2 Korean VR Launcher", "AMS2CM", "AMS2 Content Manager" })
                foreach (Process process in Process.GetProcessesByName(name))
                    using (process) { throw new InvalidOperationException("게임과 한글 런처를 종료한 뒤 다시 실행해주세요: " + name); }
        }

    }
}
