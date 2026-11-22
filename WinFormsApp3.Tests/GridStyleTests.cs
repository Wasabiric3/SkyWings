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
}
