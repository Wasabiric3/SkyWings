using SkyWings;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class ThemeTests
{
    private static void AssertRgb(Color actual, int rgb, string name)
        => Assert.AreEqual(rgb, actual.ToArgb() & 0xFFFFFF, $"{name} was #{actual.ToArgb() & 0xFFFFFF:X6}");

    [TestMethod]
    public void Palette_MatchesBrandKit()
    {
        AssertRgb(Theme.Primary, 0x527866, nameof(Theme.Primary));
        AssertRgb(Theme.Accent, 0x315B49, nameof(Theme.Accent));
        AssertRgb(Theme.AccentDark, 0x234739, nameof(Theme.AccentDark));
        AssertRgb(Theme.SoftFill, 0xDAECE1, nameof(Theme.SoftFill));
        AssertRgb(Theme.TextPrimary, 0x171C1A, nameof(Theme.TextPrimary));
        AssertRgb(Theme.TextSecondary, 0x77857E, nameof(Theme.TextSecondary));
        AssertRgb(Theme.BgMain, 0xF3F7F4, nameof(Theme.BgMain));
        AssertRgb(Theme.Border, 0xE1E8E4, nameof(Theme.Border));
        AssertRgb(Theme.BorderLight, 0xDFE7E1, nameof(Theme.BorderLight));
    }

    [TestMethod]
    public void Apply_DoesNotMakePanelsLabelsTransparent()
    {
        using var form = new Form();
        var panel = new Panel();
        var label = new Label();
        var check = new CheckBox();
        var radio = new RadioButton();
        panel.Controls.AddRange(new Control[] { label, check, radio });
        form.Controls.Add(panel);

        Theme.Apply(form);

        foreach (Control c in new Control[] { panel, label, check, radio })
            Assert.AreNotEqual(Color.Transparent.ToArgb(), c.BackColor.ToArgb(), $"{c.GetType().Name} is transparent");
        Assert.AreEqual(form.BackColor.ToArgb(), panel.BackColor.ToArgb());
        Assert.AreEqual(panel.BackColor.ToArgb(), label.BackColor.ToArgb());
    }

    [TestMethod]
    public void Apply_ControlsInsideCardTakeCardColour()
    {
        using var form = new Form();
        var card = new Panel { BackColor = Theme.BgCard };
        var label = new Label();
        card.Controls.Add(label);
        form.Controls.Add(card);

        Theme.Apply(card);   // card is not a Form/TabPage, so its own colour must be kept as the surface

        Assert.AreEqual(Theme.BgCard.ToArgb(), label.BackColor.ToArgb());
    }

    [TestMethod]
    public void Apply_TabPageTakesBgMain()
    {
        using var tabs = new TabControl();
        var page = new TabPage();
        tabs.TabPages.Add(page);

        Theme.Apply(tabs);

        Assert.AreEqual(Theme.BgMain.ToArgb(), page.BackColor.ToArgb());
    }

    private sealed class SelfStyledPanel : Panel, ISelfStyled { }

    [TestMethod]
    public void Apply_SkipsSelfStyledChildren()
    {
        using var form = new Form();
        var own = new SelfStyledPanel { BackColor = Color.Red, ForeColor = Color.Blue };
        form.Controls.Add(own);

        Theme.Apply(form);

        Assert.AreEqual(Color.Red.ToArgb(), own.BackColor.ToArgb());
        Assert.AreEqual(Color.Blue.ToArgb(), own.ForeColor.ToArgb());
    }
}
