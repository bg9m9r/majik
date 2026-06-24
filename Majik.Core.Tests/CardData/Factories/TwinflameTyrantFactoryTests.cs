using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TwinflameTyrantFactory"/>.
///
/// Card: Twinflame Tyrant — Creature — Dragon {3}{R}{R} 3/5 (Outlaws of
/// Thunder Junction).
///   "Flying
///    If a source you control would deal damage to an opponent or a
///    permanent an opponent controls, it deals double that damage instead."
///
/// Covers:
///   - Identity (name, type, subtype, mana cost, power/toughness,
///     owner/controller).
///   - Flying keyword marker.
///   - Single-arg shape path: no bus interaction.
///   - Asymmetric doubling: source you control + target opponent /
///     opponent's creature / opponent's planeswalker.
///   - Negative: source-controller mismatch (opponent's source) does not
///     double.
///   - Negative: target-controller mismatch (own permanent) does not double.
///   - Battlefield gate: no doubling while off the battlefield.
///   - Stacking with Furnace of Rath quadruples opponent-targeting damage
///     from controller-side sources.
/// </summary>
[Trait("Color", "R")]
public class TwinflameTyrantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------------
    // Identity + Flying
    // ---------------------------------------------------------------------

    [Fact]
    public void TwinflameTyrant_Identity()
    {
        var c = TwinflameTyrantFactory.Create(_alice);

        c.Name.Should().Be("Twinflame Tyrant");
        c.ManaCost.Should().Be("{3}{R}{R}");
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(5);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TwinflameTyrant_HasFlying()
    {
        var c = TwinflameTyrantFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying");
    }

    [Fact]
    public void SingleArgPath_DoesNotRegisterReplacement()
    {
        var bus = new ReplacementBus();
        _ = TwinflameTyrantFactory.Create(_alice);

        var src = new Creature("src", "{R}", 2, 2) { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: _bob))!
            .Amount.Should().Be(3,
                "single-arg dispatcher path never registers on the bus");
    }

    // ---------------------------------------------------------------------
    // Asymmetric doubling — source you control + target opponent
    // ---------------------------------------------------------------------

    [Fact]
    public void DoublesDamage_FromYourSource_ToOpponent()
    {
        var bus = new ReplacementBus();
        _ = TyrantOnBattlefield(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };

        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(6);
    }

    [Fact]
    public void DoublesDamage_FromYourSource_ToOpponentsCreature()
    {
        var bus = new ReplacementBus();
        _ = TyrantOnBattlefield(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };
        var bobsCreature = new Creature("blocker", "{G}", 3, 3)
            { Owner = _bob, Controller = _bob };

        bus.Apply(new DamageIntent(alicesCreature, 2, TargetCreature: bobsCreature) { IsCombatDamage = true })!
            .Amount.Should().Be(4);
    }

    [Fact]
    public void DoublesDamage_FromYourSource_ToOpponentsPlaneswalker()
    {
        var bus = new ReplacementBus();
        _ = TyrantOnBattlefield(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };
        var bobsWalker = new Planeswalker("Liliana", "{2}{B}{B}", 4)
            { Owner = _bob, Controller = _bob };

        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlaneswalker: bobsWalker))!
            .Amount.Should().Be(6);
    }

    [Fact]
    public void DoublesDamage_FromControllerPlayerSource_ToOpponent()
    {
        // Player-as-source path (a spell whose intent carries the caster as
        // source, e.g. a burn spell controlled by Alice).
        var bus = new ReplacementBus();
        _ = TyrantOnBattlefield(_alice, bus);

        bus.Apply(new DamageIntent(_alice, 3, TargetPlayer: _bob))!
            .Amount.Should().Be(6,
                "a Player source controlled by the Tyrant's controller — Alice — passes the gate");
    }

    // ---------------------------------------------------------------------
    // Negative cases
    // ---------------------------------------------------------------------

    [Fact]
    public void DoesNotDouble_FromOpponentsSource_ToYou()
    {
        // Bob attacks Alice — neither side is the Tyrant's controller for
        // the source check (Bob controls the source), gate fails.
        var bus = new ReplacementBus();
        _ = TyrantOnBattlefield(_alice, bus);

        var bobsCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _bob, Controller = _bob };

        bus.Apply(new DamageIntent(bobsCreature, 3, TargetPlayer: _alice) { IsCombatDamage = true })!
            .Amount.Should().Be(3,
                "Tyrant only doubles your-source-to-opponent damage; opponent's source skips");
    }

    [Fact]
    public void DoesNotDouble_FromYourSource_ToYourCreature()
    {
        // Alice's pinger hits Alice's other creature — controller-side
        // target, gate fails.
        var bus = new ReplacementBus();
        _ = TyrantOnBattlefield(_alice, bus);

        var alicesPinger = new Creature("pinger", "{R}", 1, 1)
            { Owner = _alice, Controller = _alice };
        var alicesOther = new Creature("buddy", "{G}", 2, 2)
            { Owner = _alice, Controller = _alice };

        bus.Apply(new DamageIntent(alicesPinger, 1, TargetCreature: alicesOther))!
            .Amount.Should().Be(1);
    }

    [Fact]
    public void DoesNotDouble_WhenNotOnBattlefield()
    {
        var bus = new ReplacementBus();
        _ = TwinflameTyrantFactory.Create(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlayer: _bob))!
            .Amount.Should().Be(3,
                "Tyrant is registered but off-battlefield — predicate fails");
    }

    // ---------------------------------------------------------------------
    // Multi-source interactions
    // ---------------------------------------------------------------------

    [Fact]
    public void StacksWithFurnaceOfRath_QuadrupleOpponentDamage()
    {
        // Furnace doubles symmetrically first (3 -> 6), the Tyrant's
        // predicate then sees the rewritten intent and doubles again
        // (6 -> 12) because source / target unchanged.
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);
        _ = TyrantOnBattlefield(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(12);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static Creature TyrantOnBattlefield(Player owner, ReplacementBus bus)
    {
        var c = TwinflameTyrantFactory.Create(owner, bus);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Enchantment FurnaceOnBattlefield(Player owner, ReplacementBus bus)
    {
        var c = FurnaceOfRathFactory.Create(owner, bus);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
