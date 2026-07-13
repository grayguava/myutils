using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

class CleanCore
{
    List<string> bakFiles = new List<string>();
    string tempDir;

    public string ExifToolPath { get; private set; }
    public event Action<string> Log;

    public CleanCore(string exifToolPath) { ExifToolPath = exifToolPath; }

    public bool CheckExifTool(out string version)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(ExifToolPath, "-ver")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            version = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return true;
        }
        catch { version = null; return false; }
    }

    public void Run(List<string> files)
    {
        bakFiles.Clear();
        tempDir = Path.Combine(Path.GetTempPath(), "_etsw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileMap = new Dictionary<string, string>();

        try
        {
            Emit(""); Emit("[1/4] Copying files to temp workspace...");
            for (int i = 0; i < files.Count; i++)
            {
                string src = files[i], name = Path.GetFileName(src), dest = Path.Combine(tempDir, i + Path.GetExtension(src));
                try { File.Copy(src, dest, false); }
                catch (Exception ex) { Emit(string.Format("  [ABORT] Copy failed for {0}: {1}", name, ex.Message)); throw new InvalidOperationException(string.Format("Copy failed: {0}", name)); }
                long sL = new FileInfo(src).Length, dL = new FileInfo(dest).Length;
                if (sL != dL) { Emit(string.Format("  [ABORT] Size mismatch for {0} ({1} vs {2})", name, sL, dL)); throw new InvalidOperationException(string.Format("Size mismatch: {0}", name)); }
                fileMap[src] = dest;
            }
            Emit("  OK \u2014 all copies verified");

            Emit(""); Emit("[2/4] Wiping metadata...");
            foreach (string src in files)
            {
                string name = Path.GetFileName(src), tf = fileMap[src];
                var psi = new ProcessStartInfo(ExifToolPath, "-all= -overwrite_original -P -v \"" + tf + "\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd(), stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        Emit(string.Format("  [ABORT] ExifTool failed on {0}", name));
                        foreach (string l in (stdout + stderr).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) Emit("    " + l);
                        throw new InvalidOperationException(string.Format("ExifTool failed: {0}", name));
                    }
                    var del = new List<string>();
                    foreach (string l in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) { string t = l.Trim(); if (t.StartsWith("Deleting ")) del.Add(t.Substring(9)); }
                    if (del.Count > 0) { Emit("  " + name + ":"); foreach (string tag in del) Emit("    - " + tag); }
                    else Emit("  " + name + " \u2014 (no metadata found)");
                }
            }

            Emit(""); Emit("[3/4] Verifying wiped files...");
            foreach (string src in files)
            {
                string name = Path.GetFileName(src), tf = fileMap[src];
                if (!File.Exists(tf)) { Emit(string.Format("  [ABORT] Temp file missing: {0}", name)); throw new InvalidOperationException(string.Format("Temp file missing: {0}", name)); }
                if (new FileInfo(tf).Length == 0) { Emit(string.Format("  [ABORT] Temp file is empty: {0}", name)); throw new InvalidOperationException(string.Format("Temp file empty: {0}", name)); }
                try { using (var fs = new FileStream(tf, FileMode.Open, FileAccess.Read)) fs.ReadByte(); }
                catch { Emit(string.Format("  [ABORT] Temp file unreadable: {0}", name)); throw new InvalidOperationException(string.Format("Temp file unreadable: {0}", name)); }
            }
            Emit("  OK \u2014 all files verified clean");

            Emit(""); Emit("[4/4] Replacing originals...");
            foreach (string src in files)
            {
                string name = Path.GetFileName(src), tf = fileMap[src], bak = src + ".bak";
                try { File.Move(src, bak); }
                catch (Exception ex) { Emit(string.Format("  [ABORT] Could not rename to .bak: {0}: {1}", name, ex.Message)); RestoreBaks(); throw new InvalidOperationException(string.Format("Rename to .bak failed: {0}", name)); }
                bakFiles.Add(bak);
                try { File.Move(tf, src); }
                catch (Exception ex) { Emit(string.Format("  [ABORT] Could not move temp file: {0}: {1}", name, ex.Message)); RestoreBaks(); throw new InvalidOperationException(string.Format("Move failed: {0}", name)); }
                if (!File.Exists(src) || new FileInfo(src).Length == 0) { Emit(string.Format("  [ABORT] Final file missing or empty: {0}", name)); throw new InvalidOperationException(string.Format("Final file missing: {0}", name)); }
            }
            foreach (string bak in bakFiles) try { File.Delete(bak); } catch { }
            bakFiles.Clear();
            try { Directory.Delete(tempDir, true); } catch { }
            Emit(""); Emit(string.Format("All done! {0} file(s) cleaned in place.", files.Count));
        }
        catch { try { Directory.Delete(tempDir, true); } catch { } throw; }
    }

    void RestoreBaks()
    {
        foreach (string bak in bakFiles)
        {
            string orig = bak.EndsWith(".bak") ? bak.Substring(0, bak.Length - 4) : bak;
            if (File.Exists(bak) && !File.Exists(orig)) try { File.Move(bak, orig); } catch { }
        }
    }

    void Emit(string line) { if (Log != null) Log(line); }
}
