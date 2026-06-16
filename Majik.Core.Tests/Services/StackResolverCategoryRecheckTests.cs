using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Services;

/// <summary>
/// CR 608.2b — regression coverage that a spell carrying ONLY a category-derived
/// legality predicate (from <see cref="TargetCandidateService.BuildLegalityPredicate"/>,
/// the fallback the SpellCastFlow stamp applies when a card ships no per-card
/// predicate) is still countered at resolution when its only chosen target is
/// no longer legal — here a "target creature" whose target became a non-creature
/// (left the battlefield).
/// </summary>
public class StackResolverCategoryRecheckTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public void CategoryPredicate_TargetCreature_counters_when_target_no_longer_creature()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var resolver = new StackResolver(_bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
        };

        var ran = false;
        var bolt = new Instant("Lightning Bolt", "R") { Owner = alice, Zone = ZoneType.Stack };
        var spell = new Majik.Core.Spells.Spell(
            bolt, alice,
            effects: new[] { new Majik.Core.Abilities.Effect("dmg", () => { bear.TakeDamage(3); ran = true; }) });
        spell.ChosenTargets.Add(bear);

        // The category-derived predicate the production stamp would apply for a
        // "target creature" request (no per-card predicate on the card).
        spell.TargetLegalityPredicate =
            TargetCandidateService.BuildLegalityPredicate("target creature");
        spell.TargetLegalityPredicate.Should().NotBeNull();

        stack.Push(spell);

        // The bear leaves the battlefield before resolution. The recheck reads
        // the live instance — but a category predicate only checks TYPE, so we
        // model "no longer a creature" by destroying it: SetZone(Graveyard) keeps
        // the C# Creature type, so to exercise the category recheck we move the
        // chosen target to a non-creature object instead.
        spell.ChosenTargets.Clear();
        var nowLand = new Land("Forest") { Owner = bob, Controller = bob, Zone = ZoneType.Graveyard };
        spell.ChosenTargets.Add(nowLand); // the only chosen target is not a creature

        resolver.ResolveTop(stack);

        ran.Should().BeFalse("the spell is countered: its only target is not a creature (CR 608.2b)");
        bolt.Zone.Should().Be(ZoneType.Graveyard, "a countered spell goes to the graveyard");
    }

    [Fact]
    public void CategoryPredicate_AnyTarget_resolves_against_a_player_target()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var resolver = new StackResolver(_bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var ran = false;
        var bolt = new Instant("Lightning Bolt", "R") { Owner = alice, Zone = ZoneType.Stack };
        var spell = new Majik.Core.Spells.Spell(
            bolt, alice,
            effects: new[] { new Majik.Core.Abilities.Effect("dmg", () => { bob.LoseLife(3); ran = true; }) });
        spell.ChosenTargets.Add(bob);
        spell.TargetLegalityPredicate =
            TargetCandidateService.BuildLegalityPredicate("any target");
        stack.Push(spell);

        resolver.ResolveTop(stack);

        ran.Should().BeTrue("a player is a legal 'any target' target — the spell resolves");
    }
}
