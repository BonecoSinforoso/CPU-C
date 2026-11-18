using System.Diagnostics;
using CpuC.Localization;

namespace CpuC.Ui;

/// <summary>Small helper windows: ask for a text and pick running programs.</summary>
internal static class Dialogs
{
    public static string? AskText(IWin32Window owner, string title, string label, string initial = "")
    {
        // Layout stays suspended until the end so DPI scaling applies to every control.
        using Form form = new();
        form.SuspendLayout();
        form.Text = title;
        form.Font = new Font("Segoe UI", 9F);
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterParent;
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.ShowInTaskbar = false;
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = new Size(340, 110);
        Label text = new() { Text = label, AutoSize = true, Location = new Point(12, 12) };
        TextBox box = new() { Text = initial, Location = new Point(12, 36), Width = 316 };
        Button ok = new() { Text = Lang.T.Ok, DialogResult = DialogResult.OK, Location = new Point(172, 72), Width = 75 };
        Button cancel = new() { Text = Lang.T.Cancel, DialogResult = DialogResult.Cancel, Location = new Point(253, 72), Width = 75 };
        form.Controls.AddRange([text, box, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.ResumeLayout(false);
        form.PerformLayout();

        if (form.ShowDialog(owner) != DialogResult.OK) return null;
        string value = box.Text.Trim();
        return value.Length == 0 ? null : value;
    }

    private sealed record ProcessItem(string Name, string Title)
    {
        public override string ToString() => Title.Length == 0 ? Name : $"{Name}  —  {Title}";
    }

    /// <summary>Lists running programs (those with a window first) and returns the chosen names.</summary>
    public static List<string> PickProcesses(IWin32Window owner)
    {
        Dictionary<string, ProcessItem> items = new(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4) continue;
                    string title = process.MainWindowTitle ?? "";
                    if (!items.TryGetValue(process.ProcessName, out ProcessItem? existing) || (existing.Title.Length == 0 && title.Length > 0))
                        items[process.ProcessName] = new ProcessItem(process.ProcessName, title);
                }
                catch
                {
                    // The process exited while we were listing.
                }
            }
        }
        List<ProcessItem> all = items.Values
            .OrderBy(i => i.Title.Length == 0)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using Form form = new();
        form.SuspendLayout();
        form.Text = Lang.T.RunningProgramsTitle;
        form.Font = new Font("Segoe UI", 9F);
        form.StartPosition = FormStartPosition.CenterParent;
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.ShowInTaskbar = false;
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = new Size(460, 440);
        form.MinimumSize = new Size(360, 300);
        form.Padding = new Padding(10);

        TextBox filter = new() { Dock = DockStyle.Top, PlaceholderText = Lang.T.FilterPlaceholder };
        ListBox list = new()
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.MultiExtended,
            IntegralHeight = false,
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        Button cancel = new() { Text = Lang.T.Cancel, DialogResult = DialogResult.Cancel, AutoSize = true };
        Button ok = new() { Text = Lang.T.Add, DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.AddRange([cancel, ok]);

        void Refill()
        {
            list.BeginUpdate();
            list.Items.Clear();
            string term = filter.Text.Trim();
            foreach (ProcessItem item in all)
                if (term.Length == 0 || item.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                    list.Items.Add(item);
            list.EndUpdate();
        }
        filter.TextChanged += (_, _) => Refill();
        list.DoubleClick += (_, _) => { if (list.SelectedItem is not null) form.DialogResult = DialogResult.OK; };
        Refill();

        // Docking follows insertion order: the last control added docks first.
        form.Controls.Add(list);
        form.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        form.Controls.Add(filter);
        form.Controls.Add(buttons);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.ResumeLayout(false);
        form.PerformLayout();

        return form.ShowDialog(owner) == DialogResult.OK
            ? list.SelectedItems.Cast<ProcessItem>().Select(i => i.Name).ToList()
            : [];
    }
}
