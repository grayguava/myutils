using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

class MainForm : Form
{
    const int MAX_LOGS = 10;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    const int WM_VSCROLL = 0x115;
    const int SB_TOP = 6;

    Button selectBtn;
    RichTextBox logBox;
    Label statusLabel, countLabel;

    List<string> selectedFiles = new List<string>();
    CleanCore core;
    string exeDir, logDir;

    static Color C_Window = Color.FromArgb(30, 30, 30);
    static Color C_Control = Color.FromArgb(45, 45, 48);
    static Color C_Input = Color.FromArgb(60, 60, 65);
    static Color C_Text = Color.FromArgb(204, 204, 204);
    static Color C_Muted = Color.FromArgb(140, 140, 140);
    static Color C_LogBg = Color.FromArgb(20, 20, 20);
    static Color C_LogFg = Color.FromArgb(200, 220, 200);

    public MainForm()
    {
        exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        logDir = Path.Combine(exeDir, "..", "logs", "clean");
        core = new CleanCore(ResolveExifTool());

        Text = "etgui_wrapper - clean";
        ClientSize = new Size(740, 560);
        MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = C_Window;
        ForeColor = C_Text;
        Font = new Font("Segoe UI", 9);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        BuildUI();
        CheckExifTool();
    }

    string ResolveExifTool()
    {
        foreach (string dir in Environment.GetEnvironmentVariable("PATH").Split(';'))
        {
            string c = Path.Combine(dir.Trim(), "exiftool.exe");
            if (File.Exists(c)) return c;
        }
        return null;
    }

    void CheckExifTool()
    {
        string ver;
        if (core.ExifToolPath != null && core.CheckExifTool(out ver))
            statusLabel.Text = "ExifTool " + ver;
        else
        {
            statusLabel.Text = "exiftool.exe not found on PATH";
            selectBtn.Enabled = false;
        }
    }

    Button FlatButton(string text, int width, int height)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = C_Control,
            ForeColor = C_Text,
            FlatAppearance = { BorderColor = Color.FromArgb(70, 70, 75), MouseOverBackColor = C_Control, MouseDownBackColor = C_Control, BorderSize = 1 },
            Font = new Font("Segoe UI", 9),
            Cursor = Cursors.Hand,
        };
        return b;
    }

    void BuildUI()
    {
        var topPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Height = 44,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 10, 10, 4),
        };
        selectBtn = FlatButton("Select Files...", 120, 30);
        selectBtn.Click += SelectBtn_Click;
        countLabel = new Label { Text = "No files selected", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = C_Muted };
        topPanel.Controls.Add(selectBtn);
        topPanel.Controls.Add(countLabel);

        Controls.Add(topPanel);

        var logPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 8),
        };
        logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = C_LogBg,
            ForeColor = C_LogFg,
            Font = new Font("Consolas", 9),
            WordWrap = false,
            BorderStyle = BorderStyle.FixedSingle,
        };
        logPanel.Controls.Add(logBox);
        Controls.Add(logPanel);

        statusLabel = new Label
        {
            Text = "Ready",
            Dock = DockStyle.Bottom,
            ForeColor = C_Muted,
            Padding = new Padding(10, 0, 0, 4),
        };
        Controls.Add(statusLabel);
    }

    void Log(string line)
    {
        if (logBox.InvokeRequired) { logBox.Invoke((MethodInvoker)(() => Log(line))); return; }
        logBox.AppendText(line + "\n");
        logBox.SelectionStart = logBox.Text.Length;
        logBox.ScrollToCaret();
    }

    void ResetScroll()
    {
        if (logBox.IsHandleCreated)
            SendMessage(logBox.Handle, WM_VSCROLL, (IntPtr)SB_TOP, IntPtr.Zero);
    }

    void SetStatus(string text)
    {
        if (statusLabel.InvokeRequired) { statusLabel.Invoke((MethodInvoker)(() => statusLabel.Text = text)); return; }
        statusLabel.Text = text;
    }

    void SetEnabled(bool enabled)
    {
        if (selectBtn.InvokeRequired) { selectBtn.Invoke((MethodInvoker)(() => selectBtn.Enabled = enabled)); return; }
        selectBtn.Enabled = enabled;
    }

    void SelectBtn_Click(object sender, EventArgs e)
    {
        using (var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select files to strip metadata from",
            Filter = "Supported Files|*.jpg;*.jpeg;*.png;*.webp;*.heic;*.tif;*.tiff;*.mp4;*.mov;*.pdf|All Files|*.*",
        })
        {
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                selectedFiles.Clear();
                selectedFiles.AddRange(dlg.FileNames);
                countLabel.Text = string.Format("{0} file(s) selected", selectedFiles.Count);
                countLabel.ForeColor = C_Text;

                logBox.Clear();
                ResetScroll();
                var files = new List<string>(selectedFiles);
                SetEnabled(false);
                Log("Selected files:");
                foreach (string f in files)
                    Log("  " + Path.GetFileName(f));
                Log("");
                Log("Starting metadata cleanup...\n");
                SetStatus("Working...");

                var bw = new BackgroundWorker { WorkerReportsProgress = true };
                bw.ProgressChanged += (_, args) => Log((string)args.UserState);
                bw.RunWorkerCompleted += (_, args) =>
                {
                    string outcome = args.Result as string ?? "SUCCESS";
                    SetStatus(outcome);
                    SetEnabled(true);
                    WriteLog(outcome);
                };
                bw.DoWork += (_, args) =>
                {
                    var logLines = new List<string>();
                    core.Log += line => logLines.Add(line);
                    try
                    {
                        core.Run(files);
                        foreach (string ln in logLines) bw.ReportProgress(0, ln);
                        args.Result = "SUCCESS";
                    }
                    catch (InvalidOperationException ex)
                    {
                        logLines.Add(""); logLines.Add("Aborted: " + ex.Message);
                        foreach (string ln in logLines) bw.ReportProgress(0, ln);
                        args.Result = "ABORT: " + ex.Message;
                    }
                    catch (Exception ex)
                    {
                        logLines.Add(""); logLines.Add("Unexpected error: " + ex.Message);
                        foreach (string ln in logLines) bw.ReportProgress(0, ln);
                        args.Result = "ABORT: " + ex.Message;
                    }
                    finally { core.Log -= line => logLines.Add(line); }
                };
                bw.RunWorkerAsync();
            }
        }
    }

    void WriteLog(string outcome)
    {
        try
        {
            Directory.CreateDirectory(logDir);
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(logDir, "clean_" + ts + ".log");
            var sb = new StringBuilder();
            sb.AppendLine("etgui_wrapper - clean");
            sb.AppendLine("Timestamp : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Outcome   : " + outcome);
            sb.AppendLine("----------------------------------------");
            sb.AppendLine();
            sb.Append(logBox.Text);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            var all = new List<string>(Directory.GetFiles(logDir, "clean_*.log"));
            all.Sort(); all.Reverse();
            for (int i = MAX_LOGS; i < all.Count; i++)
                try { File.Delete(all[i]); } catch { }
        }
        catch { }
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
