using AwesomeAssertions;
using MediaEncoding;
using Xunit;

namespace MediaEncoding.Tests;

public class MakeMkvProtocolTests
{
    [Fact]
    public void ExtractQuoted_ReturnsInnerText()
    {
        var line = "MSG:1005,0,0,\"MakeMKV v1.18.2 linux(x64-release) started\"";
        var result = MakeMkvProtocol.ExtractQuoted(line);
        result.Should().Be("MakeMKV v1.18.2 linux(x64-release) started");
    }

    [Fact]
    public void ExtractQuoted_ReturnsNull_WhenNoQuotes()
    {
        var line = "CINFO:1,Blu-ray disc";
        var result = MakeMkvProtocol.ExtractQuoted(line);
        result.Should().BeNull();
    }
}
