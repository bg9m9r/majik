using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SleepFactory"/>.
///
/// Card: Sleep — {2}{U}{U} Sorcery (Seventh Edition / reprints).
/// Oracle text:
///   "Tap all creatures target player controls. Those creatures don't
///    untap during that player's next untap step."
///
/// Covers:
/// - Identity ({2}{U}{U}, blue, Sorcery, mana value 4).
/// - NamedCardFactory dispatch.
/// - SpellDefinition declares one 1..1 "target player" TargetRequest.
/// - Resolve taps every creature the target player controls (CR 701.20).
/// - Resolve marks every tapped creature to skip the target player's next
///   untap step (CR 502.1 via UntapStepRestrictions.MarkPermanentDoesNotUntap).
/// - Non-creature permanents (e.g. lands) of the target player are NOT tapped.
/// - Already-tapped creatures still receive the skip-untap marker.
/// - Empty battlefield (no creatures) → no-op, no crash.
/// - CR 608.2b: illegal target (non-Player) → clean no-op.
/// - One-shot skip-untap cleanup: on the target player's next Untap step,
///   the restriction is lifted (bus-wired path).
/// </summary>
[Trait("Color", "U")]
public class SleepFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public void Dispose()
    {
        UntapStepRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Sleep_Identity()
    {
        var card = SleepFactory.Create(_alice);

        card.Name.Should().Be("Sleep");
        card.ManaCost.Should().Be("{2}{U}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sleep_IsBlue()
    {
        var card = SleepFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Blue,
            "Sleep has {U}{U} pips in its mana cost");
    }

    [Fact]
    public void Sleep_ManaValueIsFour()
    {
        var card = SleepFactory.Create(_alice);

        // {2}{U}{U} → generic 2 + two coloured pips = mana value 4 (CR 202.3).
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Sleep_SpellDefinition_DeclaresOneTargetPlayerRequest()
    {
        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: null);

        def.TargetRequests.Should().HaveCount(1,
            "Sleep names exactly one target — 'target player'");

        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1, "targeting a player is mandatory");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("player",
            because: "the target wording is 'target player'");
    }

    // -----------------------------------------------------------------------
    // Resolve — tap all target player's creatures (CR 701.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void Sleep_Resolve_TapsAllCreaturesTargetPlayerControls()
    {
        var bear1 = PutCreatureOnBattlefield(_bob, "Bear 1");
        var bear2 = PutCreatureOnBattlefield(_bob, "Bear 2");

        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: null);
        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        bear1.IsTapped.Should().BeTrue("Sleep taps all creatures the target player controls");
        bear2.IsTapped.Should().BeTrue("Sleep taps all creatures the target player controls");
    }

    [Fact]
    public void Sleep_Resolve_DoesNotTapNonCreatures()
    {
        // A land on Bob's battlefield must NOT be tapped by Sleep.
        var creature = PutCreatureOnBattlefield(_bob, "Bear");
        var land = new Land("Island");
        land.SetOwner(_bob);
        land.SetController(_bob);
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: null);
        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        creature.IsTapped.Should().BeTrue("creature is tapped by Sleep");
        land.IsTapped.Should().BeFalse("Sleep only taps creatures, not lands");
    }

    // -----------------------------------------------------------------------
    // Resolve — skip-untap marker (CR 502.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Sleep_Resolve_MarksAllCreaturesToSkipNextUntapStep()
    {
        var bear1 = PutCreatureOnBattlefield(_bob, "Bear 1");
        var bear2 = PutCreatureOnBattlefield(_bob, "Bear 2");

        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: null);
        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        UntapStepRestrictions.ShouldSkipUntap(bear1, _bob).Should().BeTrue(
            "Sleep marks each creature to skip its controller's next untap step (CR 502.1)");
        UntapStepRestrictions.ShouldSkipUntap(bear2, _bob).Should().BeTrue(
            "Sleep marks each creature to skip its controller's next untap step (CR 502.1)");
    }

    [Fact]
    public void Sleep_Resolve_AlreadyTappedCreature_StillMarkedForSkipUntap()
    {
        // A creature already tapped before Sleep resolves should still get
        // the skip-untap marker.
        var bear = PutCreatureOnBattlefield(_bob, "Bear");
        bear.Tap(); // already tapped

        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: null);
        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        UntapStepRestrictions.ShouldSkipUntap(bear, _bob).Should().BeTrue(
            "Skip-untap applies even to creatures already tapped before Sleep resolved");
    }

    // -----------------------------------------------------------------------
    // Edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Sleep_Resolve_NoBattlefieldCreatures_NoOp()
    {
        // Bob controls no creatures — Sleep resolves as a clean no-op.
        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: null);
        var act = () =>
        {
            var effects = def.EffectFactory(MakeChosen(_bob));
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow("Sleep with no targets is a clean no-op (CR 608.2b)");
    }

    [Fact]
    public void Sleep_Resolve_IllegalTarget_CleanNoOp()
    {
        // CR 608.2b — if the resolved target is not a Player, the spell fizzles.
        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: null);
        var illegalChosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { "not-a-player" } },
            Mana: ManaPayment.Empty);

        var act = () =>
        {
            var effects = def.EffectFactory(illegalChosen);
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow("CR 608.2b illegal target is a clean no-op");
    }

    // -----------------------------------------------------------------------
    // One-shot cleanup via bus (CR 502.1 / "next untap step")
    // -----------------------------------------------------------------------

    [Fact]
    public void Sleep_BusWired_SkipUntapLiftsAfterTargetPlayerUntapStep()
    {
        var bear1 = PutCreatureOnBattlefield(_bob, "Bear 1");
        var bear2 = PutCreatureOnBattlefield(_bob, "Bear 2");

        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: _bus);
        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        // Both creatures are marked immediately after resolution.
        UntapStepRestrictions.ShouldSkipUntap(bear1, _bob).Should().BeTrue();
        UntapStepRestrictions.ShouldSkipUntap(bear2, _bob).Should().BeTrue();

        // Simulate Bob's next Untap step.
        _bus.Publish(new StepStartedEvent(PhaseStateType.Untap, _bob));

        // Restrictions must be lifted after that step.
        UntapStepRestrictions.ShouldSkipUntap(bear1, _bob).Should().BeFalse(
            "Skip-untap is lifted after the target player's next untap step fires");
        UntapStepRestrictions.ShouldSkipUntap(bear2, _bob).Should().BeFalse(
            "Skip-untap is lifted after the target player's next untap step fires");
    }

    [Fact]
    public void Sleep_BusWired_OtherPlayerUntapStep_SkipUntapPersists()
    {
        // Alice's untap step must NOT clear the restriction placed on Bob's creatures.
        var bear = PutCreatureOnBattlefield(_bob, "Bear");

        var def = SleepFactory.BuildSpellDefinition(caster: _alice, eventBus: _bus);
        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        // Alice's untap step fires — Bob's creatures should still be skipped.
        _bus.Publish(new StepStartedEvent(PhaseStateType.Untap, _alice));

        UntapStepRestrictions.ShouldSkipUntap(bear, _bob).Should().BeTrue(
            "Alice's untap step must not clear the skip placed on Bob's creatures");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature PutCreatureOnBattlefield(Player controller, string name)
    {
        var c = new Creature(name, "{G}", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static ChosenSpellParams MakeChosen(Player targetPlayer) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetPlayer } },
            Mana: ManaPayment.Empty);
}
