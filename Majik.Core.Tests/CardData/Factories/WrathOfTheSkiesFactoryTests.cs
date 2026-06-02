using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Wrath of the Skies (Modern Horizons 3, {X}{W}{W}, Sorcery).
///
/// Oracle: "You may pay {E}{E}{E}{E} rather than pay this spell's mana cost.
///          Destroy each nonland permanent with mana value X or less."
///
/// Coverage:
///   * Card shape (Sorcery + cost) + NamedCardFactory dispatch.
///   * Sweep at X=2 destroys nonland permanents with mv 0/1/2 on every
///     supplied battlefield; lands and mv-3 permanents survive.
///   * EnergyAlternativeCost legality gating (≥4 energy required) +
///     drain on resolution + X=0 sweep (token / mv-0 nonland only).
///   * Energy alt-cost probe yields a candidate when the caster has
///     ≥4 energy; suppresses when caster has &lt;4.
/// </summary>
[Trait("Color", "W")]
public class WrathOfTheSkiesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Shape ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasSorceryShape_XWW()
    {
        var w = WrathOfTheSkiesFactory.Create(_alice);

        w.Name.Should().Be("Wrath of the Skies");
        w.ManaCost.Should().Be("{X}{W}{W}");
        w.HasType(CardType.Sorcery).Should().BeTrue();
        w.Owner.Should().BeSameAs(_alice);
        w.Controller.Should().BeSameAs(_alice);
    }
    // ── Resolve — sweep semantics ────────────────────────────────────────────

    [Fact]
    public void Resolve_AtXEquals2_DestroysNonlandPermanentsWithMvLeq2_OnEveryBattlefield()
    {
        // Alice's battlefield: 0-cost token-shaped artifact, 1-cost
        // creature, 2-cost enchantment, 3-cost creature (survivor),
        // a Plains (survivor — land).
        var aliceMv0 = SeedArtifact(_alice, "Alice-Mox", manaCost: "");
        var aliceMv1 = SeedCreature(_alice, "Alice-1drop", manaCost: "{W}");
        var aliceMv2 = SeedEnchantment(_alice, "Alice-Aura", manaCost: "{1}{W}");
        var aliceMv3 = SeedCreature(_alice, "Alice-3drop", manaCost: "{2}{W}");
        var alicePlains = SeedLand(_alice, "Alice-Plains");

        // Bob's battlefield: a 2-cost artifact (dies) + a 4-cost creature
        // (survives).
        var bobMv2 = SeedArtifact(_bob, "Bob-Signet", manaCost: "{2}");
        var bobMv4 = SeedCreature(_bob, "Bob-Titan", manaCost: "{2}{W}{W}");

        var effects = WrathOfTheSkiesFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob }, x: 2);
        foreach (var e in effects) e.Execute();

        // Alice's battlefield: only the 3-drop creature + Plains survive.
        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceMv3, alicePlains });
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceMv0, aliceMv1, aliceMv2 });

        // Bob's battlefield: only the 4-cost creature survives.
        _bob.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { bobMv4 });
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(
            new ICard[] { bobMv2 });

        // Zone fields updated.
        aliceMv0.Zone.Should().Be(ZoneType.Graveyard);
        aliceMv1.Zone.Should().Be(ZoneType.Graveyard);
        aliceMv2.Zone.Should().Be(ZoneType.Graveyard);
        bobMv2.Zone.Should().Be(ZoneType.Graveyard);
        aliceMv3.Zone.Should().Be(ZoneType.Battlefield);
        alicePlains.Zone.Should().Be(ZoneType.Battlefield);
        bobMv4.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_DoesNotDestroyLands_RegardlessOfManaValue()
    {
        // Lands are universally mv 0; they should NEVER be destroyed even
        // at high X. Pair with a 0-cost artifact to confirm only the
        // artifact dies.
        var alicePlains = SeedLand(_alice, "Alice-Plains");
        var aliceMox = SeedArtifact(_alice, "Alice-Mox", manaCost: "");

        var effects = WrathOfTheSkiesFactory.BuildResolveEffect(
            _alice, new[] { _alice }, x: 99);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { alicePlains });
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceMox });
    }

    [Fact]
    public void Resolve_AtXEquals2_DoesNotDestroyMv3Permanent()
    {
        // Boundary: mv 3 must survive at X=2 (the rider is mv ≤ X).
        var aliceMv3 = SeedCreature(_alice, "Alice-Bear", manaCost: "{2}{W}");

        var effects = WrathOfTheSkiesFactory.BuildResolveEffect(
            _alice, new[] { _alice }, x: 2);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceMv3 });
        aliceMv3.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Energy alt-cost — CR 118.9 + CR 106.13 ──────────────────────────────

    [Fact]
    public void EnergyAltCost_PrintedAmount_IsFour()
    {
        var altCost = WrathOfTheSkiesFactory.BuildAlternativeCost();
        altCost.EnergyAmount.Should().Be(4);
        altCost.AlternativeManaCost.IsZero.Should().BeTrue();
    }

    [Fact]
    public void EnergyAltCost_AcceptsCaster_WhenCasterHasFourEnergy()
    {
        _alice.GainEnergy(4);
        var card = WrathOfTheSkiesFactory.Create(_alice);
        var altCost = WrathOfTheSkiesFactory.BuildAlternativeCost();

        altCost.CanCastFor(card, _alice).Should().BeTrue();
    }

    [Fact]
    public void EnergyAltCost_RejectsCaster_WhenCasterHasLessThanFourEnergy()
    {
        _alice.GainEnergy(3);
        var card = WrathOfTheSkiesFactory.Create(_alice);
        var altCost = WrathOfTheSkiesFactory.BuildAlternativeCost();

        altCost.CanCastFor(card, _alice).Should().BeFalse();
    }

    [Fact]
    public void EnergyAltCost_OnResolved_DrainsExactlyFourEnergy()
    {
        _alice.GainEnergy(7); // 7 → 3 after a 4-pip pay.
        var card = WrathOfTheSkiesFactory.Create(_alice);
        var altCost = WrathOfTheSkiesFactory.BuildAlternativeCost();

        altCost.OnResolved(card, _alice);

        _alice.EnergyCounters.Should().Be(3);
    }

    [Fact]
    public void EnergyAltCost_XEqualsZero_OnlyDestroysMvZeroNonlandPermanents()
    {
        // Per CR 107.3b — when an alt-cost replaces a spell's mana cost
        // and doesn't specify X, X is treated as 0. Wrath of the Skies
        // cast via the energy alt-cost sweeps only mv-0 nonland
        // permanents (tokens / mox-shaped 0-cost permanents).
        var aliceMox = SeedArtifact(_alice, "Alice-Mox", manaCost: "");
        var aliceMv1 = SeedCreature(_alice, "Alice-1drop", manaCost: "{W}");
        var alicePlains = SeedLand(_alice, "Alice-Plains");

        var effects = WrathOfTheSkiesFactory.BuildResolveEffect(
            _alice, new[] { _alice }, x: 0);
        foreach (var e in effects) e.Execute();

        // Mox (mv 0) destroyed; 1-drop + land survive.
        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceMv1, alicePlains });
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceMox });
    }

    // ── Probe registry integration ──────────────────────────────────────────

    [Fact]
    public void EnergyProbe_YieldsCandidate_WhenCasterHasEnoughEnergy()
    {
        _alice.GainEnergy(4);
        var card = WrathOfTheSkiesFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var probe = new EnergyAltCostProbe(EnergyAltCostProbe.DefaultLookup);
        var ctx = MakeCtx();

        var candidates = probe.CandidatesFor(card, _alice, ctx).ToList();

        candidates.Should().HaveCount(1);
        candidates[0].Should().BeOfType<EnergyAlternativeCost>()
            .Which.EnergyAmount.Should().Be(4);
    }

    [Fact]
    public void EnergyProbe_SuppressesCandidate_WhenCasterHasInsufficientEnergy()
    {
        _alice.GainEnergy(3);
        var card = WrathOfTheSkiesFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var probe = new EnergyAltCostProbe(EnergyAltCostProbe.DefaultLookup);
        var ctx = MakeCtx();

        probe.CandidatesFor(card, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void DefaultRegistry_ContainsEnergyProbe()
    {
        var registry = AlternativeCostProbeRegistry.CreateDefault();
        registry.Probes.OfType<EnergyAltCostProbe>().Should().ContainSingle();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private GameContext MakeCtx()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        return new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: stack);
    }

    private static Creature SeedCreature(Player owner, string name, string manaCost)
    {
        var c = new Creature(name, manaCost, power: 1, toughness: 1);
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

    private static Enchantment SeedEnchantment(Player owner, string name, string manaCost)
    {
        var e = new Enchantment(name, manaCost);
        e.SetOwner(owner);
        e.SetController(owner);
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
        return e;
    }

    private static Artifact SeedArtifact(Player owner, string name, string manaCost)
    {
        var a = new Artifact(name, manaCost);
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}
