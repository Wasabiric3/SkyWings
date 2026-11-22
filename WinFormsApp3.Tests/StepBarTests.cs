using System.Reflection;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StepBarTests
{
    private static readonly string[] Names = { "Flight", "Passengers", "Class & extras", "Seats", "Payment", "Receipt" };

    private static StepBar Make(int width = 900, bool logo = true) => new(Names) { Width = width, ShowLogo = logo };

    private static Point Centre(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static void Click(StepBar bar, Point at)
        => typeof(StepBar).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0) });

    [TestMethod]
    public void HitStep_ReturnsThePillUnderThePoint_AndMinusOneInGapsAndOverTheLogo()
    {
        using var bar = Make();
        for (int i = 0; i < Names.Length; i++) Assert.AreEqual(i, bar.HitStep(Centre(bar.PillBounds(i))), $"pill {i}");
        Rectangle first = bar.PillBounds(0);
        Assert.AreEqual(-1, bar.HitStep(new Point(first.Right + 2, Centre(first).Y)), "gap between pills");
        Assert.AreEqual(-1, bar.HitStep(new Point(4, bar.Height / 2)), "over the logo");
    }

    [TestMethod]
    public void ClickOnAnEarlierStep_RaisesStepClicked()
    {
        using var bar = Make();
        bar.Current = 2;
        int clicked = -1;
        bar.StepClicked += (_, i) => clicked = i;
        Click(bar, Centre(bar.PillBounds(0)));
        Assert.AreEqual(0, clicked);
    }

    [TestMethod]
    public void ClickOnTheCurrentOrALaterStep_DoesNothing()
    {
        using var bar = Make();
        bar.Current = 2;
        int raised = 0;
        bar.StepClicked += (_, _) => raised++;
        Click(bar, Centre(bar.PillBounds(2)));
        Click(bar, Centre(bar.PillBounds(4)));
        Assert.AreEqual(0, raised);
    }

    [TestMethod]
    public void GoingBack_StillAllowsJumpingForwardToAVisitedStep()
    {
        using var bar = Make();
        bar.Current = 3;
        bar.Current = 1;
        Assert.AreEqual(3, bar.Furthest);
    }

    [TestMethod]
    public void Pills_FitTheWidth_AtMinimumWindowWidth_AndDoNotOverlap()
    {
        using var bar = Make(width: 900);
        Assert.IsGreaterThan(0, bar.PillBounds(0).Left);
        for (int i = 1; i < Names.Length; i++) Assert.IsGreaterThan(bar.PillBounds(i - 1).Right, bar.PillBounds(i).Left, $"pill {i} overlaps");
        Assert.IsLessThanOrEqualTo(900, bar.PillBounds(Names.Length - 1).Right);
    }

    [TestMethod]
    public void WithLogo_FirstPillStartsRightOfTheLogoArea_AndWithoutItStartsNearTheEdge()
    {
        using var with = Make(logo: true);
        using var without = Make(logo: false);
        Assert.IsGreaterThan(without.PillBounds(0).Left, with.PillBounds(0).Left);
    }

    [TestMethod]
    public void CurrentPill_FillsWithBrand()
    {
        using var bar = Make();
        bar.Current = 0;
        bar.CreateControl();
        using var bmp = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        Rectangle pill = bar.PillBounds(0);
        Assert.AreEqual(Theme.Brand.ToArgb(), bmp.GetPixel(pill.Left + 6, pill.Top + pill.Height / 2).ToArgb());
        Rectangle next = bar.PillBounds(1);
        Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(next.Left + 6, next.Top + next.Height / 2).ToArgb(), "a pending pill is white");
    }
}
