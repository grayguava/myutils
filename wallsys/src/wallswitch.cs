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
    static string assetsDir;
    static string stateFile;

    [STAThread]
    static void Main() {
        exeDir    = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        assetsDir = Path.Combine(exeDir, "assets");
        stateFile = Path.Combine(exeDir, "state.json");

        if (!Directory.Exists(assetsDir)) return;

        // Scan assets
        string[] exts = { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
        var allImages = new List<string>();
        foreach (var ext in exts)
            allImages.AddRange(Directory.GetFiles(assetsDir, ext, SearchOption.TopDirectoryOnly));

        if (allImages.Count == 0) return;

        // Normalize to relative paths for portability
        for (int i = 0; i < allImages.Count; i++)
            allImages[i] = MakeRelative(allImages[i]);

        // Load state
        var state = LoadState();

        // Sync: detect added/removed images
        var known = new HashSet<string>(state.Queue);
        known.UnionWith(state.Shown);
        var current = new HashSet<string>(allImages);

        bool dirty = !known.SetEquals(current);

        if (dirty) {
            // Files changed — rebuild queue
            // Keep unshown items that still exist, in their current order
            var keepUnshown = new List<string>();
            foreach (var f in state.Queue)
                if (current.Contains(f)) keepUnshown.Add(f);

            // New files go into a shuffled pool appended after keepUnshown
            var brandNew = new List<string>();
            foreach (var f in allImages)
                if (!known.Contains(f)) brandNew.Add(f);
            Shuffle(brandNew);

            state.Queue = new List<string>(keepUnshown);
            state.Queue.AddRange(brandNew);
            state.Shown = new List<string>();

            // If keepUnshown + brandNew is empty somehow, full reshuffle
            if (state.Queue.Count == 0) {
                state.Queue = new List<string>(allImages);
                Shuffle(state.Queue);
            }
        }

        // If queue exhausted, reshuffle everything
        if (state.Queue.Count == 0) {
            state.Queue = new List<string>(allImages);
            Shuffle(state.Queue);
            state.Shown = new List<string>();
        }

        // Pop first
        string chosen = state.Queue[0];
        state.Queue.RemoveAt(0);
        state.Shown.Add(chosen);

        // Save state
        SaveState(state);

        // Resolve full path
        string fullPath = Path.Combine(exeDir, chosen);
        if (!File.Exists(fullPath)) return;

        // Write registry for persistence across reboots
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true)) {
            key.SetValue("Wallpaper", fullPath);
            key.SetValue("WallpaperStyle", "10");
            key.SetValue("TileWallpaper", "0");
        }

        // Apply live
        SystemParametersInfo(20, 0, fullPath, 3);
    }

    // --- Shuffle (Fisher-Yates, Guid-seeded) ---
    static void Shuffle(List<string> list) {
        var rng = new Random(Guid.NewGuid().GetHashCode());
        for (int i = list.Count - 1; i > 0; i--) {
            int j = rng.Next(i + 1);
            var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    // --- Relative path helpers ---
    static string MakeRelative(string full) {
        return full.StartsWith(exeDir, StringComparison.OrdinalIgnoreCase)
            ? full.Substring(exeDir.Length).TrimStart('\\', '/')
            : full;
    }

    // --- Minimal JSON state (no external deps) ---
    class State {
        public List<string> Queue = new List<string>();
        public List<string> Shown = new List<string>();
    }

    static State LoadState() {
        var s = new State();
        if (!File.Exists(stateFile)) return s;
        try {
            string json = File.ReadAllText(stateFile, Encoding.UTF8);
            s.Queue = ParseJsonArray(json, "queue");
            s.Shown = ParseJsonArray(json, "shown");
        } catch { }
        return s;
    }

    static void SaveState(State s) {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"queue\": [");
        for (int i = 0; i < s.Queue.Count; i++) {
            string comma = i < s.Queue.Count - 1 ? "," : "";
            sb.AppendLine("    \"" + Escape(s.Queue[i]) + "\"" + comma);
        }
        sb.AppendLine("  ],");
        sb.AppendLine("  \"shown\": [");
        for (int i = 0; i < s.Shown.Count; i++) {
            string comma = i < s.Shown.Count - 1 ? "," : "";
            sb.AppendLine("    \"" + Escape(s.Shown[i]) + "\"" + comma);
        }
        sb.AppendLine("  ]");
        sb.Append("}");
        File.WriteAllText(stateFile, sb.ToString(), Encoding.UTF8);
    }

    static string Escape(string s) {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static List<string> ParseJsonArray(string json, string key) {
        var result = new List<string>();
        string marker = "\"" + key + "\"";
        int idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return result;
        int start = json.IndexOf('[', idx);
        int end   = json.IndexOf(']', start);
        if (start < 0 || end < 0) return result;
        string inner = json.Substring(start + 1, end - start - 1);
        foreach (var part in inner.Split(',')) {
            string val = part.Trim().Trim('"');
            val = val.Replace("\\\\", "\\").Replace("\\\"", "\"");
            if (!string.IsNullOrWhiteSpace(val))
                result.Add(val);
        }
        return result;
    }
}