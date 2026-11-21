namespace SkyWings.Tests;

[TestClass]
public sealed class CsvExportTests
{
    [TestMethod]
    public void Build_QuotesCells_AndNeutralisesFormulas()
    {
        var lines = CsvExport.Build(new[] { "Ref", "Name" }, new[] { new string?[] { "SKY-1", "=cmd|' /C calc'!A0" }, new string?[] { "SKY-2", "A \"B\", C" } });
        Assert.HasCount(3, lines);
        Assert.AreEqual("Ref,Name", lines[0]);
        Assert.AreEqual("\"SKY-1\",\"'=cmd|' /C calc'!A0\"", lines[1]);
        Assert.AreEqual("\"SKY-2\",\"A \"\"B\"\", C\"", lines[2]);
    }

    [TestMethod]
    public void Money_UsesTwoDecimalsAndNoCultureSeparators()
        => Assert.AreEqual("12345.50", CsvExport.Money(12345.5m));
}
