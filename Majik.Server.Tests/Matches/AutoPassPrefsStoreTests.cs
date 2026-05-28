using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Server.Matches;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Unit tests for the Slice 5a process-local
/// <see cref="AutoPassPrefsStore"/>. Validates the put/get/evict/has
/// surface in isolation from the wider MatchService stack.
/// </summary>
public class AutoPassPrefsStoreTests
{
    [Fact]
    public void Get_UnknownKey_ReturnsDefault()
    {
        var store = new AutoPassPrefsStore();
        var prefs = store.Get(Guid.NewGuid(), "any-sub");

        prefs.Should().BeSameAs(AutoPassPrefs.Default);
        prefs.FullControl.Should().BeFalse();
        // Standard MTG-client opponent-turn pattern — wake up at
        // beginning-of-combat + end step, auto-pass everywhere else on
        // the opponent's turn.
        prefs.PhaseStops.Should().HaveCount(2);
        prefs.PhaseStops["BeginningOfCombat"].Should().Be("theirs");
        prefs.PhaseStops["End"].Should().Be("theirs");
    }

    [Fact]
    public void Get_NullOrEmptySub_ReturnsDefault()
    {
        var store = new AutoPassPrefsStore();
        store.Get(Guid.NewGuid(), null).Should().BeSameAs(AutoPassPrefs.Default);
        store.Get(Guid.NewGuid(), "").Should().BeSameAs(AutoPassPrefs.Default);
    }

    [Fact]
    public void Set_ThenGet_ReturnsValue()
    {
        var store = new AutoPassPrefsStore();
        var matchId = Guid.NewGuid();
        var prefs = new AutoPassPrefs(
            FullControl: true,
            PhaseStops: new Dictionary<string, string> { ["Upkeep"] = "mine" });

        store.Set(matchId, "alice", prefs);

        var got = store.Get(matchId, "alice");
        got.Should().BeSameAs(prefs);
        got.FullControl.Should().BeTrue();
        got.PhaseStops["Upkeep"].Should().Be("mine");
    }

    [Fact]
    public void Set_OverwriteIsIdempotent()
    {
        var store = new AutoPassPrefsStore();
        var matchId = Guid.NewGuid();
        store.Set(matchId, "alice", new AutoPassPrefs(false, new Dictionary<string, string>()));
        store.Set(matchId, "alice", new AutoPassPrefs(true, new Dictionary<string, string> { ["End"] = "theirs" }));

        var got = store.Get(matchId, "alice");
        got.FullControl.Should().BeTrue();
        got.PhaseStops["End"].Should().Be("theirs");
        store.Count.Should().Be(1);
    }

    [Fact]
    public void Set_DifferentSubsKeepSeparateEntries()
    {
        var store = new AutoPassPrefsStore();
        var matchId = Guid.NewGuid();
        store.Set(matchId, "alice", new AutoPassPrefs(true, new Dictionary<string, string>()));
        store.Set(matchId, "bob", new AutoPassPrefs(false, new Dictionary<string, string> { ["End"] = "mine" }));

        store.Get(matchId, "alice").FullControl.Should().BeTrue();
        store.Get(matchId, "bob").FullControl.Should().BeFalse();
        store.Get(matchId, "bob").PhaseStops["End"].Should().Be("mine");
        store.Count.Should().Be(2);
    }

    [Fact]
    public void Set_DifferentMatchesKeepSeparateEntries()
    {
        var store = new AutoPassPrefsStore();
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        store.Set(m1, "alice", new AutoPassPrefs(true, new Dictionary<string, string>()));
        store.Set(m2, "alice", new AutoPassPrefs(false, new Dictionary<string, string>()));

        store.Get(m1, "alice").FullControl.Should().BeTrue();
        store.Get(m2, "alice").FullControl.Should().BeFalse();
    }

    [Fact]
    public void Has_TracksRegistration()
    {
        var store = new AutoPassPrefsStore();
        var matchId = Guid.NewGuid();
        store.Has(matchId, "alice").Should().BeFalse();
        store.Set(matchId, "alice", AutoPassPrefs.Default);
        store.Has(matchId, "alice").Should().BeTrue();
        store.Has(matchId, "bob").Should().BeFalse();
        store.Has(matchId, null).Should().BeFalse();
    }

    [Fact]
    public void EvictMatch_RemovesAllSubsForThatMatch()
    {
        var store = new AutoPassPrefsStore();
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        store.Set(m1, "alice", new AutoPassPrefs(true, new Dictionary<string, string>()));
        store.Set(m1, "bob", new AutoPassPrefs(false, new Dictionary<string, string>()));
        store.Set(m2, "carol", new AutoPassPrefs(true, new Dictionary<string, string>()));

        var removed = store.EvictMatch(m1);

        removed.Should().Be(2);
        store.Has(m1, "alice").Should().BeFalse();
        store.Has(m1, "bob").Should().BeFalse();
        store.Has(m2, "carol").Should().BeTrue();
        store.Count.Should().Be(1);
    }

    [Fact]
    public void EvictMatch_NoEntries_ReturnsZero()
    {
        var store = new AutoPassPrefsStore();
        store.EvictMatch(Guid.NewGuid()).Should().Be(0);
    }

    [Fact]
    public void Set_NullArgs_Throws()
    {
        var store = new AutoPassPrefsStore();
        var matchId = Guid.NewGuid();
        var act1 = () => store.Set(matchId, null!, AutoPassPrefs.Default);
        var act2 = () => store.Set(matchId, "alice", null!);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ConcurrentSetAndGet_DoesNotCrash()
    {
        var store = new AutoPassPrefsStore();
        var matchId = Guid.NewGuid();

        var sub = "alice";
        var writers = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 200; j++)
            {
                store.Set(matchId, sub, new AutoPassPrefs(j % 2 == 0, new Dictionary<string, string>()));
            }
        })).ToArray();
        var readers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (int j = 0; j < 200; j++)
            {
                var snapshot = store.Get(matchId, sub);
                snapshot.Should().NotBeNull();
            }
        })).ToArray();

        await Task.WhenAll(writers.Concat(readers).ToArray());
        // No crash + final state is one of the two written shapes.
        store.Has(matchId, sub).Should().BeTrue();
    }
}
