using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Ajani, Nacatl Pariah (MH3 DFC front face) —
/// Creature — Cat {1}{W} 1/1, Vigilance.
///   "At the beginning of your end step, you may sacrifice another
///    creature. If you do, transform Ajani, Nacatl Pariah."
///
/// Validates:
///   * Card identity + dispatch + Vigilance keyword.
///   * MdfcState attached with correct front / back face names.
///   * CR 500.4 / CR 603.1 — end-step trigger fires on controller's End step
///     and resolves by sacrificing another creature + flipping MdfcState.
///   * CR 117 — when no other creature exists, the "may" effect no-ops
///     (the source remains on the front face).
///   * The trigger only fires on the controller's End step.
/// </summary>
public class AjaniNacatlPariahTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    // ------------------------------------------------------------------
    // Card identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void AjaniNacatlPariah_IsCreatureCat_AtCost1W_1_1()
    {
        var ajani = AjaniNacatlPariahFactory.Create(_alice);

        ajani.Name.Should().Be("Ajani, Nacatl Pariah");
        ajani.HasType(CardType.Creature).Should().BeTrue();
        ajani.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        ajani.ManaCost.Should().Be("{1}{W}");
        ajani.Power.Should().Be(1);
        ajani.Toughness.Should().Be(1);
        ajani.Owner.Should().BeSameAs(_alice);
        ajani.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AjaniNacatlPariah_HasVigilanceKeyword()
    {
        var ajani = AjaniNacatlPariahFactory.Create(_alice);

        // CR 702.20 — Vigilance, consumed by CombatAbilities.HasVigilance.
        ajani.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Vigilance",
                "Ajani, Nacatl Pariah has Vigilance (CR 702.20)");
    }

    [Fact]
    public void AjaniNacatlPariah_HasMdfcStateOnFrontFace()
    {
        var ajani = AjaniNacatlPariahFactory.Create(_alice);

        ajani.MdfcState.Should().NotBeNull("DFC card must carry an MdfcState (CR 711)");
        ajani.MdfcState!.FrontFaceName.Should().Be("Ajani, Nacatl Pariah");
        ajani.MdfcState.BackFaceName.Should().Be("Ajani, Nacatl Avenger");
        ajani.MdfcState.IsBackFace.Should().BeFalse("starts on the front face");
        ajani.MdfcState.ActiveFaceName.Should().Be("Ajani, Nacatl Pariah");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AjaniNacatlPariah_AsCreatureWithMdfc()
    {
        var dispatched = NamedCardFactory.Create("Ajani, Nacatl Pariah", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Ajani, Nacatl Pariah");
        dispatched.ManaCost.Should().Be("{1}{W}");

        var ajani = (Creature)dispatched;
        ajani.MdfcState.Should().NotBeNull(
            "the dispatcher route must attach the DFC face-tracker");
        ajani.MdfcState!.BackFaceName.Should().Be("Ajani, Nacatl Avenger");
    }

    // ------------------------------------------------------------------
    // CR 500.4 / CR 603.1 + CR 701.28 — end-step transform trigger
    // ------------------------------------------------------------------

    /// <summary>
    /// On the controller's End step, with another creature available to
    /// sacrifice, the trigger fires and resolves by sending that creature
    /// to the graveyard and flipping the MdfcState to its back face.
    /// </summary>
    [Fact]
    public void AjaniNacatlPariah_AtControllersEndStep_SacrificesAnotherCreature_AndTransforms()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ajani = AjaniNacatlPariahFactory.Create(_alice, triggers);
        ajani.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ajani);

        // Another creature on the controller's battlefield to feed the
        // sacrifice cost.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        bears.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bears);

        // Fire the End step on the controller's turn — the trigger should
        // queue and resolve to sacrifice + transform.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(1,
            "the end-step transform trigger fires at the start of the controller's End step");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Count.Should().Be(1);
        stack.Pop()!.Resolve();

        bears.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrificed creature moves to its owner's graveyard (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bears);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bears);

        ajani.Zone.Should().Be(ZoneType.Battlefield,
            "Ajani is not sacrificed — only flipped (CR 701.28)");
        ajani.MdfcState!.IsBackFace.Should().BeTrue(
            "CR 701.28 — transform flips the MdfcState to the back face");
        ajani.MdfcState.ActiveFaceName.Should().Be("Ajani, Nacatl Avenger");
    }

    /// <summary>
    /// CR 117 — a "you may" effect with no valid candidate resolves as a
    /// no-op. With no other creature on the controller's battlefield, the
    /// trigger still fires (it's an "at the beginning of your end step"
    /// shape) but resolves without flipping the MdfcState.
    /// </summary>
    [Fact]
    public void AjaniNacatlPariah_AtControllersEndStep_WithNoOtherCreature_DoesNotTransform()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ajani = AjaniNacatlPariahFactory.Create(_alice, triggers);
        ajani.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ajani);

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        if (stack.Count > 0) stack.Pop()!.Resolve();

        ajani.MdfcState!.IsBackFace.Should().BeFalse(
            "no candidate to sacrifice → no flip (CR 117 no-op)");
        ajani.MdfcState.ActiveFaceName.Should().Be("Ajani, Nacatl Pariah");
        ajani.Zone.Should().Be(ZoneType.Battlefield);
    }

    /// <summary>
    /// End step on the OPPONENT's turn must not fire the trigger
    /// (Triggers.OnStepBegin filters on controller). Other steps on the
    /// controller's turn must also not fire.
    /// </summary>
    [Fact]
    public void AjaniNacatlPariah_EndStepOnOpponentsTurn_DoesNotTransform()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ajani = AjaniNacatlPariahFactory.Create(_alice, triggers);
        ajani.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ajani);

        // Plenty of sacrifice fodder on the controller's battlefield —
        // proves the gate is the controller filter, not target availability.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        bears.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bears);

        // Opponent's end step + controller's non-end steps — none of these
        // should fire the trigger.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _bob));
        _bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        _bus.Publish(new StepStartedEvent(PhaseStateType.Main, _alice));
        _bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        triggers.PendingCount.Should().Be(0,
            "the trigger only fires on the controller's End step");
        ajani.MdfcState!.IsBackFace.Should().BeFalse(
            "no End-step-on-controller-turn → no flip");
        bears.Zone.Should().Be(ZoneType.Battlefield,
            "no trigger fired → no sacrifice");
    }
}
