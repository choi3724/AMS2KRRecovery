using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ams2KoreanBeta
{
    internal static class PackageManifest {
        internal const string AppId="1066890";
        internal static string SafeJoin(string root,string relative) {
            if(String.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)) throw new IOException("Invalid relative path");
            string r=Path.GetFullPath(root).TrimEnd('\\')+"\\";
            string p=Path.GetFullPath(Path.Combine(r,relative.Replace('/', '\\')));
            if(!p.StartsWith(r,StringComparison.OrdinalIgnoreCase)) throw new IOException("Path boundary failure");
            return p;
        }
    }
    internal static class ShortcutManager {
        [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint e,uint f,IntPtr a,IntPtr b);
        internal static void RefreshShell(){SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);}
    }
    internal static class FileOps
    {
        public static string Sha256(string path)
        {
            using (SHA256 h = SHA256.Create())
            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "");
        }

        public static string Sha256Text(string text)
        {
            using (SHA256 h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        public static void EnsureDirectoryFor(string file)
        {
            string d = Path.GetDirectoryName(file);
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
        }

        public static void CopyNewExact(string source, string target, string expected)
        {
            if (File.Exists(target)) throw new InvalidOperationException("대상이 이미 존재합니다: " + target);
            EnsureDirectoryFor(target);
            string tmp = TemporarySibling(target);
            try
            {
                File.Copy(source, tmp, false);
                File.SetAttributes(tmp, File.GetAttributes(tmp) & ~FileAttributes.ReadOnly);
                if (Sha256(tmp) != expected) throw new InvalidOperationException("임시 복사 hash 불일치");
                File.Move(tmp, target);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        private static string TemporarySibling(string target)
        {
            return Path.Combine(Path.GetDirectoryName(target), Guid.NewGuid().ToString("N") + ".tmp");
        }

    }
    internal sealed class GameInfo
    {
        public string GameDir;
        public string AppManifest;
        public string BuildId;
        public string Branch;
    }

    internal static class SteamLocator
    {
        public static List<GameInfo> Detect()
        {
            HashSet<string> steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddRegistry(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistry(steamRoots, Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");
            List<string> libraries = new List<string>();
            foreach (string root in steamRoots)
            {
                libraries.Add(root);
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                    libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            List<GameInfo> found = new List<GameInfo>();
            foreach (string lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string acf = Path.Combine(lib, "steamapps", "appmanifest_" + PackageManifest.AppId + ".acf");
                if (!File.Exists(acf)) continue;
                try { found.Add(FromAppManifest(acf, null)); } catch { }
            }
            return found.GroupBy(x => x.GameDir, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        }

        public static GameInfo FromGameDirectory(string gameDir)
        {
            string game = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar);
            DirectoryInfo common = Directory.GetParent(game);
            if (common == null || !common.Name.Equals("common", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Steam common 경로가 아닙니다.");
            DirectoryInfo steamapps = common.Parent;
            if (steamapps == null) throw new InvalidOperationException("steamapps 경로를 찾지 못했습니다.");
            string acf = Path.Combine(steamapps.FullName, "appmanifest_" + PackageManifest.AppId + ".acf");
            return FromAppManifest(acf, game);
        }

        private static GameInfo FromAppManifest(string acf, string expected)
        {
            if (!File.Exists(acf)) throw new InvalidOperationException("Steam appmanifest가 없습니다.");
            string text = File.ReadAllText(acf);
            string app = Value(text, "appid");
            string build = Value(text, "buildid");
            string install = Value(text, "installdir");
            if (app != PackageManifest.AppId) throw new InvalidOperationException("AppID 불일치");
            string steamapps = Directory.GetParent(acf).FullName;
            string game = Path.Combine(steamapps, "common", install);
            if (expected != null && !Path.GetFullPath(game).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("선택 경로와 appmanifest가 다릅니다.");
            if (!File.Exists(Path.Combine(game, "AMS2.exe")) || !File.Exists(Path.Combine(game, "AMS2AVX.exe"))) throw new InvalidOperationException("AMS2 실행 파일이 없습니다.");
            return new GameInfo { GameDir = Path.GetFullPath(game), AppManifest = Path.GetFullPath(acf), BuildId = build, Branch = "public" };
        }

        private static string Value(string text, string key)
        {
            Match m = Regex.Match(text, "\\\"" + Regex.Escape(key) + "\\\"\\s+\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase);
            if (!m.Success) throw new InvalidOperationException("appmanifest field missing: " + key);
            return m.Groups[1].Value;
        }

        private static void AddRegistry(HashSet<string> roots, RegistryKey hive, string subkey, string name)
        {
            try { using (RegistryKey k = hive.OpenSubKey(subkey)) { object v = k == null ? null : k.GetValue(name); if (v != null && Directory.Exists(v.ToString())) roots.Add(Path.GetFullPath(v.ToString())); } } catch { }
        }
    }

}
