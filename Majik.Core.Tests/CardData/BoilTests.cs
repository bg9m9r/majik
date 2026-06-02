using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Boil (Stronghold and many reprints, {2}{R}, Instant).
///
/// Oracle: "Destroy all Islands."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Resolve destroys every Island on every supplied player's battlefield,
///     routing each Island to its owner's graveyard (CR 701.7).
///   - Non-Island permanents (Mountains, other lands, creatures) survive.
///   - Symmetric — destroys Islands on the caster's own battlefield too.
///   - Empty battlefield is a clean no-op.
/// </summary>
public class BoilTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Boil_IsInstant_At2R()
    {
        var b = BoilFactory.Create(_alice);

        b.Name.Should().Be("Boil");
        b.ManaCost.Should().Be("{2}{R}");
        b.HasType(CardType.Instant).Should().BeTrue();
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Boil()
    {
        var card = NamedCardFactory.Create("Boil", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Boil");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — Island sweep semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysIslandsOnBothBattlefields_ToOwnerGraveyards()
    {
        // Two Islands per player on the battlefield. Boil sweeps all four;
        // each lands in its owner's graveyard.
        var aliceIslands = new[] { SeedIsland(_alice, "Alice-Island-1"), SeedIsland(_alice, "Alice-Island-2") };
        var bobIslands = new[] { SeedIsland(_bob, "Bob-Island-1"), SeedIsland(_bob, "Bob-Island-2") };

        var effects = BoilFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();

        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(aliceIslands);
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(bobIslands);

        foreach (var island in aliceIslands) island.Zone.Should().Be(ZoneType.Graveyard);
        foreach (var island in bobIslands) island.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_LeavesNonIslandPermanentsAlone()
    {
        // Alice: 1 Island, 1 Mountain, 1 creature, 1 enchantment.
        var aliceIsland = SeedIsland(_alice, "Alice-Island");
        var aliceMountain = SeedMountain(_alice, "Alice-Mountain");
        var aliceCreature = SeedCreature(_alice, "Alice-Bear");
        var aliceEnchantment = SeedEnchantment(_alice, "Alice-Aura");

        var effects = BoilFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Island dies; everything else stays.
        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceMountain, aliceCreature, aliceEnchantment });
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new[] { aliceIsland });
        aliceIsland.Zone.Should().Be(ZoneType.Graveyard);
        aliceMountain.Zone.Should().Be(ZoneType.Battlefield);
        aliceCreature.Zone.Should().Be(ZoneType.Battlefield);
        aliceEnchantment.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_IsSymmetric_DestroysCastersOwnIslands()
    {
        // Alice (caster) owns an Island; Bob owns one too. Both die.
        var aliceIsland = SeedIsland(_alice, "Alice-Island");
        var bobIsland = SeedIsland(_bob, "Bob-Island");

        var effects = BoilFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new[] { aliceIsland });
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new[] { bobIsland });
    }

    [Fact]
    public void Resolve_EmptyBattlefields_IsCleanNoOp()
    {
        var effects = BoilFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Land SeedIsland(Player owner, string name)
    {
        var l = new Land(name, supertypes: null, subtypes: new[] { CardSubtype.Island });
        l.SetOwner(owner);
        l.SetController(owner);
        owner.Zones.Battlefield.AddCard(l);
        l.SetZone(ZoneType.Battlefield);
        return l;
    }

    private static Land SeedMountain(Player owner, string name)
    {
        var l = new Land(name, supertypes: null, subtypes: new[] { CardSubtype.Mountain });
        l.SetOwner(owner);
        l.SetController(owner);
        owner.Zones.Battlefield.AddCard(l);
        l.SetZone(ZoneType.Battlefield);
        return l;
    }

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Enchantment SeedEnchantment(Player owner, string name)
    {
        var e = new Enchantment(name, "");
        e.SetOwner(owner);
        e.SetController(owner);
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
        return e;
    }
}
