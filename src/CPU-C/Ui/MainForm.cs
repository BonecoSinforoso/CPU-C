using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Security.Principal;
using CpuC.Core;

namespace CpuC.Ui;

public sealed class MainForm : Form
{
    private const int LowLimitWarning = 10;

    private readonly CpuTopology _topology = CpuTopology.Detect();
    private readonly ProfileStore _store;
    private readonly Limiter _limiter;
    private readonly bool _askAboutLeftover;

    private readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TrackBar _maxBar = NewBar(1);
    private readonly TrackBar _minBar = NewBar(0);
    private readonly NumericUpDown _maxBox = NewPercentBox(1);
    private readonly NumericUpDown _minBox = NewPercentBox(0);
    private readonly Label _warning = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(176, 92, 0),
        Text = $"Warning: at {LowLimitWarning}% or below, all of Windows may become very slow.",
        Visible = false,
    };
    private readonly List<CheckBox> _coreBoxes = [];
    private readonly ListBox _processes = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 4) };
    private readonly Button _apply = new() { Text = "Apply limit", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _restore = new() { Text = "Restore normal", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _admin = new() { Text = "Restart as administrator", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Window,
    };

    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private readonly Icon _iconNormal = MakeIcon(Color.FromArgb(46, 139, 87));
    private readonly Icon _iconLimited = MakeIcon(Color.FromArgb(214, 96, 20));

    private bool _loading;
    private bool _skipRestoreOnClose;

    public MainForm(bool askAboutLeftover)
    {
        _askAboutLeftover = askAboutLeftover;
        _store = ProfileStore.Load(_topology);
        _limiter = new Limiter(_topology);
        _limiter.Log += AppendLog;

        // Everything is built with layout suspended so Windows DPI scaling
        // (125%, 150%...) is applied exactly once, with every control already in the window.
        SuspendLayout();
        Text = "CPU-C — CPU Controller";
        Font = new Font("Segoe UI", 9F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 740);
        MinimumSize = new Size(600, 660);
        StartPosition = FormStartPosition.CenterScreen;
        _status.Font = new Font(Font, FontStyle.Bold);
        _apply.Font = new Font(Font, FontStyle.Bold);

        BuildLayout();
        BuildTray();
        ResumeLayout(false);
        PerformLayout();

        _timer.Tick += (_, _) => OnTimer();
        _timer.Start();

        ReloadProfileList(_limiter.ActiveProfile?.Name);
        if (_limiter.ActiveProfile is { } active) ShowProfile(active);
        UpdateStatus();
    }

    // ---------- Building the window ----------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // profiles
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // speed
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // cores
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // programs
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // buttons
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));  // log

        root.Controls.Add(BuildProfileBar(), 0, 0);
        root.Controls.Add(BuildSpeedGroup(), 0, 1);
        root.Controls.Add(BuildCoresGroup(), 0, 2);
        root.Controls.Add(BuildProcessGroup(), 0, 3);
        root.Controls.Add(_status, 0, 4);
        root.Controls.Add(BuildActionBar(), 0, 5);
        root.Controls.Add(_log, 0, 6);
        Controls.Add(root);
    }

    private Control BuildProfileBar()
    {
        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        bar.Controls.Add(new Label { Text = "Profile:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        bar.Controls.Add(_profiles);
        bar.Controls.Add(SmallButton("New", (_, _) => NewProfile()));
        bar.Controls.Add(SmallButton("Save", (_, _) => SaveProfile()));
        bar.Controls.Add(SmallButton("Delete", (_, _) => DeleteProfile()));
        bar.Controls.Add(SmallButton("Import", (_, _) => ImportProfiles()));
        bar.Controls.Add(SmallButton("Export", (_, _) => ExportProfile()));

        _profiles.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _profiles.SelectedItem is string name && _store.Find(name) is { } profile)
                ShowProfile(profile);
        };
        return bar;
    }

    private Control BuildSpeedGroup()
    {
        var table = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddSpeedRow(table, 0, "Maximum", _maxBar, _maxBox);
        AddSpeedRow(table, 1, "Minimum", _minBar, _minBox);
        table.Controls.Add(_warning, 0, 2);
        table.SetColumnSpan(_warning, 4);

        _maxBar.ValueChanged += (_, _) => SetSpeed(max: _maxBar.Value, min: null);
        _maxBox.ValueChanged += (_, _) => SetSpeed(max: (int)_maxBox.Value, min: null);
        _minBar.ValueChanged += (_, _) => SetSpeed(max: null, min: _minBar.Value);
        _minBox.ValueChanged += (_, _) => SetSpeed(max: null, min: (int)_minBox.Value);

        return Group("CPU speed (processor state)", table);
    }

    private static void AddSpeedRow(TableLayoutPanel table, int row, string text, TrackBar bar, NumericUpDown box)
    {
        table.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(3, 8, 8, 0) }, 0, row);
        table.Controls.Add(bar, 1, row);
        table.Controls.Add(box, 2, row);
        table.Controls.Add(new Label { Text = "%", AutoSize = true, Margin = new Padding(0, 8, 3, 0) }, 3, row);
    }

    private Control BuildCoresGroup()
    {
        const int columns = 10;
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = columns, Margin = new Padding(0) };
        for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var tips = new ToolTip();
        foreach (var cpu in _topology.Cpus)
        {
            var box = new CheckBox
            {
                Text = cpu.KindLabel.Length == 0 ? cpu.Index.ToString() : $"{cpu.Index} {cpu.KindLabel}",
                AutoSize = true,
                Tag = cpu,
                Margin = new Padding(3, 2, 8, 2),
            };
            var kind = cpu.Kind switch
            {
                CoreKind.Performance => "fast core (P-core)",
                CoreKind.Efficiency => "slow core (E-core)",
                _ => "core",
            };
            tips.SetToolTip(box, $"CPU {cpu.Index}: {kind}, physical core {cpu.PhysicalCore}");
            _coreBoxes.Add(box);
            grid.Controls.Add(box);
        }

        var shortcuts = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        shortcuts.Controls.Add(SmallButton("All", (_, _) => CheckCores(_ => true)));
        if (_topology.IsHybrid)
        {
            shortcuts.Controls.Add(SmallButton("P-cores only", (_, _) => CheckCores(c => c.Kind == CoreKind.Performance)));
            shortcuts.Controls.Add(SmallButton("E-cores only", (_, _) => CheckCores(c => c.Kind == CoreKind.Efficiency)));
        }
        shortcuts.Controls.Add(SmallButton("None", (_, _) => CheckCores(_ => false)));

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 4, 3, 0),
            Text = "Applies only to the programs listed below. With all (or none) checked, cores are not restricted.",
            MaximumSize = new Size(570, 0),
        };

        var stack = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        stack.Controls.Add(grid);
        stack.Controls.Add(shortcuts);
        stack.Controls.Add(hint);
        return Group("Allowed cores", stack);
    }

    private Control BuildProcessGroup()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        buttons.Controls.Add(WideButton("Add running…", (_, _) => AddRunningProcesses()));
        buttons.Controls.Add(WideButton("Add .exe…", (_, _) => AddExecutable()));
        buttons.Controls.Add(WideButton("Type name…", (_, _) => AddTypedProcess()));
        buttons.Controls.Add(WideButton("Remove", (_, _) => RemoveProcesses()));

        table.Controls.Add(_processes, 0, 0);
        table.Controls.Add(buttons, 1, 0);

        var group = new GroupBox { Text = "Programs pinned to the allowed cores", Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8) };
        group.Controls.Add(table);
        return group;
    }

    private Control BuildActionBar()
    {
        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        bar.Controls.Add(_apply);
        bar.Controls.Add(_restore);
        if (!IsElevated()) bar.Controls.Add(_admin);

        _apply.Click += (_, _) => ApplyProfile(ReadProfile());
        _restore.Click += (_, _) => RestoreNormal();
        _admin.Click += (_, _) => RestartAsAdmin();
        return bar;
    }

    private void BuildTray()
    {
        Icon = _iconNormal;
        _tray.Icon = _iconNormal;
        _tray.Visible = true;
        _tray.ContextMenuStrip = _trayMenu;
        _tray.DoubleClick += (_, _) => ShowWindow();

        _trayMenu.Opening += (_, _) =>
        {
            _trayMenu.Items.Clear();
            _trayMenu.Items.Add("Open CPU-C", null, (_, _) => ShowWindow());
            _trayMenu.Items.Add(new ToolStripSeparator());
            foreach (var profile in _store.Profiles)
            {
                var item = new ToolStripMenuItem($"Apply: {profile.Name}")
                {
                    Checked = _limiter.IsActive && string.Equals(_limiter.ActiveProfile!.Name, profile.Name, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (_, _) =>
                {
                    ReloadProfileList(profile.Name);
                    ApplyProfile(profile);
                };
                _trayMenu.Items.Add(item);
            }
            _trayMenu.Items.Add(new ToolStripMenuItem("Restore normal", null, (_, _) => RestoreNormal()) { Enabled = _limiter.IsActive });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, (_, _) => Close());
        };
    }

    private static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 8),
        };
        group.Controls.Add(content);
        return group;
    }

    private static Button SmallButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 1, 4, 1) };
        button.Click += onClick;
        return button;
    }

    private static Button WideButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, Width = 140, Height = 28 };
        button.Click += onClick;
        return button;
    }

    private static TrackBar NewBar(int minimum) => new()
    {
        Minimum = minimum,
        Maximum = 100,
        TickStyle = TickStyle.None,
        AutoSize = false,
        Height = 28,
        Dock = DockStyle.Fill,
        LargeChange = 10,
    };

    private static NumericUpDown NewPercentBox(int minimum) => new() { Minimum = minimum, Maximum = 100, Width = 58 };

    // ---------- Fields <-> profile ----------

    private void SetSpeed(int? max, int? min)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            int newMax = max ?? (int)_maxBox.Value;
            int newMin = min ?? (int)_minBox.Value;
            // The minimum can never exceed the maximum, or Windows ignores the limit.
            if (max is not null && newMin > newMax) newMin = newMax;
            if (min is not null && newMin > newMax) newMax = newMin;
            newMax = Math.Clamp(newMax, 1, 100);
            newMin = Math.Clamp(newMin, 0, newMax);

            _maxBar.Value = newMax;
            _maxBox.Value = newMax;
            _minBar.Value = newMin;
            _minBox.Value = newMin;
            _warning.Visible = newMax <= LowLimitWarning;
        }
        finally
        {
            _loading = false;
        }
    }

    private void CheckCores(Func<LogicalCpu, bool> predicate)
    {
        foreach (var box in _coreBoxes) box.Checked = predicate((LogicalCpu)box.Tag!);
    }

    private void ShowProfile(Profile profile)
    {
        _loading = true;
        try
        {
            int max = Math.Clamp(profile.MaxPercent, 1, 100);
            int min = Math.Clamp(profile.MinPercent, 0, max);
            _maxBar.Value = max;
            _maxBox.Value = max;
            _minBar.Value = min;
            _minBox.Value = min;
            _warning.Visible = max <= LowLimitWarning;

            // An empty list in the profile means "no restriction": show everything checked.
            bool all = profile.Cores.Count == 0;
            foreach (var box in _coreBoxes)
                box.Checked = all || profile.Cores.Contains(((LogicalCpu)box.Tag!).Index);

            _processes.Items.Clear();
            foreach (var name in profile.Processes) _processes.Items.Add(name);
        }
        finally
        {
            _loading = false;
        }
    }

    private Profile ReadProfile()
    {
        var cores = _coreBoxes.Where(b => b.Checked).Select(b => ((LogicalCpu)b.Tag!).Index).ToList();
        if (cores.Count == _coreBoxes.Count) cores.Clear();

        var profile = new Profile
        {
            Name = _profiles.SelectedItem as string ?? "Unnamed",
            MaxPercent = (int)_maxBox.Value,
            MinPercent = (int)_minBox.Value,
            Cores = cores,
            Processes = _processes.Items.Cast<string>().ToList(),
        };
        profile.Normalize();
        return profile;
    }

    private void ReloadProfileList(string? select)
    {
        _loading = true;
        try
        {
            _profiles.Items.Clear();
            foreach (var profile in _store.Profiles) _profiles.Items.Add(profile.Name);
            int index = select is null ? -1 : _profiles.FindStringExact(select);
            if (index < 0 && _profiles.Items.Count > 0) index = 0;
            _profiles.SelectedIndex = index;
        }
        finally
        {
            _loading = false;
        }
        if (_profiles.SelectedItem is string name && _store.Find(name) is { } selected) ShowProfile(selected);
    }

    // ---------- Profiles ----------

    private void NewProfile()
    {
        var name = Dialogs.AskText(this, "New profile", "Profile name (copies the values currently on screen):");
        if (name is null) return;
        var profile = ReadProfile();
        profile.Name = _store.UniqueName(name);
        _store.Profiles.Add(profile);
        TrySave();
        ReloadProfileList(profile.Name);
        AppendLog($"Profile \"{profile.Name}\" created.");
    }

    private void SaveProfile()
    {
        if (_profiles.SelectedItem is not string name || _store.Find(name) is not { } existing)
        {
            NewProfile();
            return;
        }
        var edited = ReadProfile();
        existing.MaxPercent = edited.MaxPercent;
        existing.MinPercent = edited.MinPercent;
        existing.Cores = edited.Cores;
        existing.Processes = edited.Processes;
        TrySave();
        AppendLog($"Profile \"{existing.Name}\" saved.");
    }

    private void DeleteProfile()
    {
        if (_profiles.SelectedItem is not string name || _store.Find(name) is not { } profile) return;
        var answer = MessageBox.Show(this, $"Delete profile \"{name}\"?", "CPU-C", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        _store.Profiles.Remove(profile);
        TrySave();
        ReloadProfileList(null);
        AppendLog($"Profile \"{name}\" deleted.");
    }

    private void ImportProfiles()
    {
        using var dialog = new OpenFileDialog { Filter = "CPU-C profiles (*.json)|*.json|All files|*.*", Title = "Import profiles" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = _store.Import(dialog.FileName);
            if (imported.Count == 0)
            {
                AppendLog("The file contained no profiles.");
                return;
            }
            ReloadProfileList(imported[0].Name);
            AppendLog($"Imported: {string.Join(", ", imported.Select(p => p.Name))}.");
        }
        catch (Exception ex)
        {
            ShowError("Could not import the file.", ex);
        }
    }

    private void ExportProfile()
    {
        var profile = ReadProfile();
        using var dialog = new SaveFileDialog
        {
            Filter = "CPU-C profiles (*.json)|*.json",
            Title = "Export profile",
            FileName = string.Concat(profile.Name.Split(Path.GetInvalidFileNameChars())) + ".json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ProfileStore.Export(profile, dialog.FileName);
            AppendLog($"Profile exported to {dialog.FileName}.");
        }
        catch (Exception ex)
        {
            ShowError("Could not export the profile.", ex);
        }
    }

    private void TrySave()
    {
        try { _store.Save(); }
        catch (Exception ex) { ShowError("Could not save the profiles.", ex); }
    }

    // ---------- Programs ----------

    private void AddProcessNames(IEnumerable<string> names)
    {
        foreach (var raw in names)
        {
            var name = Profile.CleanProcessName(raw);
            if (name.Length == 0) continue;
            bool exists = _processes.Items.Cast<string>().Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            if (!exists) _processes.Items.Add(name);
        }
    }

    private void AddRunningProcesses() => AddProcessNames(Dialogs.PickProcesses(this));

    private void AddExecutable()
    {
        using var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose program", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddProcessNames(dialog.FileNames.Select(f => Path.GetFileNameWithoutExtension(f)));
    }

    private void AddTypedProcess()
    {
        var name = Dialogs.AskText(this, "Add program", "Process name (e.g. pythonw or game.exe):");
        if (name is not null) AddProcessNames([name]);
    }

    private void RemoveProcesses()
    {
        foreach (var item in _processes.SelectedItems.Cast<string>().ToList())
            _processes.Items.Remove(item);
    }

    // ---------- Apply / restore ----------

    private void ApplyProfile(Profile profile)
    {
        try
        {
            _limiter.Apply(profile);
        }
        catch (Exception ex)
        {
            ShowError("Could not apply the limit.", ex);
        }
        UpdateStatus();
    }

    private void RestoreNormal()
    {
        try
        {
            _limiter.Restore();
        }
        catch (Exception ex)
        {
            ShowError("Could not restore the CPU.", ex);
        }
        UpdateStatus();
    }

    private void OnTimer()
    {
        try
        {
            _limiter.Tick();
        }
        catch (Exception ex)
        {
            AppendLog("Error while watching programs: " + ex.Message);
        }
    }

    private void UpdateStatus()
    {
        string plan;
        try
        {
            var (min, max) = PowerPlan.Current();
            plan = $"Power plan now: minimum {min}%, maximum {max}%.";
        }
        catch (Exception ex)
        {
            plan = "Could not read the power plan: " + ex.Message;
        }

        if (_limiter.ActiveProfile is { } active)
        {
            _status.Text = $"LIMIT ACTIVE — profile \"{active.Name}\". {plan}";
            _status.ForeColor = Color.FromArgb(190, 80, 10);
            _tray.Icon = _iconLimited;
            Icon = _iconLimited;
            _tray.Text = Truncate($"CPU-C: limit active ({active.Name}, max {active.MaxPercent}%)", 63);
        }
        else
        {
            _status.Text = $"Normal, no limit. {plan}";
            _status.ForeColor = Color.FromArgb(30, 120, 70);
            _tray.Icon = _iconNormal;
            Icon = _iconNormal;
            _tray.Text = "CPU-C: normal";
        }
        _restore.Enabled = _limiter.IsActive;
        _apply.Text = _limiter.IsActive ? "Reapply limit" : "Apply limit";
    }

    // ---------- Window, tray and shutdown ----------

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_askAboutLeftover || !_limiter.IsActive) return;

        var answer = MessageBox.Show(this,
            $"A CPU limit was left active last time (profile \"{_limiter.ActiveProfile!.Name}\").\n\nRestore the CPU to normal now?",
            "CPU-C", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes) RestoreNormal();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) Hide();
    }

    private void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the app always returns the CPU to normal (except when restarting as administrator).
        if (_limiter.IsActive && !_skipRestoreOnClose)
        {
            try
            {
                _limiter.Restore();
            }
            catch (Exception ex)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    ShowError("Could not restore the CPU. The limit is still active.", ex);
                    UpdateStatus();
                    e.Cancel = true;
                }
            }
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosed(e);
    }

    private void RestartAsAdmin()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = "--keep",
        };
        Program.ReleaseInstance();
        try
        {
            Process.Start(info);
        }
        catch (Win32Exception)
        {
            // The user declined elevation: carry on as is.
            Program.AcquireInstance();
            return;
        }
        _skipRestoreOnClose = true;
        Close();
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal void SaveScreenshot(string file)
    {
        _tray.Visible = false;
        Show();
        Application.DoEvents();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        _skipRestoreOnClose = true;
        Close();
    }

    // ---------- Utilities ----------

    private void AppendLog(string message)
    {
        if (_log.IsDisposed) return;
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void ShowError(string what, Exception ex)
    {
        AppendLog($"{what} {ex.Message}");
        MessageBox.Show(this, $"{what}\n\n{ex.Message}", "CPU-C", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    private static Icon MakeIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 19F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("C", font, Brushes.White, new RectangleF(0, 0, 32, 31), format);
        }
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
