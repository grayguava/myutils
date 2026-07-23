using System;
using System.IO;
using System.Management;
using System.Reflection;

class Program
{
    static int LoadDesignCapacity()
    {
        string confPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            ".conf");
        try
        {
            foreach (string rawLine in File.ReadAllLines(confPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;
                int val;
                if (int.TryParse(line, out val)) return val;
            }
        }
        catch { }
        return 44494;
    }

    static int Main()
    {
        int designCapacity = LoadDesignCapacity();
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        int fullCharge = 0, remaining = 0, voltage = 0;
        int chargeRate = 0, dischargeRate = 0, cycles = 0;
        bool charging = false;

        try
        {
            using (var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM BatteryFullChargedCapacity"))
            {
                foreach (ManagementObject o in searcher.Get())
                {
                    fullCharge = Convert.ToInt32(o["FullChargedCapacity"]);
                    break;
                }
            }

            using (var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM BatteryStatus"))
            {
                foreach (ManagementObject o in searcher.Get())
                {
                    remaining = Convert.ToInt32(o["RemainingCapacity"]);
                    voltage = Convert.ToInt32(o["Voltage"]);
                    chargeRate = Convert.ToInt32(o["ChargeRate"]);
                    dischargeRate = Convert.ToInt32(o["DischargeRate"]);
                    charging = Convert.ToBoolean(o["Charging"]);
                    break;
                }
            }

            using (var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM BatteryCycleCount"))
            {
                foreach (ManagementObject o in searcher.Get())
                {
                    cycles = Convert.ToInt32(o["CycleCount"]);
                    break;
                }
            }
        }
        catch { }

        string line = string.Format("[{0}] Design={1}mWh Full={2}mWh Remaining={3}mWh Voltage={4}mV ChargeRate={5}mW DischargeRate={6}mW Cycles={7} Charging={8}",
            time, designCapacity, fullCharge, remaining, voltage, chargeRate, dischargeRate, cycles, charging);

        string logDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "..", "logs"));
        Directory.CreateDirectory(logDir);
        string logPath = Path.Combine(logDir, "batcap.log");
        File.AppendAllText(logPath, line + Environment.NewLine);

        Console.WriteLine(line);
        return 0;
    }
}
