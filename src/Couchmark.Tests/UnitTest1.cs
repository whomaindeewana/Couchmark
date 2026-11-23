using Couchmark.Converters;
using System.Globalization;
using System.Windows;
using Xunit;

namespace Couchmark.Tests;

public class ConverterTests
{
    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void InverseBoolToVisibilityConverter_ConvertsCorrectly(bool input, Visibility expected)
    {
        var converter = new InverseBoolToVisibilityConverter();
        var result = converter.Convert(input, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(Visibility.Collapsed, true)]
    [InlineData(Visibility.Hidden, true)]
    [InlineData(Visibility.Visible, false)]
    public void InverseBoolToVisibilityConverter_ConvertsBackCorrectly(Visibility input, bool expected)
    {
        var converter = new InverseBoolToVisibilityConverter();
        var result = converter.ConvertBack(input, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBoolConverter_ConvertsBoolCorrectly(bool input, bool expected)
    {
        var converter = new InverseBoolConverter();
        var result = converter.Convert(input, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void InverseBoolConverter_ConvertsToVisibilitySafely(bool input, Visibility expected)
    {
        var converter = new InverseBoolConverter();
        var result = converter.Convert(input, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, Visibility.Visible)]
    [InlineData(false, Visibility.Collapsed)]
    public void BoolToVisibilityConverter_ConvertsCorrectly(bool input, Visibility expected)
    {
        var converter = new BoolToVisibilityConverter();
        var result = converter.Convert(input, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1, Visibility.Visible)]
    [InlineData(5, Visibility.Visible)]
    [InlineData(0, Visibility.Collapsed)]
    [InlineData(-1, Visibility.Collapsed)]
    public void IntToVisibilityConverter_ConvertsCorrectly(int input, Visibility expected)
    {
        var converter = new IntToVisibilityConverter();
        var result = converter.Convert(input, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("path/to/image.jpg", Visibility.Collapsed)]
    [InlineData("", Visibility.Visible)]
    [InlineData(null, Visibility.Visible)]
    public void NullToVisibilityConverter_ConvertsCorrectly(string? input, Visibility expected)
    {
        var converter = new NullToVisibilityConverter();
        var result = converter.Convert(input!, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }
}
