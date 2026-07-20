using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

class Program
{
    static string BaseDir()
    {
        return Path.GetDirectoryName(typeof(Program).Assembly.Location);
    }

    static int Main(string[] args)
    {
        string targetDir = ".";
        bool dryRun = false;
        bool rollback = false;

        foreach (string a in args)
        {
            if (a == "--dry-run" || a == "-n") dryRun = true;
            else if (a == "--rollback" || a == "-r") rollback = true;
            else if (!a.StartsWith("-")) targetDir = a;
        }

        if (rollback)
            return Rollback(targetDir, dryRun);

        targetDir = Path.GetFullPath(targetDir);
        if (!Directory.Exists(targetDir))
        {
            Console.Error.WriteLine("Directory not found: " + targetDir);
            return 1;
        }

        var ignoreSet = LoadIgnore();
        string[] allFiles = Directory.GetFiles(targetDir);
        var files = new List<string>();
        foreach (string f in allFiles)
        {
            string name = Path.GetFileName(f);
            if (!ignoreSet.Contains(name))
                files.Add(f);
        }

        if (files.Count == 0)
        {
            Console.WriteLine("No files found.");
            return 0;
        }

        Array.Sort(files.ToArray(), StringComparer.OrdinalIgnoreCase);

        int digits = files.Count.ToString().Length;
        if (digits < 2) digits = 2;

        var temps = new List<string>();
        var finalNames = new List<string>();
        var originals = new List<string>();

        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                string ext = Path.GetExtension(files[i]);
                string finalName = (i + 1).ToString("D" + digits) + ext;

                if (Path.GetFileName(files[i]) == finalName)
                    continue;

                originals.Add(files[i]);
                finalNames.Add(finalName);

                string tempName = Guid.NewGuid().ToString("N") + ".tmp";
                string tempPath = Path.Combine(targetDir, tempName);
                temps.Add(tempPath);
            }

            for (int i = 0; i < originals.Count; i++)
            {
                if (!dryRun)
                    File.Move(originals[i], temps[i]);
            }

            for (int i = 0; i < temps.Count; i++)
            {
                string finalPath = Path.Combine(targetDir, finalNames[i]);

                if (!dryRun)
                    File.Move(temps[i], finalPath);

                Console.WriteLine("  " + (dryRun ? "would rename" : "renamed") + "  "
                    + Path.GetFileName(originals[i]) + "  ->  " + finalNames[i]);
            }

            if (dryRun)
            {
                Console.WriteLine();
                Console.WriteLine("  Dry run.  " + originals.Count + " files would be renamed.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("  Done.  " + originals.Count + " files renamed.");
                if (originals.Count > 0)
                    WriteLog(targetDir, originals, finalNames);
            }
        }
        catch (Exception ex)
        {
            foreach (string t in temps)
            {
                if (File.Exists(t))
                {
                    try { File.Delete(t); } catch { }
                }
            }
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }

        return 0;
    }

    static HashSet<string> LoadIgnore()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(BaseDir(), "..", "conf", ".catignore");
        if (!File.Exists(path)) return set;
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                continue;
            set.Add(line);
        }
        return set;
    }

    static string LogsDir()
    {
        string dir = Path.Combine(BaseDir(), "..", "logs", "reindex");
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void WriteLog(string dir, List<string> originals, List<string> finalNames)
    {
        string logPath = Path.Combine(LogsDir(),
            DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss") + ".txt");
        var sb = new StringBuilder();
        sb.AppendLine(dir);
        for (int i = 0; i < originals.Count; i++)
        {
            sb.AppendLine(Path.GetFileName(originals[i]) + "\t" + finalNames[i]);
        }
        File.WriteAllText(logPath, sb.ToString());

        var logs = new List<string>(Directory.GetFiles(LogsDir(), "*.txt"));
        logs.Sort();
        while (logs.Count > 25)
        {
            File.Delete(logs[0]);
            logs.RemoveAt(0);
        }
    }

    static int Rollback(string targetDir, bool dryRun)
    {
        string logsDir = LogsDir();
        var logs = new List<string>(Directory.GetFiles(logsDir, "*.txt"));
        if (logs.Count == 0)
        {
            Console.Error.WriteLine("No reindex logs found to roll back.");
            return 1;
        }
        logs.Sort();
        string logPath = logs[logs.Count - 1];

        string[] lines = File.ReadAllLines(logPath);
        if (lines.Length < 2)
        {
            Console.Error.WriteLine("Log file is empty or corrupt: " + logPath);
            return 1;
        }

        string dir = lines[0].Trim();
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine("Original directory no longer exists: " + dir);
            return 1;
        }

        var originals = new List<string>();
        var finals = new List<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            string[] parts = line.Split('\t');
            if (parts.Length != 2) continue;
            originals.Add(parts[0]);
            finals.Add(parts[1]);
        }

        if (originals.Count == 0)
        {
            Console.WriteLine("Nothing to roll back.");
            return 0;
        }

        var temps = new List<string>();
        try
        {
            for (int i = 0; i < finals.Count; i++)
            {
                string tempName = Guid.NewGuid().ToString("N") + ".tmp";
                string tempPath = Path.Combine(dir, tempName);
                temps.Add(tempPath);
            }

            for (int i = 0; i < finals.Count; i++)
            {
                string finalPath = Path.Combine(dir, finals[i]);
                if (!File.Exists(finalPath))
                {
                    Console.WriteLine("  skipped (not found)  " + finals[i]);
                    continue;
                }
                if (!dryRun)
                    File.Move(finalPath, temps[i]);
            }

            for (int i = 0; i < finals.Count; i++)
            {
                string finalPath = Path.Combine(dir, finals[i]);
                if (!File.Exists(temps[i])) continue;

                string origPath = Path.Combine(dir, originals[i]);
                if (!dryRun)
                    File.Move(temps[i], origPath);

                Console.WriteLine("  " + (dryRun ? "would revert" : "reverted") + "  "
                    + finals[i] + "  ->  " + originals[i]);
            }

            if (dryRun)
            {
                Console.WriteLine();
                Console.WriteLine("  Dry run.  " + finals.Count + " files would be reverted.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("  Rolled back.  " + finals.Count + " files reverted.");
                File.Delete(logPath);
            }
        }
        catch (Exception ex)
        {
            foreach (string t in temps)
            {
                if (File.Exists(t))
                {
                    try { File.Delete(t); } catch { }
                }
            }
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }

        return 0;
    }
}
