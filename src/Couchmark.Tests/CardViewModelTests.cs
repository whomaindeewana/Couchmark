using Couchmark.ViewModels;
using System.Collections.Generic;
using Xunit;

namespace Couchmark.Tests;

public class CardViewModelTests
{
    [Fact]
    public void MovieCardItem_ShowProgressUpdatesWhenWatchedChanges()
    {
        var card = new MovieCardItem
        {
            Id = 1,
            Title = "Dune",
            ProgressPercent = 45,
            IsWatched = false
        };

        var changedProperties = new List<string?>();
        card.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        Assert.True(card.ShowProgress);

        card.IsWatched = true;

        Assert.False(card.ShowProgress);
        Assert.Contains(nameof(MovieCardItem.ShowProgress), changedProperties);
    }

    [Fact]
    public void TvShowCardItem_HasUnwatchedTracksUnwatchedCount()
    {
        var card = new TvShowCardItem
        {
            Id = 10,
            Title = "Andor",
            TotalEpisodes = 12,
            UnwatchedCount = 5,
            IsWatched = false
        };

        var changedProperties = new List<string?>();
        card.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        Assert.True(card.HasUnwatched);

        card.UnwatchedCount = 0;
        card.IsWatched = true;

        Assert.False(card.HasUnwatched);
        Assert.True(card.IsWatched);
        Assert.Contains(nameof(TvShowCardItem.HasUnwatched), changedProperties);
    }

    [Fact]
    public void HomeMediaCard_ShowProgressAndHasUnwatchedUpdateReactively()
    {
        var card = new HomeMediaCard
        {
            Id = 7,
            Title = "Fallout",
            ProgressPercent = 60,
            IsWatched = false,
            UnwatchedCount = 4
        };

        Assert.True(card.ShowProgress);
        Assert.True(card.HasUnwatched);

        card.IsWatched = true;
        card.UnwatchedCount = 0;

        Assert.False(card.ShowProgress);
        Assert.False(card.HasUnwatched);
    }
}
