using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Unit tests for
/// <see cref="PreventAllDamageFromColoredSourcesToCreatureShield"/> —
/// backs Burrenton Forge-Tender's "Prevent all damage that would be
/// dealt to target creature this turn by red sources" activated ability
/// (CR 615 + CR 105).
/// </summary>
public class PreventAllDamageFromColoredSourcesToCreatureShieldTests
{
    [Fact]
    public void BlocksDamageFromRedSourceToTargetCreature()
    {
        var alice = new Player("A", 20);
        var bus = new ReplacementBus();
        var target = new Creature("mine", "", 2, 2) { Owner = alice, Controller = alice };

        bus.Register<DamageIntent>(
            new PreventAllDamageFromColoredSourcesToCreatureShield(target, ManaColor.Red));

        // Red source = a card with a {R} pip in its mana cost.
        var redSource = new Creature("Lightning Bolt's Stand-in", "{R}", 1, 1);
        bus.Apply(new DamageIntent(redSource, 3, TargetCreature: target))
            .Should().BeNull("shield prevents red-source damage to the chosen creature");
    }

    [Fact]
    public void PassesDamageFromNonRedSource()
    {
        var alice = new Player("A", 20);
        var bus = new ReplacementBus();
        var target = new Creature("mine", "", 2, 2) { Owner = alice, Controller = alice };

        bus.Register<DamageIntent>(
            new PreventAllDamageFromColoredSourcesToCreatureShield(target, ManaColor.Red));

        var blueSource = new Creature("Counter-spell's Stand-in", "{U}", 1, 1);
        var passed = bus.Apply(new DamageIntent(blueSource, 2, TargetCreature: target));
        passed.Should().NotBeNull("non-red source is not gated by the red shield");
        passed!.Amount.Should().Be(2);
    }

    [Fact]
    public void DoesNotBlockDamageToOtherCreatures()
    {
        var alice = new Player("A", 20);
        var bus = new ReplacementBus();
        var protectee = new Creature("protected", "", 2, 2) { Owner = alice, Controller = alice };
        var bystander = new Creature("bystander", "", 1, 1) { Owner = alice, Controller = alice };

        bus.Register<DamageIntent>(
            new PreventAllDamageFromColoredSourcesToCreatureShield(protectee, ManaColor.Red));

        var redSource = new Creature("red-src", "{R}", 1, 1);
        var passed = bus.Apply(new DamageIntent(redSource, 1, TargetCreature: bystander));
        passed.Should().NotBeNull("shield only protects the chosen creature");
        passed!.Amount.Should().Be(1);
    }

    [Fact]
    public void DoesNotBlockDamageFromPlayerSource()
    {
        // Today some spell damage threads the casting Player as Source —
        // a player is not a "red source" by colour rules, so the shield
        // should not engage. Once spell damage threads the casting ICard
        // the colour read will Just Work.
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);
        var bus = new ReplacementBus();
        var target = new Creature("mine", "", 2, 2) { Owner = alice, Controller = alice };

        bus.Register<DamageIntent>(
            new PreventAllDamageFromColoredSourcesToCreatureShield(target, ManaColor.Red));

        var passed = bus.Apply(new DamageIntent(bob, 3, TargetCreature: target));
        passed.Should().NotBeNull("Player-typed sources are colourless from the shield's perspective");
        passed!.Amount.Should().Be(3);
    }

    [Fact]
    public void ExpiresAtEndOfTurn()
    {
        var alice = new Player("A", 20);
        var target = new Creature("mine", "", 2, 2) { Owner = alice, Controller = alice };
        var shield = new PreventAllDamageFromColoredSourcesToCreatureShield(target, ManaColor.Red);

        shield.ExpiresAtEndOfTurn.Should().BeTrue(
            "shield drops at cleanup per CR 514.2 / IEndOfTurnExpirable");
    }
}
