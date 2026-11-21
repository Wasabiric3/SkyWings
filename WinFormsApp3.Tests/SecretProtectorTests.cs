namespace SkyWings.Tests;

[TestClass]
public sealed class SecretProtectorTests
{
    [TestMethod]
    public void Protect_RoundTrips_AndHidesThePlainText()
    {
        string protectedValue = SecretProtector.Protect("app-pass-123");
        Assert.IsTrue(SecretProtector.IsProtected(protectedValue));
        Assert.DoesNotContain("app-pass-123", protectedValue);
        Assert.AreEqual("app-pass-123", SecretProtector.Unprotect(protectedValue));
    }

    [TestMethod]
    public void Unprotect_LeavesPlainTextAlone_AndProtectIsIdempotent()
    {
        Assert.AreEqual("plain", SecretProtector.Unprotect("plain"));
        string once = SecretProtector.Protect("x");
        Assert.AreEqual(once, SecretProtector.Protect(once));
    }

    [TestMethod]
    public void ProtectSmtpPassword_RewritesOnlyThatLine()
    {
        var (lines, changed) = SecretProtector.ProtectSmtpPassword(new[] { "# c", "smtp.host=h", "smtp.password = secret", "smtp.user=u" });
        Assert.IsTrue(changed);
        Assert.AreEqual("# c", lines[0]);
        Assert.AreEqual("smtp.host=h", lines[1]);
        Assert.AreEqual("smtp.user=u", lines[3]);
        StringAssert.StartsWith(lines[2], "smtp.password=dpapi:");
        Assert.DoesNotContain("secret", lines[2]);
        Assert.IsFalse(SecretProtector.ProtectSmtpPassword(lines).Changed, "a second run changes nothing");
    }

    [TestMethod]
    public void ProtectSmtpPasswordIn_WorksOnAFile_AndSaysSoWhenThereIsNothing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"skywings-props-{Guid.NewGuid():N}.properties");
        try
        {
            File.WriteAllLines(path, new[] { "smtp.password=secret" });
            StringAssert.Contains(CommandLine.ProtectSmtpPasswordIn(path), "now encrypted");
            Assert.DoesNotContain("secret", File.ReadAllText(path));
            StringAssert.Contains(CommandLine.ProtectSmtpPasswordIn(path), "Nothing was changed");
            StringAssert.Contains(CommandLine.ProtectSmtpPasswordIn(null), "nothing to protect");
        }
        finally { File.Delete(path); }
    }
}
