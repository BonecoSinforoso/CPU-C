using System.Diagnostics;

namespace CpuC.Ui;

/// <summary>Small helper windows: ask for a text and pick running programs.</summary>
internal static class Dialogs
{
    public static string? AskText(IWin32Window owner, string title, string label, string initial = "")
    {
        // Layout stays suspended until the end so DPI scaling applies to every control.
        using var form = new Form();
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
        var text = new Label { Text = label, AutoSize = true, Location = new Point(12, 12) };
        var box = new TextBox { Text = initial, Location = new Point(12, 36), Width = 316 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(172, 72), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(253, 72), Width = 75 };
        form.Controls.AddRange([text, box, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.ResumeLayout(false);
        form.PerformLayout();

        if (form.ShowDialog(owner) != DialogResult.OK) return null;
        var value = box.Text.Trim();
        return value.Length == 0 ? null : value;
    }

    private sealed record ProcessItem(string Name, string Title)
    {
        public override string ToString() => Title.Length == 0 ? Name : $"{Name}  —  {Title}";
    }

    /// <summary>Lists running programs (those with a window first) and returns the chosen names.</summary>
    public static List<string> PickProcesses(IWin32Window owner)
    {
        var items = new Dictionary<string, ProcessItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4) continue;
                    var title = process.MainWindowTitle ?? "";
                    if (!items.TryGetValue(process.ProcessName, out var existing) || (existing.Title.Length == 0 && title.Length > 0))
                        items[process.ProcessName] = new ProcessItem(process.ProcessName, title);
                }
                catch
                {
                    // The process exited while we were listing.
                }
            }
        }
        var all = items.Values
            .OrderBy(i => i.Title.Length == 0)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var form = new Form();
        form.SuspendLayout();
        form.Text = "Running programs";
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

        var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Filter…" };
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.MultiExtended,
            IntegralHeight = false,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.AddRange([cancel, ok]);

        void Refill()
        {
            list.BeginUpdate();
            list.Items.Clear();
            var term = filter.Text.Trim();
            foreach (var item in all)
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
