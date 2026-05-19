using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class StackResolverTargetRecheckTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public void TargetStillLegal_SpellResolvesNormally()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var resolver = new StackResolver(_bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
        };

        var ran = false;
        var bolt = new Instant("Bolt", "R") { Owner = alice, Zone = ZoneType.Stack };
        var spell = new Majik.Core.Spells.Spell(
            bolt, alice,
            effects: new[] { new Majik.Core.Abilities.Effect("dmg", () => { bear.TakeDamage(3); ran = true; }) });
        spell.ChosenTargets.Add(bear);
        spell.TargetLegalityPredicate = t => t is Creature c && c.Zone == ZoneType.Battlefield;
        stack.Push(spell);

        resolver.ResolveTop(stack);

        ran.Should().BeTrue();
        bear.Damage.Should().Be(3);
    }

    [Fact]
    public void AllTargetsIllegal_AtResolution_SpellIsCountered()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var resolver = new StackResolver(_bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
        };

        var ran = false;
        var bolt = new Instant("Bolt", "R") { Owner = alice, Zone = ZoneType.Stack };
        var spell = new Majik.Core.Spells.Spell(
            bolt, alice,
            effects: new[] { new Majik.Core.Abilities.Effect("dmg", () => { bear.TakeDamage(3); ran = true; }) });
        spell.ChosenTargets.Add(bear);
        spell.TargetLegalityPredicate = t => t is Creature c && c.Zone == ZoneType.Battlefield;
        stack.Push(spell);

        // Bear leaves before resolution.
        bear.SetZone(ZoneType.Graveyard);

        resolver.ResolveTop(stack);

        ran.Should().BeFalse();
        bear.Damage.Should().Be(0);
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }
}
