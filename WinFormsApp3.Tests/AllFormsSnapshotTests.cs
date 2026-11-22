namespace SkyWings.Tests;

/// <summary>Renders every form to a PNG (default and minimum size) so the look can be reviewed, and checks nothing sticks out of the window.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AllFormsSnapshotTests
{

    [TestMethod]
    public void EveryForm_RendersNonBlank_AtDefaultAndMinimumSize() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var problems = new List<string>();
        foreach ((string name, Func<Form> create) in harness.Forms().Concat(harness.BookingForms()))
        {
            using Form form = create();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(20, 20);
            form.Show();
            Application.DoEvents();
            foreach ((string size, Size target) in new[] { ("default", form.Size), ("min", form.MinimumSize) }.Where(s => s.Item2 != Size.Empty))
            {
                form.Size = target;
                Application.DoEvents();
                using Bitmap bmp = FormHarness.Snapshot(form);
                if (!FormHarness.IsDrawn(bmp)) problems.Add($"{name}-{size} is blank");
                Directory.CreateDirectory(FormHarness.Folder);
                bmp.Save(Path.Combine(FormHarness.Folder, $"{name}-{size}.png"));
                problems.AddRange(FormHarness.Clipped(form).Select(c => $"{name}-{size}: {c}"));
            }
        }
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    });

    /// <summary>Every admin tab, opened by its section button, at the default and the minimum window size.</summary>
    [TestMethod]
    public void EveryAdminTab_FitsAtDefaultAndMinimumSize() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        harness.SampleBooking();
        var problems = new List<string>();
        using var form = new AdminForm(harness.Admin) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        form.Show();
        Application.DoEvents();
        foreach (string tab in new[] { "Flights", "Bookings", "Refunds", "Waitlist", "Reports", "Staff", "Audit", "Database" })
        {
            Control? button = null;
            void Find(Control root) { foreach (Control c in root.Controls) { if (c is UI.GlassButton b && b.Text.StartsWith(tab, StringComparison.Ordinal) && b.Parent is FlowLayoutPanel) button ??= b; Find(c); } }
            Find(form);
            Assert.IsNotNull(button, tab);
            ((UI.GlassButton)button).PerformClick();
            Application.DoEvents();
            foreach ((string size, Size target) in new[] { ("default", form.Size), ("min", form.MinimumSize) })
            {
                form.Size = target;
                Application.DoEvents();
                using Bitmap bmp = FormHarness.Snapshot(form);
                Directory.CreateDirectory(FormHarness.Folder);
                bmp.Save(Path.Combine(FormHarness.Folder, $"admin-tab-{tab.ToLowerInvariant()}-{size}.png"));
                problems.AddRange(FormHarness.Clipped(form).Select(c => $"{tab}-{size}: {c}"));
            }
            form.Size = new Size(1120, 640);
        }
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    });
}
