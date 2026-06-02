using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Bile Blight (Born of the Gods, {B}{B}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "Target creature and all other creatures with the same name as that
///    creature get -3/-3 until end of turn."
///
/// The same-name <i>sweep</i> sibling of Last Gasp — same -3/-3-until-EOT
/// per-creature effect (CR 514.2, CR 613 Layer 7c), but additionally applied to
/// every other creature sharing the target's name (CR 201.2), like Echoing
/// Truth's "target + all same-name" shape.
///
/// Covers:
///   - Card identity (Instant, {B}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "target creature" request, no modes,
///     no variable X, BotIntent.Removal.
///   - Resolve: -3/-3 to a single target (3/3 -> 0/0).
///   - Resolve: -3/-3 to the target AND every other same-name creature across
///     every battlefield, controller-agnostic; differently-named creatures
///     untouched.
///   - Resolve: off-battlefield target -> no-op, no sweep (CR 608.2b).
///   - Resolve: no ActiveEffectsService wired -> silent no-op, no throw.
/// </summary>
public class BileBlightTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BileBlight_IsInstant_AtCostBB()
    {
        var card = BileBlightFactory.Create(_alice);

        card.Name.Should().Be("Bile Blight");
        card.ManaCost.Should().Be("{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BileBlight()
    {
        var card = NamedCardFactory.Create("Bile Blight", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Bile Blight");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BileBlight_Definition_HasSingleCreatureTarget()
    {
        var def = BileBlightFactory.BuildDefinition(
            new[] { _alice, _bob }, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — single target -3/-3
    // -----------------------------------------------------------------------

    [Fact]
    public void BileBlight_AppliesMinus3Minus3_ToTarget_3x3To0x0()
    {
        // 3/3 creature -> 0/0 after -3/-3 (CR 613 Layer 7c, CR 514.2).
        var creature = NewControlledCreature(_bob, "Trained Armodon", "{1}{G}{G}", 3, 3);

        Resolve(creature);

        creature.Power.Should().Be(0, "Trained Armodon 3/3 with -3/-3 -> 0/0");
        creature.Toughness.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Resolve — the same-name sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void BileBlight_AppliesMinus3Minus3_ToAllSameNameCreatures_AcrossBattlefields()
    {
        // Two copies owned by Bob, a third owned by Alice (caster), plus an
        // unrelated creature that must be left untouched.
        var target = NewControlledCreature(_bob, "Goblin Guide", "{R}", 2, 2);
        var sameNameBobsOther = NewControlledCreature(_bob, "Goblin Guide", "{R}", 2, 2);
        var sameNameAlices = NewControlledCreature(_alice, "Goblin Guide", "{R}", 2, 2);
        var bystander = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}", 4, 5);

        Resolve(target);

        // CR 201.2 — the target and every OTHER same-name creature get -3/-3,
        // regardless of controller (even the caster's own copy).
        target.Power.Should().Be(-1);
        target.Toughness.Should().Be(-1);
        sameNameBobsOther.Power.Should().Be(-1);
        sameNameBobsOther.Toughness.Should().Be(-1);
        sameNameAlices.Power.Should().Be(-1,
            "the same-name sweep ignores controller — even the caster's own copy is hit");
        sameNameAlices.Toughness.Should().Be(-1);

        // The differently-named creature is untouched.
        bystander.Power.Should().Be(4, "only creatures sharing the target's name are affected");
        bystander.Toughness.Should().Be(5);
    }

    [Fact]
    public void BileBlight_LeavesDifferentlyNamedCreatures_Alone()
    {
        var target = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        var other = NewControlledCreature(_bob, "Runeclaw Bear", "{1}{G}", 2, 2);

        Resolve(target);

        target.Toughness.Should().Be(-1);
        other.Power.Should().Be(2, "only creatures sharing the target's name are affected");
        other.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal target
    // -----------------------------------------------------------------------

    [Fact]
    public void BileBlight_TargetNotOnBattlefield_NoOp_NoSweep()
    {
        // The chosen target left the battlefield before resolution. CR 608.2b:
        // illegal target -> the spell does nothing, including no same-name
        // sweep. A same-name creature still on the battlefield is untouched.
        var target = NewControlledCreature(_bob, "Goblin Guide", "{R}", 2, 2);
        var sameNameOnBattlefield = NewControlledCreature(_bob, "Goblin Guide", "{R}", 2, 2);

        _bob.Zones.Battlefield.RemoveCard(target);
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        Resolve(target);

        target.Power.Should().Be(2);
        target.Toughness.Should().Be(2);
        sameNameOnBattlefield.Power.Should().Be(2,
            "no same-name sweep happens when the chosen target itself is illegal");
        sameNameOnBattlefield.Toughness.Should().Be(2);
    }

    [Fact]
    public void BileBlight_NoActiveEffectsService_DoesNotThrow()
    {
        // Shape-only path: target on battlefield but no ContinuousEffectsService
        // wired. Must silently no-op for that creature.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var act = () => Resolve(creature);
        act.Should().NotThrow();

        creature.Power.Should().Be(2);
        creature.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(object targetToken)
    {
        var def = BileBlightFactory.BuildDefinition(
            allPlayers: new[] { _alice, _bob },
            targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(
        Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
