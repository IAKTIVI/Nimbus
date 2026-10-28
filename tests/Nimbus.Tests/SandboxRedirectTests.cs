using Nimbus.Core;
using Nimbus.Installation;

namespace Nimbus.Tests;

[Collection("Paths")]
public class SandboxRedirectTests
{
    /// <summary>
    /// Whether AppData writes are redirected depends on where the tests run, so this only asserts when told what to
    /// expect: NIMBUS_EXPECT_SANDBOX=1 (e.g. started from inside a Store app) or 0 (a normal terminal).
    /// </summary>
    [Fact]
    public void DetectsAppDataRedirection()
    {
        string? expect = Environment.GetEnvironmentVariable("NIMBUS_EXPECT_SANDBOX");
        if (expect is null)
            return;

        string original = Paths.Base;
        try
        {
            Paths.OverrideBase(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NimbusSandboxTest"));
            string? redirect = Installer.FindSandboxRedirect();

            if (expect == "1")
                Assert.Contains(@"\LocalCache\", redirect);
            else
                Assert.Null(redirect);
        }
        finally
        {
            Paths.OverrideBase(original);
        }
    }
}
