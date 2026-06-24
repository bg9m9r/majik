using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GratuitousViolenceFactory"/>.
///
/// Card: Gratuitous Violence — Enchantment {2}{R}{R}{R} (Gatecrash).
///   "If a creature you control would deal damage to a permanent or player,
///    it deals double that damage instead."
///
/// Covers only the card's UNIQUE behaviour (the CardFactoryContractTests
/// already assert dispatch + well-formedness for every implemented card):
///   - Identity (non-vanilla mana cost {2}{R}{R}{R}).
///   - Single-arg shape path: no replacement registered against an external bus.
///   - Doubling fires for a creature YOU control hitting any target shape
///     (player / creature / planeswalker), and to friendly or opposing targets
///     alike ("a permanent or player" — no opponent gate, unlike Gisela).
///   - Doubling does NOT fire for an opponent's creature source ("you control").
///   - Doubling does NOT fire for a non-creature source you control (a Player /
///     burn-spell source) — narrower than Furnace of Rath.
///   - Battlefield gate: doubling only fires while the card is on the
///     battlefield.
///   - Stacking with a second Gratuitous Violence quadruples damage.
/// </summary>
[Trait("Color", "R")]
public class GratuitousViolenceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------------
    // Identity
    // ---------------------------------------------------------------------

    [Fact]
    public void GratuitousViolence_Identity()
    {
        var c = GratuitousViolenceFactory.Create(_alice);

        c.Name.Should().Be("Gratuitous Violence");
        c.ManaCost.Should().Be("{2}{R}{R}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SingleArgPath_DoesNotRegisterReplacement()
    {
        // Shape-only path: caller's bus is not touched.
        var bus = new ReplacementBus();
        _ = GratuitousViolenceFactory.Create(_alice);

        var src = AliceCreature();
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: _bob))!
            .Amount.Should().Be(3, "single-arg dispatcher path never registers on the bus");
    }

    // ---------------------------------------------------------------------
    // Doubling — creature you control, any target shape
    // ---------------------------------------------------------------------

    [Fact]
    public void DoublesDamage_YourCreature_To_Player()
    {
        var bus = new ReplacementBus();
        _ = OnBattlefield(_alice, bus);

        bus.Apply(new DamageIntent(AliceCreature(), 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(6);
    }

    [Fact]
    public void DoublesDamage_YourCreature_To_Creature_FriendlyOrOpposing()
    {
        var bus = new ReplacementBus();
        _ = OnBattlefield(_alice, bus);

        var opposing = new Creature("opp", "{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bus.Apply(new DamageIntent(AliceCreature(), 2, TargetCreature: opposing) { IsCombatDamage = true })!
            .Amount.Should().Be(4);

        // "a permanent or player" has no opponent gate — friendly fire (e.g.
        // fight / ping your own creature) doubles too, unlike Gisela.
        var friendly = new Creature("mine", "{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(AliceCreature(), 2, TargetCreature: friendly))!
            .Amount.Should().Be(4, "no opponent-target restriction — friendly targets double too");
    }

    [Fact]
    public void DoublesDamage_YourCreature_To_Planeswalker()
    {
        var bus = new ReplacementBus();
        _ = OnBattlefield(_alice, bus);

        var pw = new Planeswalker("Chandra", "{2}{R}{R}", 4) { Owner = _bob, Controller = _bob };
        bus.Apply(new DamageIntent(AliceCreature(), 3, TargetPlaneswalker: pw))!
            .Amount.Should().Be(6);
    }

    [Fact]
    public void DoublesNonCombatDamage()
    {
        // No combat-damage filter — a creature's "deals N damage" ability doubles.
        var bus = new ReplacementBus();
        _ = OnBattlefield(_alice, bus);

        bus.Apply(new DamageIntent(AliceCreature(), 3, TargetPlayer: _bob))!
            .Amount.Should().Be(6, "every damage intent from a creature you control doubles, combat or not");
    }

    // ---------------------------------------------------------------------
    // Gates: "a creature you control"
    // ---------------------------------------------------------------------

    [Fact]
    public void DoesNotDouble_OpponentsCreatureSource()
    {
        var bus = new ReplacementBus();
        _ = OnBattlefield(_alice, bus);

        var bobsCreature = new Creature("bobs", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        bus.Apply(new DamageIntent(bobsCreature, 3, TargetPlayer: _alice))!
            .Amount.Should().Be(3, "asymmetric — only creatures YOU control double");
    }

    [Fact]
    public void DoesNotDouble_NonCreatureSourceYouControl()
    {
        // "a creature you control" — a Player source (e.g. a burn spell you cast)
        // is yours but is not a creature, so it does not double.
        var bus = new ReplacementBus();
        _ = OnBattlefield(_alice, bus);

        bus.Apply(new DamageIntent(_alice, 3, TargetPlayer: _bob))!
            .Amount.Should().Be(3, "non-creature source does not qualify, even if you control it");
    }

    // ---------------------------------------------------------------------
    // Battlefield gate
    // ---------------------------------------------------------------------

    [Fact]
    public void DoesNotDouble_WhenNotOnBattlefield()
    {
        var bus = new ReplacementBus();
        _ = GratuitousViolenceFactory.Create(_alice, bus); // registered but off-battlefield

        bus.Apply(new DamageIntent(AliceCreature(), 3, TargetPlayer: _bob))!
            .Amount.Should().Be(3, "registered but off-battlefield — predicate fails");
    }

    // ---------------------------------------------------------------------
    // Stacking
    // ---------------------------------------------------------------------

    [Fact]
    public void TwoCopies_QuadrupleDamage()
    {
        // Per-effect dedup (CR 616.1c) is per-instance — two copies each fire
        // once on the same intent: 3 -> 6 -> 12.
        var bus = new ReplacementBus();
        _ = OnBattlefield(_alice, bus);
        _ = OnBattlefield(_alice, bus);

        bus.Apply(new DamageIntent(AliceCreature(), 3, TargetPlayer: _bob))!
            .Amount.Should().Be(12);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private Creature AliceCreature()
        => new("src", "{R}", 2, 2) { Owner = _alice, Controller = _alice };

    private static Enchantment OnBattlefield(Player owner, ReplacementBus bus)
    {
        var c = GratuitousViolenceFactory.Create(owner, bus);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
