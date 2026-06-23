using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Vengeful Bloodwitch (Duskmourn: House of Horror, {1}{B}) — a
/// pure-JSON card consuming the declarative <c>whenever_another_creature_dies</c>
/// trigger with <c>youControlOnly</c> + <c>includeSelf</c>, paired with the
/// <c>lose_life_target</c> (subject "target", filter "opponent") +
/// <c>gain_life_self</c> effect verbs.
///
///   "Whenever this creature or another creature you control dies, target
///    opponent loses 1 life and you gain 1 life."
///
/// The "this creature OR another creature you control" wording is the key shape
/// difference vs Vindictive Vampire ("another creature you control" — no
/// includeSelf): the Bloodwitch's OWN death also fires the trigger (CR 603.6c).
/// The drain is a SINGLE targeted opponent (CR 102.2 / 115.1), not "each
/// opponent" — exercising the new <c>opponent</c> target filter.
/// </summary>
[Trait("Color", "B")]
public class VengefulBloodwitchFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    [Fact]
    public void VengefulBloodwitch_Identity()
    {
        var c = VengefulBloodwitchFactory.Create(_alice);

        c.Name.Should().Be("Vengeful Bloodwitch");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        c.GetPower().Should().Be(1);
        c.GetToughness().Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.TargetRequests.Should().ContainSingle(
            "the drain targets a single opponent (CR 102.2 / 115.1)");
        trigger.TargetRequests[0].Description.Should().Contain("opponent");
    }

    [Fact]
    public void VengefulBloodwitch_AnotherCreatureYouControlDies_TriggerMatches()
    {
        var witch = VengefulBloodwitchFactory.Create(_alice);
        witch.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = witch.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeTrue();
    }

    [Fact]
    public void VengefulBloodwitch_SelfDies_StillTriggers()
    {
        // includeSelf: true — "this creature OR another creature you control"
        // (CR 603.6c). The Bloodwitch's own death fires its drain.
        var witch = VengefulBloodwitchFactory.Create(_alice);
        witch.SetZone(ZoneType.Battlefield);

        var trigger = witch.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(witch, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeTrue(
            "includeSelf — the Bloodwitch's own death fires the trigger (CR 603.6c)");
    }

    [Fact]
    public void VengefulBloodwitch_OpponentCreatureDies_DoesNotTrigger()
    {
        var witch = VengefulBloodwitchFactory.Create(_alice);
        witch.SetZone(ZoneType.Battlefield);

        var enemy = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        enemy.SetOwner(_bob);
        enemy.SetController(_bob);

        var trigger = witch.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(enemy, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeFalse(
            "youControlOnly excludes an opponent's creature dying (CR 109.5)");
    }

    [Fact]
    public void VengefulBloodwitch_OpponentTargetFilter_ExcludesController()
    {
        // CR 102.2 — "target opponent" offers only players OTHER than the
        // resolving controller; the controller cannot drain themselves.
        var witch = VengefulBloodwitchFactory.Create(_alice);
        var trigger = witch.Abilities.OfType<TriggeredAbility>().Single();
        var req = trigger.TargetRequests.Single();

        var candidates = req.CandidateGatherer!(NewContext());

        candidates.Should().Contain(_bob, "Bob is Alice's opponent");
        candidates.Should().NotContain(_alice, "the controller is not their own opponent (CR 102.2)");
    }

    [Fact]
    public async Task VengefulBloodwitch_Resolution_DrainsTargetOpponent_GainsControllerLife()
    {
        var witch = VengefulBloodwitchFactory.Create(_alice);
        var trigger = witch.Abilities.OfType<TriggeredAbility>().Single();

        // The drain reads its target off ChosenTargets at slot 0; the lifegain
        // side is untargeted. Resolve both effect halves against a context whose
        // chosen target is the opponent (Bob).
        var ctx = ResolutionContext.For(
            _alice, agent: null, game: null,
            chosenTargets: new[] { new object[] { _bob } });

        foreach (var effect in trigger.Effects)
            await effect.ExecuteAsync(ctx);

        _bob.LifeTotal.Should().Be(19, "target opponent loses 1 life (CR 119.3)");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life (CR 119.3)");
    }
}
