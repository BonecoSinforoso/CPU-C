using CpuC.Core;
using CpuC.Localization;

namespace CpuC.Ui;

/// <summary>Settings window: language and theme.</summary>
internal static class SettingsDialog
{
    private sealed record ThemeItem(AppTheme Theme, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>Shows the window. Returns the chosen language and theme, or null if cancelled.</summary>
    public static (Strings Language, AppTheme Theme)? Show(IWin32Window owner, AppTheme currentTheme)
    {
        // Layout stays suspended until the end so DPI scaling applies to every control.
        using Form form = new();
        form.SuspendLayout();
        form.Text = Lang.T.SettingsTitle;
        form.Font = new Font("Segoe UI", 9F);
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.ShowInTaskbar = false;
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoSize = true;
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        form.Padding = new Padding(10);

        ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        foreach (Strings option in Lang.All) language.Items.Add(option);
        language.SelectedItem = Lang.T;

        ComboBox theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        theme.Items.AddRange(
        [
            new ThemeItem(AppTheme.System, Lang.T.ThemeSystem),
            new ThemeItem(AppTheme.Light, Lang.T.ThemeLight),
            new ThemeItem(AppTheme.Dark, Lang.T.ThemeDark),
        ]);
        theme.SelectedItem = theme.Items.Cast<ThemeItem>().First(i => i.Theme == currentTheme);

        TableLayoutPanel fields = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.Controls.Add(new Label { Text = Lang.T.LanguageLabel, AutoSize = true, Margin = new Padding(3, 7, 8, 0) }, 0, 0);
        fields.Controls.Add(language, 1, 0);
        fields.Controls.Add(new Label { Text = Lang.T.ThemeLabel, AutoSize = true, Margin = new Padding(3, 7, 8, 0) }, 0, 1);
        fields.Controls.Add(theme, 1, 1);

        GroupBox appearance = new()
        {
            Text = Lang.T.AppearanceGroup,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 8),
        };
        appearance.Controls.Add(fields);

        FlowLayoutPanel buttons = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0),
        };
        Button cancel = new() { Text = Lang.T.Cancel, DialogResult = DialogResult.Cancel, Width = 85 };
        Button ok = new() { Text = Lang.T.Ok, DialogResult = DialogResult.OK, Width = 85 };
        buttons.Controls.AddRange([cancel, ok]);

        TableLayoutPanel root = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1 };
        root.Controls.Add(appearance, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        form.Controls.Add(root);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.ResumeLayout(false);
        form.PerformLayout();

        if (form.ShowDialog(owner) != DialogResult.OK) return null;
        return ((Strings)language.SelectedItem!, ((ThemeItem)theme.SelectedItem!).Theme);
    }
}
