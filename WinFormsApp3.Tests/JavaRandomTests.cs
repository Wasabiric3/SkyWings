namespace SkyWings.Tests;

[TestClass]
public class JavaRandomTests
{
    [TestMethod]
    public void NextDouble_MatchesJavaUtilRandomForSeed42()
        => Assert.AreEqual(0.7275636800328681, new JavaRandom(42).NextDouble(), 1e-15);   // new java.util.Random(42).nextDouble()

    [TestMethod]
    public void NextDouble_IsAlwaysInTheUnitInterval_AndSameSeedRepeats()
    {
        var a = new JavaRandom(7);
        var b = new JavaRandom(7);
        for (int i = 0; i < 1000; i++)
        {
            double x = a.NextDouble();
            Assert.IsTrue(x >= 0 && x < 1, x.ToString());
            Assert.AreEqual(x, b.NextDouble());
        }
    }

    [TestMethod]
    public void StableSeed_IsTheSameEveryRun_AndDiffersByText()
    {
        Assert.AreEqual(SkyWings.Controllers.FlightStatusEngine.StableSeed("PR-101|20261120"), SkyWings.Controllers.FlightStatusEngine.StableSeed("PR-101|20261120"));
        Assert.AreNotEqual(SkyWings.Controllers.FlightStatusEngine.StableSeed("PR-101|20261120"), SkyWings.Controllers.FlightStatusEngine.StableSeed("PR-101|20261121"));
    }
}
