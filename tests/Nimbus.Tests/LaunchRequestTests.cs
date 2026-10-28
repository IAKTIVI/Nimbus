using Nimbus.Roblox;

namespace Nimbus.Tests;

public class LaunchRequestTests
{
    private const string WebsiteLink =
        "roblox-player:1+launchmode:play+gameinfo:TICKET+launchtime:1761580000000" +
        "+placelauncherurl:https%3A%2F%2Fwww.roblox.com%2FGame%2FPlaceLauncher.ashx%3FplaceId%3D920587237" +
        "+browsertrackerid:123+robloxLocale:en_us+gameLocale:en_us+channel:zbeta+LaunchExp:InApp";

    [Fact]
    public void NoArgumentsOpensTheWindow() =>
        Assert.Equal(LaunchKind.None, LaunchRequest.Parse([]).Kind);

    [Fact]
    public void WebsiteLinkIsPassedToRobloxWithoutTheChannel()
    {
        var request = LaunchRequest.Parse([WebsiteLink]);

        Assert.Equal(LaunchKind.Link, request.Kind);
        Assert.True(request.LaunchesRoblox);
        Assert.Contains("+channel:+LaunchExp:InApp", request.RobloxArgument);
        Assert.DoesNotContain("zbeta", request.RobloxArgument);
        Assert.Contains("placeId%3D920587237", request.RobloxArgument);
    }

    [Fact]
    public void ChannelAtTheEndIsStripped() =>
        Assert.Equal("roblox-player:1+launchmode:app+channel:", LaunchRequest.StripChannel("roblox-player:1+launchmode:app+channel:live"));

    [Fact]
    public void DeepLinksArePassedThrough()
    {
        var request = LaunchRequest.Parse(["roblox://experiences/start?placeId=920587237"]);

        Assert.Equal(LaunchKind.Link, request.Kind);
        Assert.Equal("roblox://experiences/start?placeId=920587237", request.RobloxArgument);
    }

    [Fact]
    public void QuotedLinkIsUnwrapped() =>
        Assert.Equal(LaunchKind.Link, LaunchRequest.Parse(["\"roblox://experiences/start?placeId=1\""]).Kind);

    [Fact]
    public void PlayerFlagOpensTheRobloxApp()
    {
        var request = LaunchRequest.Parse(["-player"]);

        Assert.Equal(LaunchKind.Player, request.Kind);
        Assert.Equal("--app", request.RobloxArgument);
    }

    [Theory]
    [InlineData("-uninstall", LaunchKind.Uninstall)]
    [InlineData("-SETTINGS", LaunchKind.Settings)]
    [InlineData("-install", LaunchKind.Install)]
    [InlineData("something-else", LaunchKind.None)]
    public void Flags(string arg, LaunchKind expected) =>
        Assert.Equal(expected, LaunchRequest.Parse([arg]).Kind);
}
