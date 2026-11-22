using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public sealed class GridStyleTests
{
    [TestMethod]
    public void Selection_UsesBrandSoft_AndHeaderUsesThePageColour()
    {
        using var grid = new DataGridView();
        GridStyle.Apply(grid);
        Assert.AreEqual(Theme.BrandSoft.ToArgb(), grid.DefaultCellStyle.SelectionBackColor.ToArgb());
        Assert.AreEqual(Theme.PageBg.ToArgb(), grid.ColumnHeadersDefaultCellStyle.BackColor.ToArgb());
        Assert.AreEqual(Theme.Border.ToArgb(), grid.GridColor.ToArgb());
    }

    [TestMethod]
    public void StatusPills_Unchanged()
    {
        Assert.AreEqual(Color.FromArgb(0xDA, 0xEC, 0xE1), GridStyle.StatusStyle("PAID")!.Value.Back);
        Assert.AreEqual(Color.FromArgb(0xF5, 0xDD, 0xDA), GridStyle.StatusStyle("CANCELED")!.Value.Back);
        Assert.AreEqual(Color.FromArgb(0xDC, 0xE7, 0xF3), GridStyle.StatusStyle("IN FLIGHT")!.Value.Back);
        Assert.IsNull(GridStyle.StatusStyle("whatever"));
    }

    [TestMethod]
    public void ASelectedStatusCell_KeepsTheRowHighlightAroundItsPill() => FormHarness.OnStaThread(() =>
    {
        using var form = new Form { Width = 500, Height = 300, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true };
        GridStyle.Apply(grid);
        grid.DataSource = new[] { new { Flight = "PR-101", Status = "Active" }, new { Flight = "PR-102", Status = "Active" } };
        form.Controls.Add(grid);
        form.Show();
        Application.DoEvents();
        grid.CurrentCell = grid[1, 0];
        grid.Rows[0].Selected = true;
        Application.DoEvents();
        using var bmp = new Bitmap(grid.Width, grid.Height);
        grid.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        Rectangle cell = grid.GetCellDisplayRectangle(1, 0, false);
        Color corner = bmp.GetPixel(cell.Right - 6, cell.Top + 4);   // inside the cell, outside the pill
        Assert.AreEqual(Theme.BrandSoft.ToArgb(), corner.ToArgb(), "the status cell of a selected row is highlighted like the rest of the row");
    });
}
