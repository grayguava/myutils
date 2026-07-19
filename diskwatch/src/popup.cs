using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

class Remind
{
    static string LatestResultPath()
    {
        string logsDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "..", "logs"));
        if (!Directory.Exists(logsDir)) return null;
        var dirs = new List<string>(Directory.GetDirectories(logsDir));
        dirs.Sort();
        if (dirs.Count == 0) return null;
        return Path.Combine(dirs[dirs.Count - 1], "result.json");
    }

    public static int Show(bool changesDetected = false)
    {
        string resultPath = LatestResultPath();
        if (resultPath == null || !File.Exists(resultPath))
        {
            Console.Error.WriteLine("No report found. Run diskwatch first.");
            return 1;
        }

        var state = MasterStateManager.Load(resultPath);
        if (state == null)
        {
            Console.Error.WriteLine("Could not read report.");
            return 1;
        }

        string date = state.Timestamp;
        if (date != null && date.Length > 10)
            date = date.Substring(0, 10);

        var b = new System.Text.StringBuilder();
        if (changesDetected)
            b.AppendLine("Today's run is successful. Some values have changed since the last run.");
        else
            b.AppendLine("Today's run is successful. No issues found.");
        b.AppendLine();
        b.AppendLine(date);
        b.AppendLine();

        if (state.Drives != null)
        {
            foreach (var kv in state.Drives)
            {
                string fs = kv.Value.Filesystem;
                if (fs == "clean") fs = "Clean";
                string line = "Drive " + kv.Key + ": " + fs;
                if (kv.Value.BadSectorsKb > 0)
                    line += "  bad sectors " + kv.Value.BadSectorsKb + " KB";
                if (kv.Value.Dirty == true)
                    line += "  DIRTY";
                b.AppendLine(line);
            }
        }

        string healthStr = "Unknown";
        string endurance = "N/A";

        if (state.Smart != null)
        {
            foreach (var kv in state.Smart)
            {
                healthStr = kv.Value.Health ?? "Unknown";
                if (kv.Value.Endurance >= 0 && kv.Value.Endurance <= 100)
                    endurance = kv.Value.Endurance + "%";
                break;
            }
        }

        b.AppendLine("Endurance: " + endurance);
        b.Append("SMART: " + healthStr);

        MessageBoxIcon icon = changesDetected ? MessageBoxIcon.Warning : MessageBoxIcon.Information;

        MessageBox.Show(b.ToString(), "Diskwatch",
            MessageBoxButtons.OK, icon);

        return 0;
    }
}
