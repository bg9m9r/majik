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
/// Unit tests for <see cref="AngrathsMaraudersFactory"/>.
///
/// Card: Angrath's Marauders — Creature — Human Pirate {4}{R}{R} 4/4
/// (Ixalan).
///   "If a source you control would deal damage to an opponent or a
///    permanent an opponent controls, it deals double that damage to
///    that player or permanent instead."
///
/// Covers:
///   - Identity (name, type, subtypes, mana cost, power/toughness,
///     owner/controller).
///   - NamedCardFactory dispatcher entry.
///   - Single-arg shape path: no bus interaction.
///   - Asymmetric doubling: source you control + target opponent /
///     opponent's creature / opponent's planeswalker.
///   - Negative: source-controller mismatch (opponent's source) does
///     not double.
///   - Negative: target-controller mismatch (own creature) does not
///     double.
///   - Battlefield gate: no doubling while off the battlefield.
///   - Stacking with Furnace of Rath quadruples opponent-targeting
///     damage from controller-side sources.
/// </summary>
public class AngrathsMaraudersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------------
    // Identity + dispatch
    // ---------------------------------------------------------------------

    [Fact]
    public void AngrathsMarauders_Identity()
    {
        var c = AngrathsMaraudersFactory.Create(_alice);

        c.Name.Should().Be("Angrath's Marauders");
        c.ManaCost.Should().Be("{4}{R}{R}");
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AngrathsMarauders_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Angrath's Marauders", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Angrath's Marauders");
        c.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
    }

    [Fact]
    public void SingleArgPath_DoesNotRegisterReplacement()
    {
        var bus = new ReplacementBus();
        _ = AngrathsMaraudersFactory.Create(_alice);

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
        _ = MaraudersOnBattlefield(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };

        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(6);
    }

    [Fact]
    public void DoublesDamage_FromYourSource_ToOpponentsCreature()
    {
        var bus = new ReplacementBus();
        _ = MaraudersOnBattlefield(_alice, bus);

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
        _ = MaraudersOnBattlefield(_alice, bus);

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
        // Player-as-source path (Lightning Bolt-style direct damage from
        // a spell whose intent carries the caster as source).
        var bus = new ReplacementBus();
        _ = MaraudersOnBattlefield(_alice, bus);

        bus.Apply(new DamageIntent(_alice, 3, TargetPlayer: _bob))!
            .Amount.Should().Be(6,
                "a Player source controlled by Marauders' controller — Alice — passes the gate");
    }

    // ---------------------------------------------------------------------
    // Negative cases
    // ---------------------------------------------------------------------

    [Fact]
    public void DoesNotDouble_FromOpponentsSource_ToOpponent()
    {
        // Bob attacks Alice — neither side is Marauders' controller,
        // gate fails on source-controller check.
        var bus = new ReplacementBus();
        _ = MaraudersOnBattlefield(_alice, bus);

        var bobsCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _bob, Controller = _bob };

        bus.Apply(new DamageIntent(bobsCreature, 3, TargetPlayer: _alice) { IsCombatDamage = true })!
            .Amount.Should().Be(3,
                "Marauders only doubles your-source-to-opponent damage; opponent's source skips");
    }

    [Fact]
    public void DoesNotDouble_FromYourSource_ToYourself()
    {
        // Alice's creature pings Alice (sandbox case) — source matches
        // but target isn't an opponent, gate fails.
        var bus = new ReplacementBus();
        _ = MaraudersOnBattlefield(_alice, bus);

        var alicesCreature = new Creature("self-pinger", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };

        bus.Apply(new DamageIntent(alicesCreature, 2, TargetPlayer: _alice))!
            .Amount.Should().Be(2,
                "target is the Marauders' controller — no doubling");
    }

    [Fact]
    public void DoesNotDouble_FromYourSource_ToYourCreature()
    {
        // Alice's pinger hits Alice's other creature — controller-side
        // target, gate fails.
        var bus = new ReplacementBus();
        _ = MaraudersOnBattlefield(_alice, bus);

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
        _ = AngrathsMaraudersFactory.Create(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlayer: _bob))!
            .Amount.Should().Be(3,
                "Marauders is registered but off-battlefield — predicate fails");
    }

    // ---------------------------------------------------------------------
    // Multi-source interactions
    // ---------------------------------------------------------------------

    [Fact]
    public void StacksWithFurnaceOfRath_QuadrupleOpponentDamage()
    {
        // Furnace doubles symmetrically first (3 -> 6), Marauders'
        // predicate then sees the rewritten intent and doubles again
        // (6 -> 12) because source / target unchanged.
        var bus = new ReplacementBus();
        _ = FurnaceOnBattlefield(_alice, bus);
        _ = MaraudersOnBattlefield(_alice, bus);

        var alicesCreature = new Creature("attacker", "{R}", 2, 2)
            { Owner = _alice, Controller = _alice };
        bus.Apply(new DamageIntent(alicesCreature, 3, TargetPlayer: _bob) { IsCombatDamage = true })!
            .Amount.Should().Be(12);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static Creature MaraudersOnBattlefield(Player owner, ReplacementBus bus)
    {
        var c = AngrathsMaraudersFactory.Create(owner, bus);
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
