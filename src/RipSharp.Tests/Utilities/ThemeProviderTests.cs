using Microsoft.Extensions.Options;

using Spectre.Console;

using Xunit;

namespace BugZapperLabs.RipSharp.Tests.Utilities;

public class ThemeProviderTests
{
    [Fact]
    public void Colors_AreLoadedFromOptions()
    {
        var options = Options.Create(new ThemeOptions
        {
            Colors = new ThemeColors
            {
                Success = "#010203"
            }
        });

        var provider = new ThemeProvider(options);

        provider.SuccessColor.Should().Be(new Color(1, 2, 3));
    }

    [Fact]
    public void Emojis_AreLoadedFromOptions()
    {
        var options = Options.Create(new ThemeOptions
        {
            Emojis = new ThemeEmojis
            {
                Warning = "!"
            }
        });

        var provider = new ThemeProvider(options);

        provider.Emojis.Warning.Should().Be("!");
    }
}
