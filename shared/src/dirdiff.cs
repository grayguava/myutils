using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
#if WINDOWS
using System.Windows.Forms;
#endif

class Program {
    static string HR = new string('\u2500', 50);

    class FileEntry {
        public string AbsPath;
        public long Size;
    }

    [STAThread]
    static void Main(string[] args) {
        string sourceRoot, destRoot;

        if (args.Length >= 2) {
            sourceRoot = args[0];
            destRoot   = args[1];
        } else {
#if WINDOWS
            Console.Write("  Source:     ");
            sourceRoot = PickFolder("SOURCE directory");
            Console.Write("\r  Source:      " + sourceRoot + "\n");

            Console.Write("  Dest:       ");
            destRoot = PickFolder("DESTINATION directory (the copy)");
            Console.Write("\r  Dest:        " + destRoot + "\n");
#else
            Console.Error.WriteLine("Usage: dirdiff <source> <destination>");
            return;
#endif
        }

        Console.WriteLine();
        Console.WriteLine("  " + new string('=', 48));
        Console.WriteLine("  Directory Comparison Report");
        Console.WriteLine("  " + new string('=', 48));
        Console.WriteLine();

        Console.WriteLine();
        Console.WriteLine("  " + HR);
        Console.WriteLine();

        Console.WriteLine("  Scanning directories...");
        var srcMap = BuildFileMap(sourceRoot);
        var dstMap = BuildFileMap(destRoot);

        var srcPaths = new HashSet<string>(srcMap.Keys);
        var dstPaths = new HashSet<string>(dstMap.Keys);

        var inBoth  = srcPaths.Intersect(dstPaths).OrderBy(x => x).ToList();
        var missing = srcPaths.Except(dstPaths).OrderBy(x => x).ToList();
        var extra   = dstPaths.Except(srcPaths).OrderBy(x => x).ToList();

        int nTotal   = srcMap.Count;
        int nPresent = inBoth.Count;

        Console.WriteLine();
        Console.WriteLine("  Files present:   " + Fmt(nPresent, nTotal));
        Console.WriteLine();

        if (missing.Count > 0) {
            Console.WriteLine("  Missing files (" + missing.Count + "):");
            Console.WriteLine();
            for (int i = 0; i < Math.Min(missing.Count, 20); i++)
                Console.WriteLine("    - " + missing[i]);
            if (missing.Count > 20)
                Console.WriteLine("    ... and " + (missing.Count - 20) + " more");
            Console.WriteLine();
        }

        if (extra.Count > 0) {
            Console.WriteLine("  Extra files (" + extra.Count + "):");
            Console.WriteLine();
            for (int i = 0; i < Math.Min(extra.Count, 20); i++)
                Console.WriteLine("    + " + extra[i]);
            if (extra.Count > 20)
                Console.WriteLine("    ... and " + (extra.Count - 20) + " more");
            Console.WriteLine();
        }

        if (missing.Count > 0 || extra.Count > 0) {
            Console.WriteLine("  " + HR);
            Console.WriteLine();
        }

        int sizeOk  = 0;
        var sizeBad = new List<string>();
        foreach (var relPath in inBoth) {
            long srcSize = srcMap[relPath].Size;
            long dstSize = dstMap[relPath].Size;
            if (srcSize == dstSize)
                sizeOk++;
            else
                sizeBad.Add(relPath);
        }

        Console.WriteLine("  Sizes matched:   " + Fmt(sizeOk, nPresent));
        Console.WriteLine();

        if (sizeBad.Count > 0) {
            Console.WriteLine("  Size mismatches (" + sizeBad.Count + "):");
            for (int i = 0; i < Math.Min(sizeBad.Count, 20); i++) {
                string rp = sizeBad[i];
                long srcSize = srcMap[rp].Size;
                long dstSize = dstMap[rp].Size;
                Console.WriteLine("    ! " + rp + "  (" + srcSize + " vs " + dstSize + " bytes)");
            }
            if (sizeBad.Count > 20)
                Console.WriteLine("    ... and " + (sizeBad.Count - 20) + " more");
            Console.WriteLine();
        }

        Console.WriteLine("  " + HR);
        Console.WriteLine();

        int hashOk  = 0;
        int hashBad = 0;
        int done    = 0;
        int nHash   = inBoth.Count;
        object lockObj = new object();

        Parallel.ForEach(inBoth, new ParallelOptions { MaxDegreeOfParallelism = 8 }, (relPath) => {
            string sh = HashFile(srcMap[relPath].AbsPath);
            string dh = HashFile(dstMap[relPath].AbsPath);
            lock (lockObj) {
                done++;
                if (sh != null && dh != null && sh == dh)
                    hashOk++;
                else
                    hashBad++;
                Console.Write("  Computing SHA256 hashes (" + done + "/" + nHash + ")\r");
            }
        });

        Console.Write("  Computing SHA256 hashes (" + nHash + "/" + nHash + ")");

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("  Hashes matched:  " + Fmt(hashOk, nHash));
        Console.WriteLine();

        if (hashBad > 0) {
            Console.WriteLine("  Hash mismatches (" + hashBad + "):");
            Console.WriteLine();
            Console.WriteLine("    (" + hashBad + " files with differing or unreadable hashes)");
            Console.WriteLine();
        }

        Console.WriteLine("  " + HR);
        Console.WriteLine();

        int nIssues = missing.Count + extra.Count + sizeBad.Count + hashBad;
        if (nIssues == 0) {
            Console.WriteLine("  All " + nTotal + " files verified OK.");
        } else {
            Console.WriteLine("  Issue(s) found:");
            Console.WriteLine();
            if (missing.Count > 0) Console.WriteLine("    - " + missing.Count + " items missing");
            if (extra.Count > 0)   Console.WriteLine("    + " + extra.Count + " items extra");
            if (sizeBad.Count > 0) Console.WriteLine("    ! " + sizeBad.Count + " items size mismatch");
            if (hashBad > 0)       Console.WriteLine("    ! " + hashBad + " items hash mismatch");
        }
    }

    static string PickFolder(string title) {
        using (var dlg = new OpenFileDialog()) {
            dlg.Title = title;
            dlg.CheckFileExists = false;
            dlg.CheckPathExists = true;
            dlg.ValidateNames = false;
            dlg.Multiselect = false;
            dlg.FileName = "Select this folder";
            if (dlg.ShowDialog() == DialogResult.OK)
                return Path.GetDirectoryName(dlg.FileName);
        }
        Console.WriteLine("\n  No folder selected (cancelled). Exiting.");
        Environment.Exit(1);
        return null;
    }

    static Dictionary<string, FileEntry> BuildFileMap(string root) {
        var map = new Dictionary<string, FileEntry>();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
            try {
                var fi = new FileInfo(file);
                map[file.Substring(root.Length).TrimStart('\\', '/')] = new FileEntry {
                    AbsPath = file,
                    Size    = fi.Length,
                };
            } catch { }
        }
        return map;
    }

    const int ChunkSize = 1024 * 1024;

    static string HashFile(string path) {
        try {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize))
            using (var sha = SHA256.Create()) {
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLower();
            }
        } catch {
            return null;
        }
    }

    static string Fmt(int n, int total) {
        if (total == 0)
            return "     0 / 0         (  N/A  )";
        return n.ToString().PadLeft(6) + " / " + total.ToString().PadRight(6)
            + "      (" + (n * 100.0 / total).ToString("F1") + "%)";
    }
}
