using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class GlassCheckBoxTests
{
    private static Bitmap Render(GlassCheckBox box)
    {
        box.CreateControl();
        var bmp = new Bitmap(Math.Max(1, box.Width), Math.Max(1, box.Height));
        box.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }

    private static Point BoxCentre(GlassCheckBox box) => new(Gfx.S(box, 2) + Gfx.S(box, 9), box.Height / 2);

    [TestMethod]
    public void Checked_PaintsBrandBox()
    {
        using var box = new GlassCheckBox { Text = "Pet in cabin", Checked = true, AutoSize = false, Width = 200, Height = 28 };
        using Bitmap bmp = Render(box);
        // Left of the centre, so the white tick does not sit on the sampled pixel.
        Assert.AreEqual(Theme.Brand.ToArgb(), bmp.GetPixel(Gfx.S(box, 5), box.Height / 2).ToArgb());
    }

    [TestMethod]
    public void Unchecked_PaintsWhiteBox()
    {
        using var box = new GlassCheckBox { Text = "Pet in cabin", Checked = false, AutoSize = false, Width = 200, Height = 28 };
        using Bitmap bmp = Render(box);
        Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(BoxCentre(box).X, BoxCentre(box).Y).ToArgb());
    }

    [TestMethod]
    public void Click_TogglesChecked_AndRaisesCheckedChanged()
    {
        using var box = new GlassCheckBox { Text = "Senior" };
        int raised = 0;
        box.CheckedChanged += (_, _) => raised++;
        box.CreateControl();
        // A click on a CheckBox toggles it in OnClick (protected), the same path the mouse and Space take.
        typeof(CheckBox).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(box, new object[] { EventArgs.Empty });
        Assert.IsTrue(box.Checked);
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void ItIsATabStop_AndStyledItself()
    {
        using var box = new GlassCheckBox();
        Assert.IsTrue(box.TabStop);
        Assert.IsInstanceOfType<ISelfStyled>(box);
    }
}
