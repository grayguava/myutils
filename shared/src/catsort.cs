using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

class Program
{
    static List<Category> _cats = new List<Category>();
    static bool _dryRun;
    static int _copied;
    static int _verified;
    static int _failed;

    class Category
    {
        public string Name;
        public List<string> Exts = new List<string>();
    }

    static int Main(string[] args)
    {
        string targetDir = ".";
        string confPath = null;

        foreach (string a in args)
        {
            if (a == "--dry-run" || a == "-n") _dryRun = true;
            else if (a.StartsWith("--conf=")) confPath = a.Substring(7);
            else if (!a.StartsWith("-")) targetDir = a;
        }

        targetDir = Path.GetFullPath(targetDir);
        if (!Directory.Exists(targetDir))
        {
            Console.Error.WriteLine("Directory not found: " + targetDir);
            return 1;
        }

        if (confPath == null)
            confPath = Path.Combine(
                Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                "..", "conf", "catsort.ini");

        if (!File.Exists(confPath))
        {
            Console.Error.WriteLine("Config not found: " + confPath);
            return 1;
        }

        LoadConfig(confPath);

        if (_cats.Count == 0)
        {
            Console.Error.WriteLine("No categories found in config.");
            return 1;
        }

        Console.WriteLine("Sorting files in " + targetDir);
        if (_dryRun) Console.WriteLine("  (dry run)");
        Console.WriteLine();

        string[] files = Directory.GetFiles(targetDir);
        string parentDirName = Path.GetFileName(targetDir);
        foreach (string file in files)
            ProcessFile(file, targetDir, parentDirName);

        Console.WriteLine();
        if (_dryRun)
            Console.WriteLine("  Dry run.  " + _copied + " would move.");
        else
            Console.WriteLine("  Done.  " + _copied + " copied, "
                + _verified + " verified, " + _failed + " failed.");

        return _failed > 0 ? 1 : 0;
    }

    static void LoadConfig(string path)
    {
        Category cur = null;
        foreach (string line in File.ReadAllLines(path))
        {
            string s = line.Trim();
            if (s.Length == 0 || s.StartsWith("#") || s.StartsWith(";"))
                continue;

            if (s.StartsWith("[") && s.EndsWith("]"))
            {
                cur = new Category { Name = s.Substring(1, s.Length - 2) };
                _cats.Add(cur);
            }
            else if (cur != null && s.StartsWith("ext=", StringComparison.OrdinalIgnoreCase))
            {
                string exts = s.Substring(4);
                foreach (string e in exts.Split(','))
                {
                    string ext = e.Trim().ToLowerInvariant();
                    if (ext.Length > 0)
                    {
                        if (!ext.StartsWith(".")) ext = "." + ext;
                        if (!cur.Exts.Contains(ext)) cur.Exts.Add(ext);
                    }
                }
            }
        }
    }

    static void ProcessFile(string filePath, string targetDir, string parentDirName)
    {
        string fileName = Path.GetFileName(filePath);
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        foreach (Category cat in _cats)
        {
            if (!cat.Exts.Contains(ext))
                continue;

            // Skip files already inside a folder matching the category
            if (string.Equals(parentDirName, cat.Name, StringComparison.OrdinalIgnoreCase))
                return;

            string catDir = Path.Combine(targetDir, cat.Name);
            string destPath = Path.Combine(catDir, fileName);

            if (!_dryRun && !Directory.Exists(catDir))
                Directory.CreateDirectory(catDir);

            if (!_dryRun)
            {
                try
                {
                    File.Copy(filePath, destPath, false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  FAIL copy   " + fileName + "  (" + ex.Message + ")");
                    _failed++;
                    return;
                }

                if (!VerifyHash(filePath, destPath))
                {
                    Console.WriteLine("  FAIL verify " + fileName + "  (hash mismatch)");
                    File.Delete(destPath);
                    _failed++;
                    return;
                }

                File.Delete(filePath);
                _verified++;
            }

            _copied++;
            Console.WriteLine("  " + (_dryRun ? "would move" : "moved") + "  " + fileName
                + "  ->  " + cat.Name + "/");
            return;
        }
    }

    static bool VerifyHash(string src, string dst)
    {
        try
        {
            using (var sha = SHA256.Create())
            {
                byte[] h1, h2;
                using (var s = File.OpenRead(src)) h1 = sha.ComputeHash(s);
                using (var s = File.OpenRead(dst)) h2 = sha.ComputeHash(s);

                if (h1.Length != h2.Length) return false;
                for (int i = 0; i < h1.Length; i++)
                    if (h1[i] != h2[i]) return false;
                return true;
            }
        }
        catch { return false; }
    }
}
