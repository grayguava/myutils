using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public class DriveState
{
    public bool? Dirty;
    public string Filesystem;
    public long BadSectorsKb;
}

public class SmartState
{
    public string Model;
    public string Serial;
    public string Firmware;
    public string Health;
    public int Endurance;
    public Dictionary<string, long> Attrs;
}

public class MasterState
{
    public string Timestamp;
    public Dictionary<string, DriveState> Drives;
    public Dictionary<string, SmartState> Smart;
    public string LastRepair;
}

public static class MasterStateManager
{
    public static MasterState Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            var jss = new JavaScriptSerializer();
            var d = jss.Deserialize<Dictionary<string, object>>(json);
            return MapMaster(d);
        }
        catch { return null; }
    }

    public static void Save(string path, MasterState state)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToJson(state), new System.Text.UTF8Encoding(false));
    }

    public static MasterState Build(string runDir, List<int> smartAttrs)
    {
        var state = new MasterState
        {
            Timestamp = DateTime.Now.ToString("o"),
            Drives = new Dictionary<string, DriveState>(),
            Smart = new Dictionary<string, SmartState>(),
            LastRepair = null
        };

        if (Directory.Exists(runDir))
        {
            foreach (string file in Directory.GetFiles(runDir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                var result = LoadRaw(file);
                if (result == null) continue;

                if (name.StartsWith("fsutil_") || name.StartsWith("chkdsk_"))
                {
                    string letter = name.Substring(name.IndexOf('_') + 1);
                    if (string.IsNullOrEmpty(letter)) continue;
                    if (name.StartsWith("fsutil_"))
                        GetOrCreateDrive(state, letter).Dirty = ParseDirty(result.Output);
                    else
                        ParseChkdsk(result.Output, GetOrCreateDrive(state, letter));
                }
                else if (name.StartsWith("smartctl_"))
                {
                    string label = name.Substring(name.IndexOf('_') + 1);
                    state.Smart[label] = ParseSmart(result.Output, smartAttrs);
                }
                else if (name == "wininit")
                {
                    state.LastRepair = ParseWininit(result.Output);
                }
            }
        }

        return state;
    }

    static DriveState GetOrCreateDrive(MasterState state, string letter)
    {
        DriveState ds;
        if (!state.Drives.TryGetValue(letter, out ds))
        {
            ds = new DriveState { BadSectorsKb = -1 };
            state.Drives[letter] = ds;
        }
        return ds;
    }

    static bool? ParseDirty(string output)
    {
        if (output == null) return null;
        if (output.IndexOf("NOT Dirty", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (output.IndexOf("is set", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return null;
    }

    static void ParseChkdsk(string output, DriveState ds)
    {
        if (output == null) return;
        string o = output;
        if (o.IndexOf("Access Denied", StringComparison.OrdinalIgnoreCase) >= 0)
            ds.Filesystem = "access_denied";
        else if (o.IndexOf("found no problems", StringComparison.OrdinalIgnoreCase) >= 0
                 || o.IndexOf("No further action", StringComparison.OrdinalIgnoreCase) >= 0)
            ds.Filesystem = "clean";
        else if (o.IndexOf("found problems", StringComparison.OrdinalIgnoreCase) >= 0
                 || o.IndexOf("problems found", StringComparison.OrdinalIgnoreCase) >= 0)
            ds.Filesystem = "issues";
        else
            ds.Filesystem = "unknown";

        var m = Regex.Match(o, @"(\d+)\s+KB in bad sectors");
        long b;
        if (m.Success && long.TryParse(m.Groups[1].Value, out b))
            ds.BadSectorsKb = b;
        else
            ds.BadSectorsKb = -1;
    }

    static SmartState ParseSmart(string output, List<int> smartAttrs)
    {
        var ss = new SmartState
        {
            Endurance = -1,
            Attrs = new Dictionary<string, long>()
        };
        if (output == null) return ss;

        string o = output;
        var m = Regex.Match(o, @"Device Model:\s+(.+)");
        if (m.Success) ss.Model = m.Groups[1].Value.Trim();
        m = Regex.Match(o, @"Serial Number:\s+(.+)");
        if (m.Success) ss.Serial = m.Groups[1].Value.Trim();
        m = Regex.Match(o, @"Firmware Version:\s+(.+)");
        if (m.Success) ss.Firmware = m.Groups[1].Value.Trim();
        m = Regex.Match(o, @"SMART overall-health self-assessment test result:\s+(\w+)");
        if (m.Success) ss.Health = m.Groups[1].Value;
        m = Regex.Match(o, @"(\d+)\s+---\s+Percentage Used Endurance Indicator");
        if (m.Success)
        {
            int used = int.Parse(m.Groups[1].Value);
            ss.Endurance = 100 - used;
        }
        foreach (int attrId in smartAttrs)
        {
            m = Regex.Match(o,
                @"^\s*" + attrId + @"\s+\S[\S ]*?\S\s+\S+\s+\d+\s+\d+\s+\S+\s+\S\s+(\d+)",
                RegexOptions.Multiline);
            if (m.Success)
                ss.Attrs[attrId.ToString()] = long.Parse(m.Groups[1].Value);
        }
        return ss;
    }

    static string ParseWininit(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var m = Regex.Match(output, @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2})");
        return m.Success ? m.Groups[1].Value : "found";
    }

    public static List<string> Diff(MasterState prev, MasterState curr)
    {
        var changes = new List<string>();
        if (prev == null) return changes;
        if (prev.Drives == null || curr.Drives == null) return changes;

        foreach (var kv in curr.Drives)
        {
            DriveState pd;
            if (!prev.Drives.TryGetValue(kv.Key, out pd))
            {
                changes.Add(kv.Key + ": new drive");
                continue;
            }
            DriveState cd = kv.Value;
            if (pd.Dirty != cd.Dirty)
                changes.Add(kv.Key + ": dirty " + pd.Dirty + " \u2192 " + cd.Dirty);
            if (pd.Filesystem != cd.Filesystem)
                changes.Add(kv.Key + ": filesystem " + pd.Filesystem + " \u2192 " + cd.Filesystem);
            if (pd.BadSectorsKb != cd.BadSectorsKb)
                changes.Add(kv.Key + ": bad sectors " + pd.BadSectorsKb + " \u2192 " + cd.BadSectorsKb);
        }

        if (prev.Smart != null && curr.Smart != null)
        {
            foreach (var kv in curr.Smart)
            {
                SmartState ps;
                if (!prev.Smart.TryGetValue(kv.Key, out ps))
                {
                    changes.Add(kv.Key + ": new smart device");
                    continue;
                }
                SmartState cs = kv.Value;
                if (ps.Health != cs.Health)
                    changes.Add(kv.Key + ": health " + ps.Health + " \u2192 " + cs.Health);
                if (ps.Endurance != cs.Endurance)
                    changes.Add(kv.Key + ": endurance " + ps.Endurance + "% \u2192 " + cs.Endurance + "%");
                if (ps.Attrs != null && cs.Attrs != null)
                {
                    foreach (var akv in cs.Attrs)
                    {
                        long pv;
                        if (ps.Attrs.TryGetValue(akv.Key, out pv) && pv != akv.Value)
                            changes.Add(kv.Key + ": attr " + akv.Key + " " + pv + " \u2192 " + akv.Value);
                    }
                }
            }
        }

        if (prev.LastRepair != curr.LastRepair)
            changes.Add("repair events changed");

        return changes;
    }

    class RawResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
    }

    static RawResult LoadRaw(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var jss = new JavaScriptSerializer();
            return jss.Deserialize<RawResult>(json);
        }
        catch { return null; }
    }

    static MasterState MapMaster(Dictionary<string, object> d)
    {
        var s = new MasterState();
        if (d == null) return s;
        s.Timestamp = d.ContainsKey("timestamp") ? (string)d["timestamp"] : null;
        if (d.ContainsKey("drives"))
        {
            var dd = d["drives"] as Dictionary<string, object>;
            if (dd != null)
            {
                s.Drives = new Dictionary<string, DriveState>();
                foreach (var kv in dd)
                    s.Drives[kv.Key] = MapDrive(kv.Value as Dictionary<string, object>);
            }
        }
        if (d.ContainsKey("smart"))
        {
            var sd = d["smart"] as Dictionary<string, object>;
            if (sd != null)
            {
                s.Smart = new Dictionary<string, SmartState>();
                foreach (var kv in sd)
                    s.Smart[kv.Key] = MapSmart(kv.Value as Dictionary<string, object>);
            }
        }
        s.LastRepair = d.ContainsKey("lastRepair") ? (string)d["lastRepair"] : null;
        return s;
    }

    static DriveState MapDrive(Dictionary<string, object> d)
    {
        var ds = new DriveState();
        if (d == null) return ds;
        if (d.ContainsKey("dirty")) ds.Dirty = d["dirty"] as bool?;
        ds.Filesystem = d.ContainsKey("filesystem") ? (string)d["filesystem"] : null;
        if (d.ContainsKey("badSectorsKb")) ds.BadSectorsKb = Convert.ToInt64(d["badSectorsKb"]);
        return ds;
    }

    static SmartState MapSmart(Dictionary<string, object> d)
    {
        var ss = new SmartState();
        if (d == null) return ss;
        ss.Model = d.ContainsKey("model") ? (string)d["model"] : null;
        ss.Serial = d.ContainsKey("serial") ? (string)d["serial"] : null;
        ss.Firmware = d.ContainsKey("firmware") ? (string)d["firmware"] : null;
        ss.Health = d.ContainsKey("health") ? (string)d["health"] : null;
        if (d.ContainsKey("endurance")) ss.Endurance = Convert.ToInt32(d["endurance"]);
        if (d.ContainsKey("attrs"))
        {
            var ad = d["attrs"] as Dictionary<string, object>;
            if (ad != null)
            {
                ss.Attrs = new Dictionary<string, long>();
                foreach (var kv in ad)
                    ss.Attrs[kv.Key] = Convert.ToInt64(kv.Value);
            }
        }
        return ss;
    }

    static string ToJson(MasterState s)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\r\n");
        Field(sb, "timestamp", s.Timestamp, 1); sb.Append(",\r\n");
        sb.Append("  \"drives\": {\r\n");
        bool first = true;
        if (s.Drives != null)
        {
            foreach (var kv in s.Drives)
            {
                if (!first) sb.Append(",\r\n");
                first = false;
                sb.Append("    \"" + kv.Key + "\": {\r\n");
                Field(sb, "dirty", kv.Value.Dirty, 3); sb.Append(",\r\n");
                Field(sb, "filesystem", kv.Value.Filesystem, 3); sb.Append(",\r\n");
                Field(sb, "badSectorsKb", kv.Value.BadSectorsKb, 3); sb.Append("\r\n");
                sb.Append("    }");
            }
        }
        sb.Append("\r\n  },\r\n");
        sb.Append("  \"smart\": {\r\n");
        first = true;
        if (s.Smart != null)
        {
            foreach (var kv in s.Smart)
            {
                if (!first) sb.Append(",\r\n");
                first = false;
                sb.Append("    \"" + kv.Key + "\": {\r\n");
                Field(sb, "model", kv.Value.Model, 3); sb.Append(",\r\n");
                Field(sb, "serial", kv.Value.Serial, 3); sb.Append(",\r\n");
                Field(sb, "firmware", kv.Value.Firmware, 3); sb.Append(",\r\n");
                Field(sb, "health", kv.Value.Health, 3); sb.Append(",\r\n");
                Field(sb, "endurance", kv.Value.Endurance, 3); sb.Append(",\r\n");
                sb.Append("      \"attrs\": {\r\n");
                bool afirst = true;
                if (kv.Value.Attrs != null)
                {
                    foreach (var akv in kv.Value.Attrs)
                    {
                        if (!afirst) sb.Append(",\r\n");
                        afirst = false;
                        sb.Append("        \"" + akv.Key + "\": " + akv.Value);
                    }
                }
                sb.Append("\r\n      }\r\n");
                sb.Append("    }");
            }
        }
        sb.Append("\r\n  },\r\n");
        Field(sb, "lastRepair", s.LastRepair, 1); sb.Append("\r\n");
        sb.Append("}\r\n");
        return sb.ToString();
    }

    static void Field(System.Text.StringBuilder sb, string name, object val, int indent)
    {
        string pad = new string(' ', indent * 2);
        sb.Append(pad + "\"" + name + "\": ");
        if (val == null) sb.Append("null");
        else if (val is bool) sb.Append(((bool)val) ? "true" : "false");
        else if (val is long || val is int) sb.Append(val.ToString());
        else
        {
            sb.Append('"');
            foreach (char c in val.ToString())
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
