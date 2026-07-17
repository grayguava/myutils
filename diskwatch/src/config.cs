using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

public class Config
{
    private List<string> _drives = new List<string> { "C" };
    private List<string> _smartDevices = new List<string>();
    private List<int> _smartAttrs = new List<int> { 5, 9, 197, 198, 190 };

    public List<string> Drives { get { return _drives; } set { _drives = value; } }
    public string SmartCtlPath { get; set; }
    public List<string> SmartDevices { get { return _smartDevices; } set { _smartDevices = value; } }
    public List<int> SmartAttrs { get { return _smartAttrs; } set { _smartAttrs = value; } }

    public Config()
    {
        SmartCtlPath = "smartctl";
    }

    static string NormalizeSmartCtlPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "smartctl";
        string trimmed = raw.Trim();
        if (!trimmed.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':' || c == '\\' || c == '/'))
            return "smartctl";
        string name = Path.GetFileName(trimmed);
        if (!string.Equals(name, "smartctl", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "smartctl.exe", StringComparison.OrdinalIgnoreCase))
            return "smartctl";
        return trimmed;
    }

    static bool IsSafeDeviceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '.' || c == '_' || c == '-');
    }

    public static Config Load(string path)
    {
        var config = new Config();
        if (!File.Exists(path)) return config;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }

        if (values.ContainsKey("Drives"))
            config.Drives = ParseList(values["Drives"]);
        if (values.ContainsKey("SmartCtlPath"))
            config.SmartCtlPath = NormalizeSmartCtlPath(values["SmartCtlPath"]);
        if (values.ContainsKey("SmartDevices"))
            config.SmartDevices = ParseList(values["SmartDevices"]).Where(IsSafeDeviceName).ToList();
        if (values.ContainsKey("SmartAttrs"))
        {
            var attrs = new List<int>();
            foreach (string s in values["SmartAttrs"].Split(','))
            {
                int id;
                if (int.TryParse(s.Trim(), out id))
                    attrs.Add(id);
            }
            if (attrs.Count > 0) config.SmartAttrs = attrs;
        }

        return config;
    }

    static List<string> ParseList(string raw)
    {
        var result = new List<string>();
        foreach (string s in raw.Split(','))
        {
            string t = s.Trim();
            if (t.Length > 0) result.Add(t);
        }
        return result;
    }

    public static string BaseDir()
    {
        return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }
}
