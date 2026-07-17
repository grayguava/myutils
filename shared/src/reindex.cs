using System;
using System.Collections.Generic;
using System.IO;

class Program
{
    static int Main(string[] args)
    {
        string targetDir = ".";
        bool dryRun = false;

        foreach (string a in args)
        {
            if (a == "--dry-run" || a == "-n") dryRun = true;
            else if (!a.StartsWith("-")) targetDir = a;
        }

        targetDir = Path.GetFullPath(targetDir);
        if (!Directory.Exists(targetDir))
        {
            Console.Error.WriteLine("Directory not found: " + targetDir);
            return 1;
        }

        string[] files = Directory.GetFiles(targetDir);
        if (files.Length == 0)
        {
            Console.WriteLine("No files found.");
            return 0;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        int digits = files.Length.ToString().Length;
        if (digits < 2) digits = 2;

        // Build rename plan, skipping files already in correct position
        var temps = new List<string>();
        var finalNames = new List<string>();
        var originals = new List<string>();

        try
        {
            for (int i = 0; i < files.Length; i++)
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

            // Phase 1: rename to temp names to avoid collisions
            for (int i = 0; i < originals.Count; i++)
            {
                if (!dryRun)
                    File.Move(originals[i], temps[i]);
            }

            // Phase 2: rename temp names to final sequential names
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
