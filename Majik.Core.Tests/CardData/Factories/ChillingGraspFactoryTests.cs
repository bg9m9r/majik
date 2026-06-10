using FluentAssertions;
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
/// Unit tests for <see cref="ChillingGraspFactory"/>.
///
/// Card: Chilling Grasp — {2}{U} Instant. Oracle text:
///   "Tap up to two target creatures. Those creatures don't untap during
///    their controller's next untap step.
///    Madness {3}{U}"
///
/// Madness is intrinsic (CR 702.35 via MadnessCatalog + Fx.DiscardCard) and is
/// NOT exercised here — these tests cover only the spell body.
///
/// Covers:
/// - Identity ({2}{U}, blue, Instant, mana value 3).
/// - SpellDefinition declares one 0..2 "target creature" request (CR 601.2c).
/// - Resolve taps up to two target creatures (CR 701.20).
/// - Resolve marks each tapped creature to skip its controller's next untap
///   step (CR 502.1 via UntapStepRestrictions.MarkPermanentDoesNotUntap).
/// - Already-tapped target still gets the skip-untap marker.
/// - Non-creature / off-battlefield targets are clean no-ops (CR 608.2b).
/// - Zero targets ("up to two") resolves as a clean no-op.
/// - One-shot cleanup: each target's skip lifts on ITS controller's next
///   untap step, even when the two targets have different controllers.
/// </summary>
[Trait("Color", "U")]
public class ChillingGraspFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public void Dispose() => UntapStepRestrictions.Clear();

    // ------------------------------------------------------------------ Identity

    [Fact]
    public void ChillingGrasp_Identity()
    {
        var card = ChillingGraspFactory.Create(_alice);

        card.Name.Should().Be("Chilling Grasp");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ChillingGrasp_IsBlue_ManaValueThree()
    {
        var card = ChillingGraspFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Blue,
            "Chilling Grasp has a {U} pip in its mana cost");
        // {2}{U} → generic 2 + one coloured pip = mana value 3 (CR 202.3).
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(3);
    }

    // -------------------------------------------------------------- SpellDefinition

    [Fact]
    public void ChillingGrasp_SpellDefinition_DeclaresUpToTwoTargetCreatures()
    {
        var def = ChillingGraspFactory.BuildDefinition(o => o, eventBus: null);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(0, "Chilling Grasp targets 'up to two' creatures (CR 601.2c)");
        req.MaxTargets.Should().Be(2);
        req.Description.Should().Contain("creature");
    }

    // ------------------------------------------------------------------- Resolution

    [Fact]
    public void ChillingGrasp_Resolve_TapsBothTargetCreatures()
    {
        var bear1 = PutCreature(_bob, "Bear 1");
        var bear2 = PutCreature(_bob, "Bear 2");

        Execute(ChillingGraspFactory.BuildDefinition(o => o, eventBus: null), bear1, bear2);

        bear1.IsTapped.Should().BeTrue("Chilling Grasp taps each target creature (CR 701.20)");
        bear2.IsTapped.Should().BeTrue("Chilling Grasp taps each target creature (CR 701.20)");
    }

    [Fact]
    public void ChillingGrasp_Resolve_MarksTargetsToSkipNextUntapStep()
    {
        var bear1 = PutCreature(_bob, "Bear 1");
        var bear2 = PutCreature(_bob, "Bear 2");

        Execute(ChillingGraspFactory.BuildDefinition(o => o, eventBus: null), bear1, bear2);

        UntapStepRestrictions.ShouldSkipUntap(bear1, _bob).Should().BeTrue(
            "each tapped creature skips its controller's next untap step (CR 502.1)");
        UntapStepRestrictions.ShouldSkipUntap(bear2, _bob).Should().BeTrue(
            "each tapped creature skips its controller's next untap step (CR 502.1)");
    }

    [Fact]
    public void ChillingGrasp_Resolve_AlreadyTappedTarget_StillMarkedForSkipUntap()
    {
        var bear = PutCreature(_bob, "Bear");
        bear.Tap(); // already tapped before the spell resolves

        Execute(ChillingGraspFactory.BuildDefinition(o => o, eventBus: null), bear);

        UntapStepRestrictions.ShouldSkipUntap(bear, _bob).Should().BeTrue(
            "skip-untap applies even to creatures already tapped before resolution");
    }

    [Fact]
    public void ChillingGrasp_Resolve_OneTargetOnly_IsLegal()
    {
        // "up to two" — casting it on a single creature is legal.
        var bear = PutCreature(_bob, "Bear");

        Execute(ChillingGraspFactory.BuildDefinition(o => o, eventBus: null), bear);

        bear.IsTapped.Should().BeTrue();
        UntapStepRestrictions.ShouldSkipUntap(bear, _bob).Should().BeTrue();
    }

    [Fact]
    public void ChillingGrasp_Resolve_ZeroTargets_NoOp()
    {
        var def = ChillingGraspFactory.BuildDefinition(o => o, eventBus: null);
        var act = () => Execute(def /* no targets */);
        act.Should().NotThrow("'up to two' permits zero targets — a clean no-op");
    }

    [Fact]
    public void ChillingGrasp_Resolve_NonCreatureTarget_CleanNoOp()
    {
        // CR 608.2b — a non-creature resolved token is ignored.
        var def = ChillingGraspFactory.BuildDefinition(o => o, eventBus: null);
        var act = () => Execute(def, "not-a-creature");
        act.Should().NotThrow("CR 608.2b illegal target is a clean no-op");
    }

    // ----------------------------------------------------------- One-shot cleanup

    [Fact]
    public void ChillingGrasp_BusWired_SkipLiftsOnEachControllersNextUntapStep()
    {
        // Two targets with DIFFERENT controllers — each skip must lift only on
        // its own controller's untap step (CR 502.1 "their controller's").
        var aliceBear = PutCreature(_alice, "Alice Bear");
        var bobBear = PutCreature(_bob, "Bob Bear");

        Execute(ChillingGraspFactory.BuildDefinition(o => o, eventBus: _bus), aliceBear, bobBear);

        UntapStepRestrictions.ShouldSkipUntap(aliceBear, _alice).Should().BeTrue();
        UntapStepRestrictions.ShouldSkipUntap(bobBear, _bob).Should().BeTrue();

        // Alice's untap step lifts only her creature's skip.
        _bus.Publish(new StepStartedEvent(StepStateType.Untap, _alice));
        UntapStepRestrictions.ShouldSkipUntap(aliceBear, _alice).Should().BeFalse(
            "Alice's creature skips ONLY Alice's next untap step");
        UntapStepRestrictions.ShouldSkipUntap(bobBear, _bob).Should().BeTrue(
            "Bob's creature is untouched by Alice's untap step");

        // Bob's untap step lifts his creature's skip.
        _bus.Publish(new StepStartedEvent(StepStateType.Untap, _bob));
        UntapStepRestrictions.ShouldSkipUntap(bobBear, _bob).Should().BeFalse(
            "Bob's creature skips ONLY Bob's next untap step");
    }

    // ------------------------------------------------------------------- Helpers

    private Creature PutCreature(Player controller, string name)
    {
        var c = new Creature(name, "{G}", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void Execute(SpellDefinition def, params object[] targets)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
