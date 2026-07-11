using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;

class Program {
    [DllImport("user32.dll", CharSet=CharSet.Auto)]
    static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    static string exeDir;
    static string stateFile;

    [STAThread]
    static void Main() {
        exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        stateFile = Path.Combine(exeDir, "state");

        var state = LoadState();

        while (state.Queue.Count > 0) {
            string chosen = state.Queue[0];
            string fullPath = Path.Combine(exeDir, chosen);
            if (File.Exists(fullPath)) {
                state.Queue.RemoveAt(0);
                state.Shown.Add(chosen);
                SaveState(state);
                ApplyWallpaper(fullPath);
                return;
            }
            state.Queue.RemoveAt(0);
        }

        // Queue exhausted: full scan + rebuild
        {
            string assetsDir = ResolveAssetsDir();
            if (!Directory.Exists(assetsDir)) return;

            string[] exts = { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
            var allImages = new List<string>();
            foreach (var ext in exts)
                allImages.AddRange(Directory.GetFiles(assetsDir, ext, SearchOption.TopDirectoryOnly));
            if (allImages.Count == 0) return;

            for (int i = 0; i < allImages.Count; i++)
                allImages[i] = MakeRelative(allImages[i]);

            Shuffle(allImages);
            state.Queue = allImages;
            state.Shown = new List<string>();

            string c = state.Queue[0];
            state.Queue.RemoveAt(0);
            state.Shown.Add(c);
            SaveState(state);

            string fp = Path.Combine(exeDir, c);
            if (!File.Exists(fp)) return;
            ApplyWallpaper(fp);
        }
    }

    static void ApplyWallpaper(string fullPath) {
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true)) {
            key.SetValue("Wallpaper", fullPath);
            key.SetValue("WallpaperStyle", "10");
            key.SetValue("TileWallpaper", "0");
        }
        SystemParametersInfo(20, 0, fullPath, 3);
    }

    static string ResolveAssetsDir() {
        string path = Path.Combine(exeDir, "config.ini");
        if (!File.Exists(path)) return Path.Combine(exeDir, "assets");
        try {
            foreach (string raw in File.ReadAllLines(path)) {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (string.Equals(key, "AssetsDir", StringComparison.OrdinalIgnoreCase))
                    return Path.IsPathRooted(val) ? val : Path.Combine(exeDir, val);
            }
        } catch { }
        return Path.Combine(exeDir, "assets");
    }

    static void Shuffle(List<string> list) {
        var rng = new Random(Guid.NewGuid().GetHashCode());
        for (int i = list.Count - 1; i > 0; i--) {
            int j = rng.Next(i + 1);
            var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    static string MakeRelative(string full) {
        return full.StartsWith(exeDir, StringComparison.OrdinalIgnoreCase)
            ? full.Substring(exeDir.Length).TrimStart('\\', '/')
            : full;
    }

    class State {
        public List<string> Queue = new List<string>();
        public List<string> Shown = new List<string>();
    }

    static State LoadState() {
        var s = new State();
        if (!File.Exists(stateFile)) return s;
        try {
            string section = "";
            foreach (string raw in File.ReadAllLines(stateFile, Encoding.UTF8)) {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line == "queue:") { section = "queue"; continue; }
                if (line == "shown:") { section = "shown"; continue; }
                if (section == "queue") s.Queue.Add(line);
                else if (section == "shown") s.Shown.Add(line);
            }
        } catch { }
        return s;
    }

    static void SaveState(State s) {
        var sb = new StringBuilder();
        sb.AppendLine("queue:");
        foreach (var f in s.Queue) sb.AppendLine(f);
        sb.AppendLine();
        sb.AppendLine("shown:");
        foreach (var f in s.Shown) sb.AppendLine(f);
        File.WriteAllText(stateFile, sb.ToString(), Encoding.UTF8);
    }
}
