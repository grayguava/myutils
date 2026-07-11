using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class Program {
    [DllImport("user32.dll", CharSet=CharSet.Auto)]
    static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    [STAThread]
    static void Main() {
        string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        string assetsDir = Path.Combine(exeDir, "assets\\nature");

        if (!Directory.Exists(assetsDir)) return;

        string[] exts = { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
        var images = new System.Collections.Generic.List<string>();
        foreach (var ext in exts)
            images.AddRange(Directory.GetFiles(assetsDir, ext, SearchOption.AllDirectories));

        if (images.Count == 0) return;

        string chosen = images[new Random().Next(images.Count)];

        // Write to registry so it survives reboots
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true)) {
            key.SetValue("Wallpaper", chosen);
            key.SetValue("WallpaperStyle", "10");  // Fill
            key.SetValue("TileWallpaper", "0");
        }

        // Apply live
        SystemParametersInfo(20, 0, chosen, 3);
    }
}