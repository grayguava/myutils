// kdbxWatch
//
// Watches a source directory for changes to .kdbx files. When a file's
// content actually changes (verified by hash, not just by filesystem event),
// it takes a fresh snapshot of ALL .kdbx files in the source directory into
// a new timestamped folder under DestDir.
//
// Design notes:
//  - No external dependencies. Hand-parsed INI config.
//  - No persisted state. Last-known hashes live in memory only; on startup
//    we hash everything fresh and take an immediate baseline snapshot.
//  - Per-file debounce timers absorb multiple filesystem events from a
//    single save (e.g. write + rename).
//  - A single lock guards the "compare hash -> decide -> snapshot -> update
//    baseline" sequence so concurrent timer callbacks can't race.
//  - Source files are only ever read and copied from. Never modified,
//    renamed, or deleted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace kdbxWatch
{
    internal static class Program
    {
        // Resolved configuration.
        private static string SourceDir = "";
        private static string DestDir = "";
        private static string HashAlgo = "SHA256";
        private static int DebounceMs = 5000;
        private static string LogFile = "";

        // In-memory state. Guarded by StateLock.
        private static readonly Dictionary<string, string> LastHashes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Timer> DebounceTimers =
            new Dictionary<string, Timer>(StringComparer.OrdinalIgnoreCase);
        private static readonly object StateLock = new object();
        private static readonly object LogLock = new object();

        private static Mutex SingleInstanceMutex;

        private static void Main()
        {
            bool isNewInstance;
            SingleInstanceMutex = new Mutex(true, @"Global\kdbxWatchSingleInstance", out isNewInstance);

            if (!isNewInstance)
            {
                // Another instance already owns the mutex. Exit immediately
                // without touching config or the log file.
                return;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            LoadConfig(Path.Combine(baseDir, "config.ini"), baseDir);
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile) ?? baseDir);
            Directory.CreateDirectory(DestDir);

            Log("Started. Watching: " + SourceDir);

            // Baseline: hash everything currently present and take an
            // immediate snapshot. This both seeds LastHashes and guarantees
            // a known-good starting snapshot exists.
            TakeBaselineSnapshot();

            using (var watcher = new FileSystemWatcher(SourceDir, "*.kdbx"))
            {
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                watcher.Changed += OnFileEvent;
                watcher.Created += OnFileEvent;
                watcher.Renamed += OnFileRenamed;
                watcher.EnableRaisingEvents = true;

                // Block forever. Task Scheduler / Task Manager will end the
                // process on logoff; no graceful shutdown logic needed for
                // a personal utility like this.
                new ManualResetEvent(false).WaitOne();
            }
        }

        private static void LoadConfig(string configPath, string baseDir)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in File.ReadAllLines(configPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                values[key] = val;
            }

            SourceDir = RequireValue(values, "WatchSourceDir");
            HashAlgo = values.ContainsKey("HashAlgo") ? values["HashAlgo"] : "SHA256";
            DebounceMs = (values.ContainsKey("DebounceSeconds")
                ? int.Parse(values["DebounceSeconds"])
                : 5) * 1000;

            string destRaw = values.ContainsKey("DestDir") ? values["DestDir"] : "snapshots";
            DestDir = Path.IsPathRooted(destRaw) ? destRaw : Path.Combine(baseDir, destRaw);

            string logRaw = values.ContainsKey("WatchLogFile") ? values["WatchLogFile"] : "logs\\watch.log";
            LogFile = Path.IsPathRooted(logRaw) ? logRaw : Path.Combine(baseDir, logRaw);
        }

        private static string RequireValue(Dictionary<string, string> values, string key)
        {
            if (!values.ContainsKey(key) || values[key].Length == 0)
                throw new InvalidOperationException("Missing required config value: " + key);
            return values[key];
        }

        // --- Filesystem event handling -------------------------------------------------

        private static void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            ScheduleDebounce(e.Name);
        }

        private static void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            // Covers editors that write to a temp file then rename into place.
            ScheduleDebounce(e.Name);
        }

        private static void ScheduleDebounce(string fileName)
        {
            if (fileName == null) return;

            lock (StateLock)
            {
                Timer existing;
                if (DebounceTimers.TryGetValue(fileName, out existing))
                {
                    existing.Change(DebounceMs, Timeout.Infinite);
                    return;
                }

                var timer = new Timer(OnDebounceElapsed, fileName, DebounceMs, Timeout.Infinite);
                DebounceTimers[fileName] = timer;
            }
        }

        private static void OnDebounceElapsed(object state)
        {
            string fileName = (string)state;
            string fullPath = Path.Combine(SourceDir, fileName);

            lock (StateLock)
            {
                // Timer has fired; remove it so a future event schedules a fresh one.
                Timer timer;
                if (DebounceTimers.TryGetValue(fileName, out timer))
                {
                    timer.Dispose();
                    DebounceTimers.Remove(fileName);
                }

                if (!File.Exists(fullPath))
                {
                    Log("File no longer exists, skipping: " + fileName);
                    return;
                }

                string newHash;
                try
                {
                    newHash = ComputeHash(fullPath);
                }
                catch (IOException)
                {
                    // File still locked/being written despite the debounce wait.
                    // Reschedule rather than failing silently.
                    Log("File locked, rescheduling: " + fileName);
                    ScheduleDebounce(fileName);
                    return;
                }

                string oldHash;
                if (LastHashes.TryGetValue(fileName, out oldHash) && oldHash == newHash)
                {
                    Log("Hash unchanged, skipping: " + fileName);
                    return;
                }

                Log("Change detected: " + fileName);
                TakeSnapshot();
            }
        }

        // --- Hashing ---------------------------------------------------------------

        private static string ComputeHash(string path)
        {
            using (HashAlgorithm algo = CreateHashAlgorithm())
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = algo.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static HashAlgorithm CreateHashAlgorithm()
        {
            switch (HashAlgo.ToUpperInvariant())
            {
                case "SHA1": return SHA1.Create();
                case "MD5": return MD5.Create();
                case "SHA256":
                default: return SHA256.Create();
            }
        }

        // --- Snapshotting ------------------------------------------------------------

        private static void TakeBaselineSnapshot()
        {
            lock (StateLock)
            {
                string[] files = Directory.GetFiles(SourceDir, "*.kdbx");

                if (files.Length == 0)
                {
                    Log("No .kdbx files found at startup; baseline is empty.");
                    return;
                }

                // Compute current hashes first so we can compare against the
                // most recent existing snapshot before deciding to copy.
                var currentHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    currentHashes[Path.GetFileName(f)] = ComputeHash(f);
                }

                Dictionary<string, string> lastSnapshotHashes = LoadMostRecentSnapshotHashes();

                if (lastSnapshotHashes != null && HashesMatch(currentHashes, lastSnapshotHashes))
                {
                    foreach (var pair in currentHashes) LastHashes[pair.Key] = pair.Value;
                    Log("Baseline unchanged since last run, skipping snapshot (" + currentHashes.Count + " files).");
                    return;
                }

                string snapshotDir = CreateSnapshotDir();
                Dictionary<string, string> hashes;
                CopyAllKdbxFiles(snapshotDir, out hashes);

                foreach (var pair in hashes) LastHashes[pair.Key] = pair.Value;

                Log("Baseline snapshot created: " + snapshotDir.Substring(DestDir.Length).TrimStart(Path.DirectorySeparatorChar) + " (" + hashes.Count + " files)");
            }
        }

        private static bool HashesMatch(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var pair in a)
            {
                string otherHash;
                if (!b.TryGetValue(pair.Key, out otherHash)) return false;
                if (otherHash != pair.Value) return false;
            }
            return true;
        }

        // Finds the most recent snapshot folder under DestDir by walking the
        // MM -> dd -> HHmmss hierarchy. All levels sort correctly as strings
        // (zero-padded numbers), so the last entry at each level is newest.
        // Returns null if no snapshot exists or the manifest can't be read.
        private static Dictionary<string, string> LoadMostRecentSnapshotHashes()
        {
            if (!Directory.Exists(DestDir)) return null;

            string newestLeaf = FindNewestLeaf(DestDir, 3);
            if (newestLeaf == null) return null;

            string[] sumsFiles = Directory.GetFiles(newestLeaf, "*SUMS.txt");
            if (sumsFiles.Length == 0) return null;

            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string rawLine in File.ReadAllLines(sumsFiles[0]))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0) continue;

                    int sep = line.IndexOf(':');
                    if (sep <= 0) continue;

                    string fileName = line.Substring(0, sep).Trim();
                    string hash = line.Substring(sep + 1).Trim();
                    hashes[fileName] = hash;
                }
            }
            catch (IOException)
            {
                return null; // unreadable manifest -> fall back to taking a fresh snapshot
            }

            return hashes;
        }

        // Recursively finds the lexicographically last leaf directory at the
        // given depth. depth=3 means MM/dd/HHmmss — three levels down.
        private static string FindNewestLeaf(string dir, int depth)
        {
            string[] children = Directory.GetDirectories(dir);
            if (children.Length == 0) return null;

            Array.Sort(children, StringComparer.OrdinalIgnoreCase);
            string newest = children[children.Length - 1];

            if (depth <= 1) return newest;
            return FindNewestLeaf(newest, depth - 1);
        }

        // Caller must hold StateLock.
        private static void TakeSnapshot()
        {
            string snapshotDir = CreateSnapshotDir();
            Dictionary<string, string> hashes;
            int count = CopyAllKdbxFiles(snapshotDir, out hashes);

            // Refresh baseline for every file, since the snapshot just
            // captured the current state of all of them.
            foreach (var pair in hashes) LastHashes[pair.Key] = pair.Value;

            Log("Snapshot created: " + snapshotDir.Substring(DestDir.Length).TrimStart(Path.DirectorySeparatorChar) + " (" + count + " files)");
        }

        private static string CreateSnapshotDir()
        {
            DateTime now = DateTime.Now;
            string fullPath = Path.Combine(
                DestDir,
                now.ToString("MM"),
                now.ToString("dd"),
                now.ToString("HHmmss"));
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        private static int CopyAllKdbxFiles(string snapshotDir, out Dictionary<string, string> hashes)
        {
            hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;

            foreach (string f in Directory.GetFiles(SourceDir, "*.kdbx"))
            {
                string fileName = Path.GetFileName(f);
                string dest = Path.Combine(snapshotDir, fileName);
                File.Copy(f, dest, overwrite: true);

                string hash = ComputeHash(dest); // hash the copy, proof matches what's actually in the snapshot
                hashes[fileName] = hash;
                count++;
            }

            WriteSumsFile(snapshotDir, hashes);
            return count;
        }

        private static void WriteSumsFile(string snapshotDir, Dictionary<string, string> hashes)
        {
            string sumsFileName = HashAlgo.ToUpperInvariant() + "SUMS.txt";
            string sumsPath = Path.Combine(snapshotDir, sumsFileName);

            var fileNames = new List<string>(hashes.Keys);
            fileNames.Sort(StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            foreach (string fileName in fileNames)
            {
                sb.Append(fileName).Append(": ").Append(hashes[fileName]).Append(Environment.NewLine);
            }

            File.WriteAllText(sumsPath, sb.ToString());
        }

        // --- Logging -----------------------------------------------------------------

        private static void Log(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
            lock (LogLock)
            {
                File.AppendAllText(LogFile, line);
            }
        }
    }
}