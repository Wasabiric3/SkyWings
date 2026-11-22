using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The Figma look's rounded input field: a rounded host around a native TextBox, ComboBox or DateTimePicker.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RoundedInputTests
{
    private static void OnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AssertFailedException(error.Message, error);
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    private static Bitmap Render(Control control)
    {
        control.CreateControl();   // owner-drawn combo boxes and date pickers only reach their final height with a handle
        Application.DoEvents();
        var bmp = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bmp, new Rectangle(0, 0, control.Width, control.Height));
        return bmp;
    }

    [TestMethod]
    public void TextBox_IsBorderless_AndCornerShowsSurface() => OnStaThread(() =>
    {
        var box = new TextBox();
        using var host = new RoundedInput(box) { Width = 260 };
        using Bitmap bmp = Render(host);
        Assert.AreEqual(BorderStyle.None, box.BorderStyle);
        Assert.AreEqual(Theme.BgMain.ToArgb(), bmp.GetPixel(1, 1).ToArgb(), "the rounded corner shows the surface");
        Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(30, host.Height / 2).ToArgb(), "the field is white inside");
    });

    [TestMethod]
    public void ComboBox_NativeBorderAndArrowButtonAreHidden_AndAChevronIsPainted() => OnStaThread(() =>
    {
        using var host = new RoundedInput(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }) { Width = 260 };
        using Bitmap bmp = Render(host);
        Assert.AreEqual(Theme.BgMain.ToArgb(), bmp.GetPixel(1, 1).ToArgb(), "rounded corner");
        Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(40, 3).ToArgb(), "no native border line under the host's own");
        Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(40, host.Height - 4).ToArgb(), "no native border line at the bottom");
        Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(host.Width - 8, host.Height / 2).ToArgb(), "the native arrow button is clipped away");
        bool chevron = false;
        for (int x = host.Width - 34; x < host.Width - 10 && !chevron; x++)
            for (int y = host.Height / 2 - 8; y < host.Height / 2 + 8; y++)
                if (bmp.GetPixel(x, y).ToArgb() != Color.White.ToArgb()) { chevron = true; break; }
        Assert.IsTrue(chevron, "the host paints its own chevron");
    });

    [TestMethod]
    public void AnInputThatIsAlreadyDisabled_PaintsTheDisabledColourThroughout() => OnStaThread(() =>
    {
        using var host = new RoundedInput(new DateField { Enabled = false }) { Width = 260 };
        using Bitmap bmp = Render(host);
        Assert.AreEqual(Theme.DisabledBack.ToArgb(), bmp.GetPixel(host.Width - 8, host.Height / 2).ToArgb(), "inside the box, right of the chevron");
        Assert.AreEqual(Theme.DisabledBack.ToArgb(), bmp.GetPixel(host.Width - 60, host.Height / 2).ToArgb(), "inside the field itself, right of its text");
    });

    [TestMethod]
    public void DateField_InsideTheBox_IsAsTallAsOtherFields() => OnStaThread(() =>
    {
        using var text = new RoundedInput(new TextBox());
        using var date = new RoundedInput(new DateField());
        Assert.AreEqual(text.Height, date.Height);
    });

    [TestMethod]
    public void ErrorMessage_DrawsRedOutline() => OnStaThread(() =>
    {
        var row = new FieldRow("NAME", new TextBox(), rounded: true) { Width = 300 };
        row.SetMessage("Required");
        RoundedInput host = All(row).OfType<RoundedInput>().Single();
        Assert.IsTrue(host.HasError);
        host.Width = 260;
        using Bitmap bmp = Render(host);
        Assert.AreEqual(Theme.DangerRed.ToArgb(), bmp.GetPixel(host.Width / 2, 1).ToArgb());
    });

    [TestMethod]
    public void Focus_ShowsRing_AndTabGoesStraightToInput() => OnStaThread(() =>
    {
        using var form = new Form { Width = 400, Height = 300 };
        var first = new FieldRow("ONE", new TextBox(), rounded: true);
        var second = new FieldRow("TWO", new TextBox(), rounded: true);
        form.Controls.Add(first);
        form.Controls.Add(second);
        form.Show();
        Application.DoEvents();

        RoundedInput host = All(first).OfType<RoundedInput>().Single();
        Assert.IsFalse(host.TabStop, "the host itself is not a tab stop");
        first.Input.Focus();
        Application.DoEvents();
        Assert.IsTrue(host.FocusVisible);
        form.SelectNextControl(first.Input, true, true, true, true);
        Assert.AreSame(second.Input, form.ActiveControl, "active: " + form.ActiveControl?.GetType().Name);
    });

    [TestMethod]
    public void RoundedRow_KeepsInputInstance_AndMessage() => OnStaThread(() =>
    {
        var input = new TextBox();
        var row = new FieldRow("NAME", input, rounded: true);
        Assert.AreSame(input, row.Input);
        row.SetMessage("Required");
        Assert.AreEqual("Required", row.Message);
        row.ClearMessage();
        Assert.IsFalse(All(row).OfType<RoundedInput>().Single().HasError);
    });
}
