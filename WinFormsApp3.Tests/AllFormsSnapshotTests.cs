namespace SkyWings.Tests;

/// <summary>Renders every form to a PNG (default and minimum size) so the look can be reviewed, and checks nothing sticks out of the window.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AllFormsSnapshotTests
{
    /// <summary>Forms that clip content today; each is removed by the task that restyles it (the set must be empty at the end).</summary>
    private static readonly HashSet<string> KnownNotFitting = new();

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
                if (!KnownNotFitting.Contains(name))
                    problems.AddRange(FormHarness.Clipped(form).Select(c => $"{name}-{size}: {c}"));
            }
        }
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    });
}
