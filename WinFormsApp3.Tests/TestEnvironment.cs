using SkyWings;

namespace SkyWings.Tests;

/// <summary>Points the data folder at a throwaway directory so tests never write into a real user's data.</summary>
[TestClass]
public static class TestEnvironment
{
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        string folder = Path.Combine(Path.GetTempPath(), "skywings-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, folder);
    }
}
