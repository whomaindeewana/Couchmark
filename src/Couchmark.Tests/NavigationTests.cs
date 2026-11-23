using Couchmark.Services;
using Xunit;

namespace Couchmark.Tests;

public class NavigationTests
{
    [Fact]
    public void PlayerNavigationArgs_SetsPropertiesCorrectly()
    {
        var movieArgs = new PlayerNavigationArgs(42, IsMovie: true);
        var episodeArgs = new PlayerNavigationArgs(108, IsMovie: false);

        Assert.Equal(42, movieArgs.Id);
        Assert.True(movieArgs.IsMovie);

        Assert.Equal(108, episodeArgs.Id);
        Assert.False(episodeArgs.IsMovie);
    }

    [Fact]
    public void LibVlcSharp_TrackMethodsExist()
    {
        // Just verify method signatures compile
        Assert.NotNull(typeof(LibVLCSharp.Shared.MediaPlayer).GetProperty("AudioTrack"));
        Assert.NotNull(typeof(LibVLCSharp.Shared.MediaPlayer).GetProperty("AudioTrackDescription"));
        Assert.NotNull(typeof(LibVLCSharp.Shared.MediaPlayer).GetProperty("Spu"));
        Assert.NotNull(typeof(LibVLCSharp.Shared.MediaPlayer).GetProperty("SpuDescription"));
    }
}
