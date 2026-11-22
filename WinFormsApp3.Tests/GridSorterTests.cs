using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class GridSorterTests
{
    private sealed record Item(string Name, int Count, DateTime At, string? Note = null)
    {
        public string Label => Name;   // shown column
        public int Qty => Count;
        public DateTime When => At;
    }

    private static readonly DateTime T0 = new(2026, 11, 22, 9, 0, 0);

    private static GridSorter<Item> Make(string column = "When", bool descending = true) => new(
        new Dictionary<string, SortKey<Item>>
        {
            ["Label"] = new(i => i.Name),
            ["Qty"] = new(i => i.Count),
            ["When"] = new(i => i.At, FirstDescending: true),
            ["Note"] = new(i => i.Note),
        }, column, descending);

    private static readonly Item[] Items =
    {
        new("banana", 3, T0.AddMinutes(10)),
        new("Apple", 9, T0.AddMinutes(30)),
        new("cherry", 3, T0.AddMinutes(20)),
    };

    private static string[] Names(IEnumerable<Item> items) => items.Select(i => i.Name).ToArray();

    [TestMethod]
    public void Sort_UsesTheDefaultColumnAndDirection()
    {
        CollectionAssert.AreEqual(new[] { "Apple", "cherry", "banana" }, Names(Make("When", true).Sort(Items)), "newest first");
        CollectionAssert.AreEqual(new[] { "banana", "cherry", "Apple" }, Names(Make("When", false).Sort(Items)), "oldest first");
    }

    [TestMethod]
    public void Text_SortsIgnoringCase_AndNumbersAsNumbers()
    {
        CollectionAssert.AreEqual(new[] { "Apple", "banana", "cherry" }, Names(Make("Label", false).Sort(Items)), "Apple before banana despite the capital");
        CollectionAssert.AreEqual(new[] { "Apple", "banana", "cherry" }, Names(Make("Qty", true).Sort(Items)), "9 before 3; the two 3s keep their order");
    }

    [TestMethod]
    public void EqualKeys_KeepTheirOriginalOrder_BothWays()
    {
        CollectionAssert.AreEqual(new[] { "banana", "cherry", "Apple" }, Names(Make("Qty", false).Sort(Items)));
        CollectionAssert.AreEqual(new[] { "Apple", "banana", "cherry" }, Names(Make("Qty", true).Sort(Items)));
    }

    [TestMethod]
    public void ARowWithoutAValue_SortsFirstWhenAscending_AndLastWhenDescending()
    {
        Item[] rows = { new("a", 1, T0, "zzz"), new("b", 1, T0, null), new("c", 1, T0, "aaa") };
        CollectionAssert.AreEqual(new[] { "b", "c", "a" }, Names(Make("Note", false).Sort(rows)));
        CollectionAssert.AreEqual(new[] { "a", "c", "b" }, Names(Make("Note", true).Sort(rows)));
    }

    [TestMethod]
    public void Toggle_ANewColumnStartsInItsOwnFirstDirection_TheSameColumnFlips()
    {
        GridSorter<Item> sorter = Make("Label", false);
        Assert.IsTrue(sorter.Toggle("When"));
        Assert.AreEqual(("When", true), (sorter.Column, sorter.Descending), "dates start newest first");
        Assert.IsTrue(sorter.Toggle("When"));
        Assert.IsFalse(sorter.Descending);
        Assert.IsTrue(sorter.Toggle("Qty"));
        Assert.AreEqual(("Qty", false), (sorter.Column, sorter.Descending), "other columns start ascending");
    }

    [TestMethod]
    public void Toggle_AnUnknownColumn_ChangesNothing()
    {
        GridSorter<Item> sorter = Make("When", true);
        Assert.IsFalse(sorter.Toggle("Nope"));
        Assert.AreEqual(("When", true), (sorter.Column, sorter.Descending));
    }

    [TestMethod]
    public void TheSetting_RoundTrips_AndBadOrUnknownSettingsAreIgnored()
    {
        GridSorter<Item> sorter = Make("Qty", false);
        Assert.AreEqual("Qty:asc", sorter.ToSetting());
        GridSorter<Item> other = Make();
        other.LoadSetting("Qty:asc");
        Assert.AreEqual(("Qty", false), (other.Column, other.Descending));
        other.LoadSetting("Label:desc");
        Assert.AreEqual(("Label", true), (other.Column, other.Descending));

        foreach (string bad in new[] { "", "   ", "Qty", "Nope:asc", "Qty:sideways", ":asc", "Qty:asc:extra" })
        {
            other.LoadSetting(bad);
            Assert.AreEqual(("Label", true), (other.Column, other.Descending), $"'{bad}' is ignored");
        }
        other.LoadSetting(null);
        Assert.AreEqual("Label", other.Column);
    }

    [TestMethod]
    public void AnEmptyList_SortsToAnEmptyList()
        => Assert.IsEmpty(Make().Sort(Array.Empty<Item>()));

    // ---- on a real grid ----

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AssertFailedException(error.Message, error);
    }

    [TestMethod]
    public void OnAGrid_ColumnsBecomeSortable_TheSortedColumnShowsAnArrow_AndAHeaderClickSortsAndTellsTheOwner() => OnSta(() =>
    {
        using var form = new Form { Width = 600, Height = 400 };
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
        form.Controls.Add(grid);
        GridSorter<Item> sorter = Make("When", true);
        sorter.Attach(grid);
        form.Show();
        grid.DataSource = sorter.Sort(Items).ToList();
        Application.DoEvents();

        DataGridViewColumn when = grid.Columns.Cast<DataGridViewColumn>().Single(c => c.DataPropertyName == "When");
        DataGridViewColumn qty = grid.Columns.Cast<DataGridViewColumn>().Single(c => c.DataPropertyName == "Qty");
        Assert.AreEqual(DataGridViewColumnSortMode.Programmatic, qty.SortMode);
        Assert.AreEqual(SortOrder.Descending, when.HeaderCell.SortGlyphDirection, "the default sort is marked");
        Assert.AreEqual(SortOrder.None, qty.HeaderCell.SortGlyphDirection);

        int changed = 0;
        sorter.Changed += (_, _) => changed++;
        typeof(DataGridView).GetMethod("OnColumnHeaderMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(grid, new object[] { new DataGridViewCellMouseEventArgs(qty.Index, -1, 5, 5, new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)) });
        Assert.AreEqual(1, changed, "the owner is told so it can re-sort and rebind");
        Assert.AreEqual(("Qty", false), (sorter.Column, sorter.Descending));
        Assert.AreEqual(SortOrder.Ascending, qty.HeaderCell.SortGlyphDirection);
        Assert.AreEqual(SortOrder.None, when.HeaderCell.SortGlyphDirection, "only the sorted column has an arrow");
    });
}
