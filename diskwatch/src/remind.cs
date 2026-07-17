using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

class Remind
{
    static string ResultPath()
    {
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "..", "logs", "result.json"));
    }

    public static int Show()
    {
        string resultPath = ResultPath();
        if (!File.Exists(resultPath))
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
        b.AppendLine("Today's run is successful.");
        b.AppendLine("Please don't forget to review the latest reports.");
        b.AppendLine();
        b.AppendLine(date);
        b.AppendLine();

        if (state.Drives != null)
        {
            foreach (var kv in state.Drives)
            {
                string fs = kv.Value.Filesystem;
                if (fs == "clean") fs = "Clean";
                b.AppendLine("Drive " + kv.Key + ": " + fs);
            }
        }

        string endurance = "N/A";
        string health = "Unknown";

        if (state.Smart != null)
        {
            foreach (var kv in state.Smart)
            {
                health = kv.Value.Health ?? "Unknown";
                if (kv.Value.Endurance >= 0 && kv.Value.Endurance <= 100)
                    endurance = kv.Value.Endurance + "%";
                break;
            }
        }

        b.AppendLine("Endurance: " + endurance);
        b.Append("SMART: " + health);

        MessageBox.Show(b.ToString(), "diskwatch",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

        return 0;
    }
}
