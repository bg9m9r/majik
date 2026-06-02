using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FurnaceOfRathFactory"/>.
///
/// Card: Furnace of Rath — Enchantment {2}{R}{R} (Tempest).
///   "If a source would deal damage to a creature or player, it deals
///    double that damage to that creature or player instead."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatcher entry.
///   - Single-arg shape path: no replacement registered against an
///     external bus.
///   - Symmetric all-damage doubling: combat + non-combat, every target
///     shape (player / creature / planeswalker), both directions.
///   - Battlefield gate: doubling only fires while the card is on the
///     battlefield.
///   - Stacking with a second Furnace quadruples damage (per-effect
///     dedup is per-instance, two instances each fire once).
/// </summary>
[Trait("Color", "R")]
public class FurnaceOfRathFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------------
    // Identity + dispatch
    // ---------------------------------------------------------------------

    [Fact]
    public void FurnaceOfRath_Identity()
    {
        var c = FurnaceOfRathFactory.Create(_alice);

        c.Name.Should().Be("Furnace of Rath");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void FurnaceOfRath_SingleArgPath_DoesNotRegisterReplacement()
    {
        // Shape-only path: caller's bus is not touched.
        var bus = new ReplacementBus();
        _ = FurnaceOfRathFactory.Create(_alice);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        var intent = new DamageIntent(src, 3, TargetPlayer: _alice);
        bus.Apply(intent)!.Amount.Should().Be(3,
            "single-arg dispatcher path never registers on the bus");
    }

    // ---------------------------------------------------------------------
    // Symmetric all-damage doubling
    // ---------------------------------------------------------------------

    [Fact]
    public void DoublesDamage_To_Player_Symmetric()
    {
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);

        var bobsCreature = new Creature("src", "{R}", 2, 2) { Owner = _bob, Controller = _bob };

        // Bob's creature deals damage to Alice (Furnace's controller).
        bus.Apply(new DamageIntent(bobsCreature, 3, TargetPlayer: _alice) { IsCombatDamage = true })!
            .Amount.Should().Be(6);

        // Alice's creature deals damage to Bob.
        var alicesCreature = new Creature("a", "{R}", 2, 2) { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(6,
                "Furnace doubles symmetrically — both players' damage doubles");
    }

    [Fact]
    public void DoublesDamage_To_Creature()
    {
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        var target = new Creature("target", "{G}", 2, 2) { Owner = _alice, Controller = _alice };

        bus.Apply(new DamageIntent(src, 2, TargetCreature: target) { IsCombatDamage = true })!
            .Amount.Should().Be(4);
    }

    [Fact]
    public void DoublesDamage_To_Planeswalker()
    {
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        var pw = new Planeswalker("Chandra", "{2}{R}{R}", 4) { Owner = _alice, Controller = _alice };

        bus.Apply(new DamageIntent(src, 3, TargetPlaneswalker: pw))!
            .Amount.Should().Be(6);
    }

    [Fact]
    public void DoublesNonCombatDamage()
    {
        // Furnace has no combat-damage filter — Lightning Bolt-style
        // targeted damage doubles too.
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);

        var intent = new DamageIntent(_bob, 3, TargetPlayer: _alice);
        // IsCombatDamage defaults to false — non-combat spell damage.

        bus.Apply(intent)!.Amount.Should().Be(6,
            "Furnace doubles every damage intent, combat or not");
    }

    // ---------------------------------------------------------------------
    // Battlefield gate
    // ---------------------------------------------------------------------

    [Fact]
    public void DoesNotDouble_WhenNotOnBattlefield()
    {
        // Construct + register but don't move to battlefield — gate
        // short-circuits.
        var bus = new ReplacementBus();
        _ = FurnaceOfRathFactory.Create(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: _alice))!
            .Amount.Should().Be(3,
                "Furnace is registered but off-battlefield — predicate fails");
    }

    // ---------------------------------------------------------------------
    // Stacking / multi-source interactions
    // ---------------------------------------------------------------------

    [Fact]
    public void TwoCopiesOfFurnace_QuadrupleDamage()
    {
        // Per-effect dedup (CR 616.1c) is per-instance — two Furnaces
        // each fire once on the same intent: 3 -> 6 -> 12.
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);
        _ = FurnaceOnBattlefield(_alice, bus);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: _alice))!
            .Amount.Should().Be(12);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static Enchantment FurnaceOnBattlefield(Player owner, ReplacementBus bus)
    {
        var c = FurnaceOfRathFactory.Create(owner, bus);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
