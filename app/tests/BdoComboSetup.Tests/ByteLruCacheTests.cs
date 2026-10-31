using BdoComboSetup.Core.Assets.Pack;

namespace BdoComboSetup.Tests;

public sealed class ByteLruCacheTests
{
    [Fact]
    public void The_least_recently_used_item_is_evicted_first()
    {
        var cache = new ByteLruCache(20);
        cache.Add("a", new byte[10]);
        cache.Add("b", new byte[10]);

        cache.Add("c", new byte[10]);

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(20, cache.Bytes);
    }

    [Fact]
    public void Reading_an_item_makes_it_the_most_recently_used()
    {
        var cache = new ByteLruCache(20);
        cache.Add("a", new byte[10]);
        cache.Add("b", new byte[10]);
        Assert.True(cache.TryGet("a", out _));

        cache.Add("c", new byte[10]);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void An_item_larger_than_the_budget_is_not_kept()
    {
        var cache = new ByteLruCache(5);

        cache.Add("big", new byte[10]);

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("big", out _));
    }

    [Fact]
    public void Adding_an_existing_key_replaces_it_and_its_size()
    {
        var cache = new ByteLruCache(100);
        cache.Add("k", new byte[10]);

        cache.Add("k", new byte[4]);

        Assert.Equal(1, cache.Count);
        Assert.Equal(4, cache.Bytes);
    }

    [Fact]
    public void Clear_empties_the_cache()
    {
        var cache = new ByteLruCache(100);
        cache.Add("k", new byte[10]);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void A_negative_budget_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteLruCache(-1));
    }
}
