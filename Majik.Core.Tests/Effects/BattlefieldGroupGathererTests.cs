using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Unit tests for <see cref="BattlefieldGroupGatherer"/> — the reusable
/// candidate gatherer for a Layer-6 group ability-grant that enumerates EVERY
/// permanent across ALL players' battlefield zones, so a group static's
/// <c>scope</c> filter can select members by EFFECTIVE controller
/// (CR 110.2 / 700.6 / 611.2c) rather than by which battlefield zone physically
/// holds the card. This is the controlled-but-not-owned residual of the
/// group-static family (Chromatic Lantern / Kataki / Serra's Emissary).
/// </summary>
public class BattlefieldGroupGathererTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Permanent AddArtifact(string name, Player owner, Player controller)
    {
        var a = new Artifact(name, "{2}");
        a.ChangeOwner(owner);
        a.ChangeController(controller);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }

    [Fact]
    public void AllBattlefieldPermanents_NullPlayers_ReturnsEmpty()
    {
        BattlefieldGroupGatherer.AllBattlefieldPermanents(null).Should().BeEmpty();
    }

    [Fact]
    public void AllBattlefieldPermanents_EnumeratesBothPlayersBattlefields()
    {
        var aliceArt = AddArtifact("Alice's Widget", _alice, _alice);
        var bobArt = AddArtifact("Bob's Widget", _bob, _bob);

        var all = BattlefieldGroupGatherer
            .AllBattlefieldPermanents(new[] { _alice, _bob })
            .ToList();

        all.Should().HaveCount(2);
        all.Should().Contain(aliceArt);
        all.Should().Contain(bobArt);
    }

    [Fact]
    public void AllBattlefieldPermanents_IncludesControlledNotOwned()
    {
        // Bob owns it (lives in Bob's battlefield zone) but Alice controls it.
        var stolen = AddArtifact("Stolen Widget", owner: _bob, controller: _alice);

        var all = BattlefieldGroupGatherer
            .AllBattlefieldPermanents(new[] { _alice, _bob })
            .ToList();

        all.Should().Contain(stolen);
        // The effective-controller filter the group static applies selects it
        // as one of "permanents Alice controls" even though it sits in Bob's
        // battlefield zone.
        all.Where(p => ReferenceEquals(p.Controller, _alice))
            .Should().ContainSingle().Which.Should().Be(stolen);
    }

    [Fact]
    public void AllBattlefieldPermanents_DedupesRepeatedPlayer()
    {
        var art = AddArtifact("Widget", _alice, _alice);

        var all = BattlefieldGroupGatherer
            .AllBattlefieldPermanents(new[] { _alice, _alice, _alice })
            .ToList();

        all.Should().ContainSingle().Which.Should().Be(art);
    }

    [Fact]
    public void AllBattlefieldPermanents_ExcludesPermanentNotInBattlefieldZone()
    {
        // Card sits in a battlefield collection but its Zone says Graveyard
        // (a stale residual) — it must be excluded.
        var a = new Artifact("Ghost", "{1}");
        a.ChangeOwner(_alice);
        a.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Graveyard);

        BattlefieldGroupGatherer
            .AllBattlefieldPermanents(new[] { _alice, _bob })
            .Should().BeEmpty();
    }

    [Fact]
    public void WholeBattlefield_ProviderReReadsPlayersOnEachCall()
    {
        AddArtifact("Widget", _alice, _alice);

        var provider = BattlefieldGroupGatherer.WholeBattlefield(() => new[] { _alice, _bob });

        provider().Should().HaveCount(1);

        // A later entrant is picked up on the next call (live membership).
        AddArtifact("Late Widget", _bob, _bob);
        provider().Should().HaveCount(2);
    }
}
