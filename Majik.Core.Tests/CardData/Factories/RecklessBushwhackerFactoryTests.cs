using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RecklessBushwhackerFactory"/> (Oath of the Gatewatch,
/// {2}{R}).
///
/// Covers:
/// - Identity + named-factory dispatch.
/// - <see cref="RecklessBushwhackerFactory.BuildAlternativeCost"/> shape
///   (Surge {R} bound to the supplied <see cref="TurnState"/>).
/// - ETB triggered ability fires the surge-conditional pump+haste rider:
///   pump applies only when <see cref="Card.WasCastForSurge"/> is true;
///   no-op otherwise (intervening-if collapse — CR 603.4).
/// </summary>
[Trait("Color", "R")]
public class RecklessBushwhackerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Identity()
    {
        var c = RecklessBushwhackerFactory.Create(_alice);

        c.Name.Should().Be("Reckless Bushwhacker");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Berserker).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().Be(_alice);
        c.Controller.Should().Be(_alice);
    }
    [Fact]
    public void BuildAlternativeCost_ReturnsSurgeAltCost_BoundToTurnState()
    {
        var ts = new TurnState();

        var cost = RecklessBushwhackerFactory.BuildAlternativeCost(ts);

        cost.Should().BeOfType<SurgeAlternativeCost>();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));

        // No prior spells: surge gate refuses.
        cost.IsLegalInContext(_alice).Should().BeFalse();

        // After Alice casts a spell this turn, the surge gate unlocks.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        cost.IsLegalInContext(_alice).Should().BeTrue();
    }

    [Fact]
    public void HasSingleEtbTriggeredAbility()
    {
        var c = RecklessBushwhackerFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "ETB intervening-if surge pump+haste trigger");
    }

    [Fact]
    public void EtbTrigger_WhenSurgeWasPaid_PumpsAndGrantsHasteToControllersCreatures()
    {
        // Build a controlled battlefield with one bear; Alice's Bushwhacker
        // enters with WasCastForSurge=true → bear gets +1/+0 + Haste.
        var effects = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var bobBear = new Creature("Bob's Bear", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _bob.Zones.Battlefield.AddCard(bobBear);

        var bushwhacker = RecklessBushwhackerFactory.Create(_alice);
        bushwhacker.SetWasCastForSurge(true);
        // Place the Bushwhacker itself on the battlefield, with the
        // continuous-effects service attached so its own +1/+0 lifts.
        bushwhacker.ActiveEffects = effects;
        bushwhacker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bushwhacker);

        // Fire the ETB trigger body.
        var etb = bushwhacker.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        // Alice's bear pumped + haste.
        bear.GetPower().Should().Be(3);
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        // Bushwhacker itself is also one of Alice's creatures → pumped + haste.
        bushwhacker.GetPower().Should().Be(3);
        CombatAbilities.HasHaste(bushwhacker).Should().BeTrue();

        // Bob's creature untouched.
        bobBear.GetPower().Should().Be(2);
        CombatAbilities.HasHaste(bobBear).Should().BeFalse();
    }

    [Fact]
    public void EtbTrigger_WhenSurgeNotPaid_NoOps()
    {
        // Hard cast (printed mana). WasCastForSurge=false → intervening-if
        // collapses to no-op; no pump, no haste.
        var effects = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var bushwhacker = RecklessBushwhackerFactory.Create(_alice);
        bushwhacker.WasCastForSurge.Should().BeFalse();
        bushwhacker.ActiveEffects = effects;
        bushwhacker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bushwhacker);

        var etb = bushwhacker.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        bear.GetPower().Should().Be(2, "no pump when surge cost wasn't paid");
        CombatAbilities.HasHaste(bear).Should().BeFalse();
        bushwhacker.GetPower().Should().Be(2);
        CombatAbilities.HasHaste(bushwhacker).Should().BeFalse();
    }

    [Fact]
    public void EtbTrigger_NoCreatures_NoOpsCleanly()
    {
        var bushwhacker = RecklessBushwhackerFactory.Create(_alice);
        bushwhacker.SetWasCastForSurge(true);
        bushwhacker.SetZone(ZoneType.Battlefield);

        var etb = bushwhacker.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
    }
}
