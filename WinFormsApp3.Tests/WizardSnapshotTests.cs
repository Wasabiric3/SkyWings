namespace SkyWings.Tests;

/// <summary>Renders each wizard step to a PNG so the look can be compared with the Figma by eye (see the folder name below).</summary>
[TestClass]
[DoNotParallelize]
public sealed class WizardSnapshotTests
{
    public static string Folder => Path.Combine(Path.GetTempPath(), "skywings-wizard-snapshots");

    [TestMethod]
    public void WizardSnapshots_WriteSixNonBlankImages() => WizardHarness.OnStaThread(() =>
    {
        Directory.CreateDirectory(Folder);
        using var wizard = new WizardHarness();
        wizard.AddTraveller("Brisias G. Labuson");
        for (int step = 0; step <= 5; step++)
        {
            if (step == 3) wizard.Call("PrepareSeatStep");
            wizard.GoTo(step);
            using Bitmap bmp = wizard.Snapshot();
            var colours = new HashSet<int>();
            for (int y = 0; y < bmp.Height; y += 7)
                for (int x = 0; x < bmp.Width; x += 7) colours.Add(bmp.GetPixel(x, y).ToArgb());
            Assert.IsGreaterThan(5, colours.Count, $"step {step + 1} is not blank");
            bmp.Save(Path.Combine(Folder, $"step{step + 1}.png"));
        }
    });
}
