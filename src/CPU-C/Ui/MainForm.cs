using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Principal;
using CpuC.Core;
using CpuC.Localization;

namespace CpuC.Ui;

public sealed class MainForm : Form
{
    private const int LowLimitWarning = 10;

    private readonly CpuTopology _topology = CpuTopology.Detect();
    private readonly ProfileStore _store;
    private readonly Limiter _limiter;
    private readonly bool _askAboutLeftover;

    private readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly TrackBar _maxBar = NewBar(1);
    private readonly TrackBar _minBar = NewBar(0);
    private readonly NumericUpDown _maxBox = NewPercentBox(1);
    private readonly NumericUpDown _minBox = NewPercentBox(0);
    private readonly Label _warning = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(176, 92, 0),
        Visible = false,
    };
    private readonly List<CheckBox> _coreBoxes = [];
    private readonly ListBox _processes = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 4) };
    private readonly Button _apply = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
    private readonly Button _restore = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
    private readonly Button _admin = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Window,
    };
    private readonly ToolTip _tips = new();

    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private readonly Icon _iconNormal = MakeIcon(Color.FromArgb(46, 139, 87));
    private readonly Icon _iconLimited = MakeIcon(Color.FromArgb(214, 96, 20));

    /// <summary>Puts the current language's text on each control; rerun when the language changes.</summary>
    private readonly List<Action> _textUpdaters = [];

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
        Font = new Font("Segoe UI", 9F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 740);
        MinimumSize = new Size(600, 660);
        StartPosition = FormStartPosition.CenterScreen;
        _status.Font = new Font(Font, FontStyle.Bold);
        _apply.Font = new Font(Font, FontStyle.Bold);

        Localize(() => Text = Lang.T.WindowTitle);
        Localize(_warning, s => s.LowLimitWarning(LowLimitWarning));
        Localize(_restore, s => s.RestoreNormal);
        Localize(_admin, s => s.RestartAsAdmin);
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

    // ---------- Language ----------

    /// <summary>Runs <paramref name="update"/> now and again on every language change.</summary>
    private void Localize(Action update)
    {
        _textUpdaters.Add(update);
        update();
    }

    private T Localize<T>(T control, Func<Strings, string> text) where T : Control
    {
        Localize(() => control.Text = text(Lang.T));
        return control;
    }

    private void ChangeLanguage(Strings language)
    {
        if (language == Lang.T) return;
        Lang.Set(language, save: true);
        SuspendLayout();
        foreach (Action update in _textUpdaters) update();
        UpdateStatus();
        ResumeLayout(true);
    }

    // ---------- Building the window ----------

    private void BuildLayout()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
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
        FlowLayoutPanel bar = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        bar.Controls.Add(Localize(new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, s => s.ProfileLabel));
        bar.Controls.Add(_profiles);
        bar.Controls.Add(SmallButton(s => s.New, (_, _) => NewProfile()));
        bar.Controls.Add(SmallButton(s => s.Save, (_, _) => SaveProfile()));
        bar.Controls.Add(SmallButton(s => s.Delete, (_, _) => DeleteProfile()));
        bar.Controls.Add(SmallButton(s => s.Import, (_, _) => ImportProfiles()));
        bar.Controls.Add(SmallButton(s => s.Export, (_, _) => ExportProfile()));

        _profiles.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _profiles.SelectedItem is string name && _store.Find(name) is { } profile)
                ShowProfile(profile);
        };
        return bar;
    }

    private Control BuildSpeedGroup()
    {
        TableLayoutPanel table = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddSpeedRow(table, 0, s => s.Maximum, _maxBar, _maxBox);
        AddSpeedRow(table, 1, s => s.Minimum, _minBar, _minBox);
        table.Controls.Add(_warning, 0, 2);
        table.SetColumnSpan(_warning, 4);

        _maxBar.ValueChanged += (_, _) => SetSpeed(max: _maxBar.Value, min: null);
        _maxBox.ValueChanged += (_, _) => SetSpeed(max: (int)_maxBox.Value, min: null);
        _minBar.ValueChanged += (_, _) => SetSpeed(max: null, min: _minBar.Value);
        _minBox.ValueChanged += (_, _) => SetSpeed(max: null, min: (int)_minBox.Value);

        return Group(s => s.SpeedGroup, table);
    }

    private void AddSpeedRow(TableLayoutPanel table, int row, Func<Strings, string> text, TrackBar bar, NumericUpDown box)
    {
        table.Controls.Add(Localize(new Label { AutoSize = true, Margin = new Padding(3, 8, 8, 0) }, text), 0, row);
        table.Controls.Add(bar, 1, row);
        table.Controls.Add(box, 2, row);
        table.Controls.Add(new Label { Text = "%", AutoSize = true, Margin = new Padding(0, 8, 3, 0) }, 3, row);
    }

    private Control BuildCoresGroup()
    {
        const int columns = 10;
        TableLayoutPanel grid = new() { AutoSize = true, ColumnCount = columns, Margin = new Padding(0) };
        for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        foreach (LogicalCpu cpu in _topology.Cpus)
        {
            CheckBox box = new()
            {
                Text = cpu.KindLabel.Length == 0 ? cpu.Index.ToString() : $"{cpu.Index} {cpu.KindLabel}",
                AutoSize = true,
                Tag = cpu,
                Margin = new Padding(3, 2, 8, 2),
            };
            Localize(() =>
            {
                string kind = cpu.Kind switch
                {
                    CoreKind.Performance => Lang.T.CoreKindFast,
                    CoreKind.Efficiency => Lang.T.CoreKindSlow,
                    _ => Lang.T.CoreKindNormal,
                };
                _tips.SetToolTip(box, Lang.T.CoreTooltip(cpu.Index, kind, cpu.PhysicalCore));
            });
            _coreBoxes.Add(box);
            grid.Controls.Add(box);
        }

        FlowLayoutPanel shortcuts = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        shortcuts.Controls.Add(SmallButton(s => s.AllCores, (_, _) => CheckCores(_ => true)));
        if (_topology.IsHybrid)
        {
            shortcuts.Controls.Add(SmallButton(s => s.OnlyPCores, (_, _) => CheckCores(c => c.Kind == CoreKind.Performance)));
            shortcuts.Controls.Add(SmallButton(s => s.OnlyECores, (_, _) => CheckCores(c => c.Kind == CoreKind.Efficiency)));
        }
        shortcuts.Controls.Add(SmallButton(s => s.NoCores, (_, _) => CheckCores(_ => false)));

        Label hint = Localize(new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 4, 3, 0),
            MaximumSize = new Size(570, 0),
        }, s => s.CoresHint);

        FlowLayoutPanel stack = new() { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        stack.Controls.Add(grid);
        stack.Controls.Add(shortcuts);
        stack.Controls.Add(hint);
        return Group(s => s.CoresGroup, stack);
    }

    private Control BuildProcessGroup()
    {
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        buttons.Controls.Add(WideButton(s => s.AddRunning, (_, _) => AddRunningProcesses()));
        buttons.Controls.Add(WideButton(s => s.AddExe, (_, _) => AddExecutable()));
        buttons.Controls.Add(WideButton(s => s.TypeName, (_, _) => AddTypedProcess()));
        buttons.Controls.Add(WideButton(s => s.Remove, (_, _) => RemoveProcesses()));

        table.Controls.Add(_processes, 0, 0);
        table.Controls.Add(buttons, 1, 0);

        GroupBox group = Localize(new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8) }, s => s.ProgramsGroup);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildActionBar()
    {
        FlowLayoutPanel buttons = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.Add(_apply);
        buttons.Controls.Add(_restore);
        if (!IsElevated()) buttons.Controls.Add(_admin);

        FlowLayoutPanel language = new() { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        language.Controls.Add(Localize(new Label { AutoSize = true, Margin = new Padding(3, 7, 0, 0) }, s => s.LanguageLabel));
        language.Controls.Add(_language);
        foreach (Strings option in Lang.All) _language.Items.Add(option);
        _language.SelectedItem = Lang.T;
        _language.SelectedIndexChanged += (_, _) =>
        {
            if (_language.SelectedItem is Strings chosen) ChangeLanguage(chosen);
        };

        TableLayoutPanel bar = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 6) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(buttons, 0, 0);
        bar.Controls.Add(language, 1, 0);

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

        // Rebuilt on every opening, so it always has the current profiles and language.
        _trayMenu.Opening += (_, _) =>
        {
            _trayMenu.Items.Clear();
            _trayMenu.Items.Add(Lang.T.TrayOpen, null, (_, _) => ShowWindow());
            _trayMenu.Items.Add(new ToolStripSeparator());
            foreach (Profile profile in _store.Profiles)
            {
                ToolStripMenuItem item = new(Lang.T.TrayApply(profile.Name))
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
            _trayMenu.Items.Add(new ToolStripMenuItem(Lang.T.RestoreNormal, null, (_, _) => RestoreNormal()) { Enabled = _limiter.IsActive });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(Lang.T.TrayExit, null, (_, _) => Close());
        };
    }

    private GroupBox Group(Func<Strings, string> title, Control content)
    {
        GroupBox group = Localize(new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 8),
        }, title);
        group.Controls.Add(content);
        return group;
    }

    private Button SmallButton(Func<Strings, string> text, EventHandler onClick)
    {
        Button button = Localize(new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 1, 4, 1) }, text);
        button.Click += onClick;
        return button;
    }

    private Button WideButton(Func<Strings, string> text, EventHandler onClick)
    {
        Button button = Localize(new Button { Width = 140, Height = 28 }, text);
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
        foreach (CheckBox box in _coreBoxes) box.Checked = predicate((LogicalCpu)box.Tag!);
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
            foreach (CheckBox box in _coreBoxes)
                box.Checked = all || profile.Cores.Contains(((LogicalCpu)box.Tag!).Index);

            _processes.Items.Clear();
            foreach (string name in profile.Processes) _processes.Items.Add(name);
        }
        finally
        {
            _loading = false;
        }
    }

    private Profile ReadProfile()
    {
        List<int> cores = _coreBoxes.Where(b => b.Checked).Select(b => ((LogicalCpu)b.Tag!).Index).ToList();
        if (cores.Count == _coreBoxes.Count) cores.Clear();

        Profile profile = new()
        {
            Name = _profiles.SelectedItem as string ?? Lang.T.UnnamedProfile,
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
            foreach (Profile profile in _store.Profiles) _profiles.Items.Add(profile.Name);
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
        string? name = Dialogs.AskText(this, Lang.T.NewProfileTitle, Lang.T.NewProfilePrompt);
        if (name is null) return;
        Profile profile = ReadProfile();
        profile.Name = _store.UniqueName(name);
        _store.Profiles.Add(profile);
        TrySave();
        ReloadProfileList(profile.Name);
        AppendLog(Lang.T.ProfileCreated(profile.Name));
    }

    private void SaveProfile()
    {
        if (_profiles.SelectedItem is not string name || _store.Find(name) is not { } existing)
        {
            NewProfile();
            return;
        }
        Profile edited = ReadProfile();
        existing.MaxPercent = edited.MaxPercent;
        existing.MinPercent = edited.MinPercent;
        existing.Cores = edited.Cores;
        existing.Processes = edited.Processes;
        TrySave();
        AppendLog(Lang.T.ProfileSaved(existing.Name));
    }

    private void DeleteProfile()
    {
        if (_profiles.SelectedItem is not string name || _store.Find(name) is not { } profile) return;
        DialogResult answer = MessageBox.Show(this, Lang.T.DeleteProfileQuestion(name), "CPU-C", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        _store.Profiles.Remove(profile);
        TrySave();
        ReloadProfileList(null);
        AppendLog(Lang.T.ProfileDeleted(name));
    }

    private void ImportProfiles()
    {
        using OpenFileDialog dialog = new() { Filter = Lang.T.ProfilesFilterWithAll, Title = Lang.T.ImportTitle };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            List<Profile> imported = _store.Import(dialog.FileName);
            if (imported.Count == 0)
            {
                AppendLog(Lang.T.ImportEmpty);
                return;
            }
            ReloadProfileList(imported[0].Name);
            AppendLog(Lang.T.Imported(string.Join(", ", imported.Select(p => p.Name))));
        }
        catch (Exception ex)
        {
            ShowError(Lang.T.ImportFailed, ex);
        }
    }

    private void ExportProfile()
    {
        Profile profile = ReadProfile();
        using SaveFileDialog dialog = new()
        {
            Filter = Lang.T.ProfilesFilter,
            Title = Lang.T.ExportTitle,
            FileName = string.Concat(profile.Name.Split(Path.GetInvalidFileNameChars())) + ".json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ProfileStore.Export(profile, dialog.FileName);
            AppendLog(Lang.T.Exported(dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowError(Lang.T.ExportFailed, ex);
        }
    }

    private void TrySave()
    {
        try { _store.Save(); }
        catch (Exception ex) { ShowError(Lang.T.SaveProfilesFailed, ex); }
    }

    // ---------- Programs ----------

    private void AddProcessNames(IEnumerable<string> names)
    {
        foreach (string raw in names)
        {
            string name = Profile.CleanProcessName(raw);
            if (name.Length == 0) continue;
            bool exists = _processes.Items.Cast<string>().Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            if (!exists) _processes.Items.Add(name);
        }
    }

    private void AddRunningProcesses() => AddProcessNames(Dialogs.PickProcesses(this));

    private void AddExecutable()
    {
        using OpenFileDialog dialog = new() { Filter = Lang.T.ExeFilter, Title = Lang.T.ChooseProgramTitle, Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddProcessNames(dialog.FileNames.Select(f => Path.GetFileNameWithoutExtension(f)));
    }

    private void AddTypedProcess()
    {
        string? name = Dialogs.AskText(this, Lang.T.AddProgramTitle, Lang.T.AddProgramPrompt);
        if (name is not null) AddProcessNames([name]);
    }

    private void RemoveProcesses()
    {
        foreach (string item in _processes.SelectedItems.Cast<string>().ToList())
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
            ShowError(Lang.T.ApplyFailed, ex);
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
            ShowError(Lang.T.RestoreFailed, ex);
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
            AppendLog(Lang.T.WatchError(ex.Message));
        }
    }

    private void UpdateStatus()
    {
        string plan;
        try
        {
            (int min, int max) = PowerPlan.Current();
            plan = Lang.T.PowerPlanNow(min, max);
        }
        catch (Exception ex)
        {
            plan = Lang.T.PowerPlanReadFailed(ex.Message);
        }

        if (_limiter.ActiveProfile is { } active)
        {
            _status.Text = Lang.T.StatusLimitActive(active.Name, plan);
            _status.ForeColor = Color.FromArgb(190, 80, 10);
            _tray.Icon = _iconLimited;
            Icon = _iconLimited;
            _tray.Text = Truncate(Lang.T.TrayLimitActive(active.Name, active.MaxPercent), 63);
        }
        else
        {
            _status.Text = Lang.T.StatusNormal(plan);
            _status.ForeColor = Color.FromArgb(30, 120, 70);
            _tray.Icon = _iconNormal;
            Icon = _iconNormal;
            _tray.Text = Lang.T.TrayNormal;
        }
        _restore.Enabled = _limiter.IsActive;
        _apply.Text = _limiter.IsActive ? Lang.T.ReapplyLimit : Lang.T.ApplyLimit;
    }

    // ---------- Window, tray and shutdown ----------

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_askAboutLeftover || !_limiter.IsActive) return;

        DialogResult answer = MessageBox.Show(this,
            Lang.T.LeftoverQuestion(_limiter.ActiveProfile!.Name),
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
                    ShowError(Lang.T.RestoreFailedStillActive, ex);
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
        ProcessStartInfo info = new(Environment.ProcessPath!)
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
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal void SaveScreenshot(string file)
    {
        _tray.Visible = false;
        Show();
        Application.DoEvents();
        using Bitmap bitmap = new(Width, Height);
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
        ReadOnlySpan<int> sizes = [16, 24, 32, 48, 64, 256];
        byte[][] payloads = new byte[sizes.Length][];

        using FontFamily family = new("Segoe UI");
        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            using Bitmap bitmap = new(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float margin = size * (1f / 32f);
                float diameter = size - 2f * margin;
                using SolidBrush brush = new(color);
                g.FillEllipse(brush, margin, margin, diameter, diameter);

                using GraphicsPath path = new();
                using Matrix matrix = new();
                path.AddString("C", family, (int)FontStyle.Bold, size * (19f / 32f), PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bounds = path.GetBounds();
                matrix.Translate((size - bounds.Width) / 2f - bounds.Left, (size - bounds.Height) / 2f - bounds.Top);
                path.Transform(matrix);
                g.FillPath(Brushes.White, path);
            }

            if (size >= 256)
            {
                using MemoryStream png = new();
                bitmap.Save(png, ImageFormat.Png);
                payloads[i] = png.ToArray();
            }
            else
            {
                payloads[i] = EncodeIconDib(bitmap);
            }
        }

        using MemoryStream ico = new();
        using (BinaryWriter bw = new(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)payloads[i].Length);
                bw.Write((uint)offset);
                offset += payloads[i].Length;
            }
            for (int i = 0; i < sizes.Length; i++)
                bw.Write(payloads[i]);
        }

        ico.Position = 0;
        return new Icon(ico);
    }

    private static byte[] EncodeIconDib(Bitmap bitmap)
    {
        int w = bitmap.Width;
        int h = bitmap.Height;
        int xorStride = w * 4;
        int andStride = ((w + 31) / 32) * 4;

        using MemoryStream ms = new();
        using BinaryWriter bw = new(ms);
        bw.Write(40u);
        bw.Write(w);
        bw.Write(h * 2);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0u);
        bw.Write((uint)((xorStride + andStride) * h));
        bw.Write(0);
        bw.Write(0);
        bw.Write(0u);
        bw.Write(0u);

        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[xorStride];
            for (int y = h - 1; y >= 0; y--)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, xorStride);
                bw.Write(row);
            }
            byte[] maskRow = new byte[andStride];
            for (int y = h - 1; y >= 0; y--)
            {
                Array.Clear(maskRow);
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, xorStride);
                for (int x = 0; x < w; x++)
                {
                    if (row[x * 4 + 3] == 0)
                        maskRow[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
                bw.Write(maskRow);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return ms.ToArray();
    }
}
