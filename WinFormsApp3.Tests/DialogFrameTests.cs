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

    [TestMethod]
    public void Create_SizesTheWindowToItsRows_SoNothingIsCutOff() => FormHarness.OnStaThread(() =>
    {
        using var form = new GlassForm { FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        var rows = new Control[]
        {
            new FieldRow("USERNAME", new TextBox()), new FieldRow("ROLE", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }),
            new FieldRow("DATE", new DateTimePicker()), new FieldRow("PASSWORD", new TextBox())
        };
        var ok = new GlassButton { Text = "Save" };
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame frame = DialogFrame.Create(form, 540, "Add a staff member", "They must change the password at first sign-in. Pick a long phrase.", rows, ok, cancel);
        form.Show();
        Application.DoEvents();

        GlassCard card = frame.Content.Controls.OfType<GlassCard>().Single();
        Control last = rows[^1];
        Assert.IsLessThanOrEqualTo(card.DisplayRectangle.Bottom, last.Bottom, "the last field is inside the card");
        Assert.IsLessThanOrEqualTo(frame.ClientSize.Height, frame.Buttons.Bottom, "the buttons are inside the window");
        Assert.AreSame(ok, form.AcceptButton);
        Assert.AreSame(cancel, form.CancelButton);
        Assert.AreEqual(rows[0], card.Controls.Cast<Control>().OrderBy(c => c.TabIndex).First(), "the first field is first in Tab order");
        Assert.IsEmpty(FormHarness.Clipped(form));
    });

    [TestMethod]
    public void Enter_And_Esc_StillWork_OnEveryStaffDialog() => FormHarness.OnStaThread(() =>
    {
        string[] names = { "first-run-admin", "change-password", "prompt", "add-staff", "reset-password", "backup-password", "restore-credentials" };
        using var harness = new FormHarness();
        foreach ((string name, Func<Form> create) in harness.Forms().Where(f => names.Contains(f.Name)))
        {
            using Form form = create();
            form.Show();
            Application.DoEvents();
            Assert.IsNotNull(form.AcceptButton, name + " has an Enter button");
            Assert.IsNotNull(form.CancelButton, name + " has an Esc button");
            Assert.AreEqual(DialogResult.Cancel, ((GlassButton)form.CancelButton).DialogResult, name + " Esc cancels");
            Assert.IsEmpty(FormHarness.Clipped(form), name);
            ((GlassButton)form.CancelButton).PerformClick();
            Assert.AreEqual(DialogResult.Cancel, form.DialogResult, name);
        }
    });

    [TestMethod]
    public void Create_MeasuresSideBySideRowsToo() => FormHarness.OnStaThread(() =>
    {
        using var form = new GlassForm { FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        var combo = new FieldRow("TYPE", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
        var rows = new Control[]
        {
            Layouts.Row(14, new FieldRow("FLIGHT", new TextBox()), combo),
            Layouts.Row(14, new FieldRow("FARE", new TextBox()), new FieldRow("SEATS", new TextBox())),
            new FieldRow("TIME", new DateTimePicker())
        };
        DialogFrame frame = DialogFrame.Create(form, 560, "Edit flight", "", rows, new GlassButton { Text = "Save" }, new GlassButton { Text = "Cancel" });
        form.Show();
        Application.DoEvents();
        GlassCard card = frame.Content.Controls.OfType<GlassCard>().Single();
        Assert.IsLessThanOrEqualTo(card.DisplayRectangle.Bottom, rows[^1].Bottom, "the last row is inside the card");
        foreach (Control row in rows.OfType<TableLayoutPanel>())
            Assert.IsGreaterThanOrEqualTo(row.Controls.Cast<Control>().Max(c => c.Height), row.Height, "a side-by-side row is as tall as its tallest field");
        Assert.IsEmpty(FormHarness.Clipped(form));
    });
}
