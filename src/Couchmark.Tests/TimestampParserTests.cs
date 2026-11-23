using Couchmark.Helpers;
using Xunit;

namespace Couchmark.Tests;

public class TimestampParserTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(-10, "")]
    [InlineData(45, "00:45")]
    [InlineData(90, "01:30")]
    [InlineData(3665, "01:01:05")]
    public void FormatSeconds_FormatsCorrectly(int seconds, string expected)
    {
        string actual = TimestampParser.FormatSeconds(seconds);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("   ", 0)]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:30", 90)]
    [InlineData("00:45", 45)]
    [InlineData("01:01:05", 3665)]
    [InlineData("1:01:05", 3665)]
    [InlineData("invalid", 0)]
    [InlineData("-5", 0)]
    public void ParseToSeconds_ParsesCorrectly(string? input, int expected)
    {
        int actual = TimestampParser.ParseToSeconds(input);
        Assert.Equal(expected, actual);
    }
}
