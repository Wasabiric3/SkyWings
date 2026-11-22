using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DialogFrameTests
{
    private static (Form Form, DialogFrame Frame, GlassButton Ok, GlassButton Cancel) Make()
    {
        var form = new GlassForm { ClientSize = new Size(520, 400) };
        var frame = new DialogFrame();
        frame.Header.Title = "Reset password";
        frame.Header.Hint = "They will choose their own after signing in.";
        var ok = new GlassButton { Text = "Save" };
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        frame.Content.Controls.Add(new TextBox { Dock = DockStyle.Top });
        frame.Buttons.Controls.Add(ok);
        frame.Buttons.Controls.Add(cancel);
        form.Controls.Add(frame);
        frame.Attach(form, ok, cancel);
        form.Show();
        Application.DoEvents();
        return (form, frame, ok, cancel);
    }

    [TestMethod]
    public void Attach_SetsAcceptAndCancelButtons() => FormHarness.OnStaThread(() =>
    {
        (Form form, _, GlassButton ok, GlassButton cancel) = Make();
        using (form)
        {
            Assert.AreSame(ok, form.AcceptButton);
            Assert.AreSame(cancel, form.CancelButton);
        }
    });

    [TestMethod]
    public void Esc_ClosesWithCancel() => FormHarness.OnStaThread(() =>
    {
        (Form form, _, _, GlassButton cancel) = Make();
        using (form)
        {
            cancel.PerformClick();
            Assert.AreEqual(DialogResult.Cancel, form.DialogResult);
        }
    });

    [TestMethod]
    public void ButtonsAreRightAligned_FirstAddedIsTheRightmost() => FormHarness.OnStaThread(() =>
    {
        (Form form, DialogFrame frame, GlassButton ok, GlassButton cancel) = Make();
        using (form)
        {
            Assert.IsGreaterThan(cancel.Left, ok.Left, "the primary button is at the far right");
            Assert.IsGreaterThan(frame.ClientSize.Width * 2 / 3, ok.Right, "the buttons sit at the right edge");
        }
    });

    [TestMethod]
    public void ContentFillsBetweenHeaderAndButtons() => FormHarness.OnStaThread(() =>
    {
        (Form form, DialogFrame frame, _, _) = Make();
        using (form)
        {
            Assert.IsGreaterThanOrEqualTo(frame.Header.Bottom, frame.Content.Top);
            Assert.IsLessThanOrEqualTo(frame.Buttons.Top, frame.Content.Bottom);
            Assert.IsGreaterThan(0, frame.Content.Height);
        }
    });

    [TestMethod]
    public void TabOrder_ContentBeforeButtons() => FormHarness.OnStaThread(() =>
    {
        (Form form, DialogFrame frame, _, _) = Make();
        using (form)
            Assert.IsLessThan(frame.Buttons.TabIndex, frame.Content.TabIndex);
    });
}
