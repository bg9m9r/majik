using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FumigateFactory"/> — Fumigate (Aether Revolt,
/// {3}{W}{W}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Destroy all creatures. You gain 1 life for each creature destroyed
///    this way."
///
/// Covers the card's UNIQUE behaviour — a symmetric board wipe with a
/// count-of-kills life-gain rider — plus a single identity assert for the
/// non-vanilla mana cost. Dispatch + well-formedness is asserted for every
/// implemented card by <c>CardFactoryContractTests</c>, so no dispatch test
/// is duplicated here.
/// </summary>
[Trait("Color", "W")]
public class FumigateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Fumigate_Identity_SorceryAt3WW()
    {
        var card = FumigateFactory.Create(_alice);

        card.Name.Should().Be("Fumigate");
        card.ManaCost.Should().Be("{3}{W}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // BuildResolveEffect — symmetric sweep + life per kill
    // -----------------------------------------------------------------------

    [Fact]
    public void Fumigate_Resolve_DestroysAllCreatures_BothBattlefields_AndGainsOneLifePerKill()
    {
        // Alice casts; Bob is the opponent. Three creatures total across both
        // battlefields → the caster gains 3 life (CR 109.5 — the sweep is
        // symmetric; the life-gain counts EVERY creature destroyed this way,
        // not just the caster's own).
        var aliceA = SeedCreature(_alice, "Alice-A");
        var aliceB = SeedCreature(_alice, "Alice-B");
        var bobA = SeedCreature(_bob, "Bob-A");

        var effects = FumigateFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
        foreach (var e in effects) e.Execute();

        // All creatures destroyed → owners' graveyards.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { aliceA, aliceB });
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobA);

        // Caster gained 1 life per creature destroyed (3 kills → 20 + 3 = 23).
        _alice.LifeTotal.Should().Be(23);
        // Opponent's life is untouched — only the caster gains.
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Fumigate_Resolve_LeavesNonCreaturePermanentsAlone_AndCountsOnlyCreatures()
    {
        var creature = SeedCreature(_alice, "Alice-Creature");
        var land = SeedLand(_alice, "Alice-Plains");
        var artifact = SeedArtifact(_alice, "Alice-Mox");

        var effects = FumigateFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
        foreach (var e in effects) e.Execute();

        creature.Zone.Should().Be(ZoneType.Graveyard);
        land.Zone.Should().Be(ZoneType.Battlefield);
        artifact.Zone.Should().Be(ZoneType.Battlefield);

        // Only the single creature was destroyed → +1 life (land/artifact are
        // not "creatures destroyed this way").
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void Fumigate_Resolve_EmptyBattlefields_NoKills_NoLifeGain_CleanNoOp()
    {
        var act = () =>
        {
            var effects = FumigateFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow();
        // Zero creatures destroyed this way → zero life gained (CR — "1 life
        // for each creature destroyed" is a count-of-zero no-op).
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Land SeedLand(Player owner, string name)
    {
        var l = new Land(name);
        l.SetOwner(owner);
        l.SetController(owner);
        owner.Zones.Battlefield.AddCard(l);
        l.SetZone(ZoneType.Battlefield);
        return l;
    }

    private static Artifact SeedArtifact(Player owner, string name)
    {
        var a = new Artifact(name, "");
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}
