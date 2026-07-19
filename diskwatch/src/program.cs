using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

class Program
{
    struct Command
    {
        public string Name;
        public string Exe;
        public string Args;
    }

    static string BaseDir()
    {
        return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }

    static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--remind" || args[0] == "/remind"))
            return Remind.Show();

        string baseDir = BaseDir();
        string logsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "logs"));
        string runDir = Path.Combine(logsDir, DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss"));
        string runsDir = Path.Combine(runDir, "runs");

        var commands = LoadCommands(Path.Combine(baseDir, "commands.ini"));
        var smartAttrs = LoadSmartAttrs(Path.Combine(baseDir, "smartAttributes.ini"));

        foreach (var cmd in commands)
        {
            string output;
            int code = CommandRunner.Run(cmd.Exe, cmd.Args, out output);
            SaveRaw(runsDir, cmd.Name, code, output);
        }

        string evtLog = ReadWininitLog();
        SaveRaw(runsDir, "wininit", 0, evtLog ?? "");

        var prev = LoadPrevState(logsDir, runDir);
        var curr = MasterStateManager.Build(runsDir, smartAttrs);
        MasterStateManager.Save(Path.Combine(runDir, "result.json"), curr);

        var dirs = new List<string>(Directory.GetDirectories(logsDir));
        dirs.Sort();
        while (dirs.Count > 5)
        {
            Directory.Delete(dirs[0], true);
            dirs.RemoveAt(0);
        }

        var changes = MasterStateManager.Diff(prev, curr);

        foreach (var kv in curr.Drives)
        {
            string icon = kv.Value.Filesystem == "clean" ? "\u2713" : "!";
            Console.WriteLine("  " + icon + " " + kv.Key + ": " + kv.Value.Filesystem
                + (kv.Value.BadSectorsKb > 0 ? "  bad sectors " + kv.Value.BadSectorsKb + " KB" : ""));
        }
        foreach (var kv in curr.Smart)
        {
            Console.WriteLine("  \u2713 " + kv.Key + "  " + kv.Value.Health
                + (kv.Value.Endurance >= 0 ? "  endurance " + kv.Value.Endurance + "%" : ""));
        }
        Console.WriteLine("  \u2713 No repairs");

        if (changes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Changes detected:");
            foreach (string c in changes)
                Console.WriteLine("    " + c);
            Console.WriteLine();
            Remind.Show(true);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("  No changes since last run.");
        Remind.Show(false);
        return 0;
    }

    static List<Command> LoadCommands(string path)
    {
        var commands = new List<Command>();
        if (!File.Exists(path)) return commands;
        string section = null;
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line.Substring(1, line.Length - 2);
                continue;
            }
            if (section == null) continue;
            int sep = line.IndexOf(' ');
            string exe = sep > 0 ? line.Substring(0, sep) : line;
            string args = sep > 0 ? line.Substring(sep + 1) : "";
            string suffix = MakeSuffix(args);
            commands.Add(new Command { Name = section + "_" + suffix, Exe = exe, Args = args });
        }
        return commands;
    }

    static string MakeSuffix(string args)
    {
        var m = System.Text.RegularExpressions.Regex.Match(args, @"\b([A-Za-z]):");
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        string[] parts = args.Split(' ');
        string last = parts[parts.Length - 1];
        int slash = last.LastIndexOfAny(new char[] { '/', '\\' });
        return slash >= 0 ? last.Substring(slash + 1) : last;
    }

    static List<int> LoadSmartAttrs(string path)
    {
        var attrs = new List<int>();
        if (!File.Exists(path)) return attrs;
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            int id;
            if (int.TryParse(line, out id)) attrs.Add(id);
        }
        return attrs;
    }

    static void SaveRaw(string dir, string name, int exitCode, string output)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".json");
        File.WriteAllText(path,
            "{\"ExitCode\":" + exitCode + ",\"Output\":" + EncodeJson(output ?? "") + "}");
    }

    static string EncodeJson(string s)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('"');
        foreach (char c in s)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    static string ReadWininitLog()
    {
        string[] logNames = {
            "Microsoft-Windows-Wininit/Operational",
            "System", "Application"
        };
        foreach (string logName in logNames)
        {
            try
            {
                using (var log = new System.Diagnostics.EventLog(logName))
                {
                    var sb = new System.Text.StringBuilder();
                    int count = Math.Min(log.Entries.Count, 50);
                    for (int i = log.Entries.Count - 1; i >= log.Entries.Count - count && i >= 0; i--)
                    {
                        var entry = log.Entries[i];
                        bool isRepair = (entry.Source != null
                            && entry.Source.IndexOf("wininit", StringComparison.OrdinalIgnoreCase) >= 0
                            && (entry.InstanceId == 262 || entry.InstanceId == 264
                                || (entry.Message != null
                                    && entry.Message.IndexOf("repair", StringComparison.OrdinalIgnoreCase) >= 0)))
                            || (entry.EntryType == System.Diagnostics.EventLogEntryType.Warning
                                && entry.Message != null
                                && entry.Message.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0
                                && entry.Message.IndexOf("repair", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (isRepair)
                            sb.AppendLine(entry.TimeGenerated.ToString("yyyy-MM-dd HH:mm")
                                + "  " + entry.Source + "  "
                                + (entry.Message ?? "").Replace("\r\n", " ").Replace("\n", " "));
                    }
                    if (sb.Length > 0)
                    {
                        sb.Insert(0, "Source log: " + logName + "\n");
                        return sb.ToString().TrimEnd();
                    }
                }
            }
            catch { continue; }
        }
        return "";
    }

    static MasterState LoadPrevState(string logsDir, string currentDir)
    {
        var dirs = new List<string>(Directory.GetDirectories(logsDir));
        dirs.Sort();
        for (int i = dirs.Count - 1; i >= 0; i--)
        {
            if (dirs[i] != currentDir)
                return MasterStateManager.Load(Path.Combine(dirs[i], "result.json"));
        }
        return null;
    }
}
