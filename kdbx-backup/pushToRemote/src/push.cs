// pushToRemote
//
// Runs rclone copy from a local source directory to each configured remote
// sequentially. Exits when all remotes are done.
//
// Design notes:
//  - No external dependencies. Hand-parsed INI config.
//  - /target:winexe — no console window, runs silently in the background.
//  - Run-to-completion: starts, syncs all remotes, logs results, exits.
//    Not a daemon — intended to be triggered by Task Scheduler on a schedule.
//  - rclone copy (not sync) — remotes are append-only. Nothing is ever
//    deleted from the cloud even if deleted locally.
//  - Each remote runs as a child process. stdout and stderr are both
//    captured and written to the log file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace kdbxPushToRemote
{
    internal static class Program
    {
        // Resolved configuration.
        private static string SourceDir  = "";
        private static string RclonePath = "rclone";
        private static List<string> Remotes = new List<string>();
        private static string RemotePath = "kdbx-backup";
        private static string LogFile    = "";

        private static readonly object LogLock = new object();

        private static void Main()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            LoadConfig(Path.Combine(baseDir, "config.ini"), baseDir);
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile) ?? baseDir);

            Log("--- Sync started ---");

            if (Remotes.Count == 0)
            {
                Log("No remotes configured. Check Remotes= in config.ini.");
                return;
            }

            if (!Directory.Exists(SourceDir))
            {
                Log("Source directory not found: " + SourceDir);
                return;
            }

            foreach (string remote in Remotes)
            {
                string trimmed = remote.Trim();
                if (trimmed.Length == 0) continue;
                SyncRemote(trimmed);
            }

            Log("--- Sync complete ---");
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

            string sourceRaw = values.ContainsKey("SourceDir") ? values["SourceDir"] : "..\\databaseCopies";
            SourceDir = Path.IsPathRooted(sourceRaw)
                ? sourceRaw
                : Path.GetFullPath(Path.Combine(baseDir, sourceRaw));

            RclonePath = "rclone";
            RemotePath = values.ContainsKey("RemotePath") ? values["RemotePath"] : "kdbx-backup";

            if (values.ContainsKey("Remotes"))
            {
                foreach (string r in values["Remotes"].Split(','))
                {
                    string trimmed = r.Trim();
                    if (trimmed.Length > 0) Remotes.Add(trimmed);
                }
            }

            string logRaw = values.ContainsKey("LogFile") ? values["LogFile"] : "..\\logs\\rclone.log";
            LogFile = Path.IsPathRooted(logRaw)
                ? logRaw
                : Path.GetFullPath(Path.Combine(baseDir, logRaw));
        }

        private static void SyncRemote(string remote)
        {
            string destination = remote + ":" + RemotePath;
            Log("Syncing to " + destination + " ...");

            var psi = new ProcessStartInfo
            {
                FileName               = RclonePath,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            psi.ArgumentList.Add("copy");
            psi.ArgumentList.Add(SourceDir);
            psi.ArgumentList.Add(destination);
            psi.ArgumentList.Add("--stats-one-line");

            var outputBuffer = new StringBuilder();
            var errorBuffer  = new StringBuilder();

            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    // Collect stdout and stderr asynchronously to avoid
                    // deadlocks when both buffers fill simultaneously.
                    process.OutputDataReceived += (s, e) => {
                        if (e.Data != null) lock (outputBuffer) { outputBuffer.AppendLine(e.Data); }
                    };
                    process.ErrorDataReceived += (s, e) => {
                        if (e.Data != null) lock (errorBuffer) { errorBuffer.AppendLine(e.Data); }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    int exit = process.ExitCode;
                    string stdout = outputBuffer.ToString().Trim();
                    string stderr = errorBuffer.ToString().Trim();

                    if (stdout.Length > 0) Log("  [stdout] " + stdout);
                    if (stderr.Length > 0) Log("  [stderr] " + stderr);

                    if (exit == 0)
                        Log("  " + remote + ": OK");
                    else
                        Log("  " + remote + ": FAILED (exit " + exit + ")");
                }
            }
            catch (Exception ex)
            {
                Log("  " + remote + ": ERROR launching rclone — " + ex.Message);
            }
        }

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