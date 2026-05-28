using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GlimmervoidFactory"/>.
///
/// Glimmervoid — Land (Mirrodin):
///   "At the beginning of the end step, if you control no artifacts,
///    sacrifice this land."
///   "{T}: Add one mana of any color."
///
/// Covers:
///   - Card identity (Land, non-basic, no subtypes, no supertypes).
///   - NamedCardFactory dispatch.
///   - Five mana abilities (WUBRG), one per colour; each generates 1 pip.
///   - Tap gate: mana abilities blocked while land is tapped.
///   - End-step trigger present; only fires on End step, controller only.
///   - InterveningIf — trigger does NOT fire (CanBePutOnStack = false)
///     when controller has ≥ 1 artifact.
///   - InterveningIf — trigger DOES fire (CanBePutOnStack = true) when
///     controller has no artifacts.
///   - Resolution with artifact on battlefield → no sacrifice (condition
///     re-checked — CR 603.4).
///   - Resolution with no artifact → Glimmervoid sacrificed to graveyard.
///   - Off-battlefield zone guard: effect is a no-op when land isn't in play.
/// </summary>
public class GlimmervoidTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public GlimmervoidTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Glimmervoid_HasCorrectIdentity()
    {
        var land = GlimmervoidFactory.Create(_alice);

        land.Name.Should().Be("Glimmervoid");
        land.HasType(CardType.Land).Should().BeTrue("Glimmervoid is a Land");
        land.HasType(CardType.Artifact).Should().BeFalse("it is not an Artifact");
        land.HasType(CardType.Creature).Should().BeFalse("it is not a Creature");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("non-legendary");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("non-basic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Glimmervoid()
    {
        var card = NamedCardFactory.Create("Glimmervoid", _alice);

        card.Should().BeOfType<Land>("factory creates a Land instance");
        card.Name.Should().Be("Glimmervoid");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Glimmervoid_HasFiveManaAbilities_OnePerColor()
    {
        var land = GlimmervoidFactory.Create(_alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1
                                     && m.ManaGenerated.TotalValue == 1);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — all five colours produce the correct mana
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("W", 1, 0, 0, 0, 0)]
    [InlineData("U", 0, 1, 0, 0, 0)]
    [InlineData("B", 0, 0, 1, 0, 0)]
    [InlineData("R", 0, 0, 0, 1, 0)]
    [InlineData("G", 0, 0, 0, 0, 1)]
    public void Glimmervoid_ManaAbility_ProducesExpectedColor(
        string color, int white, int blue, int black, int red, int green)
    {
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var mas = land.Abilities.OfType<ManaAbility>().ToList();
        ManaAbility ability = color switch
        {
            "W" => mas.Single(m => m.ManaGenerated.White == 1),
            "U" => mas.Single(m => m.ManaGenerated.Blue == 1),
            "B" => mas.Single(m => m.ManaGenerated.Black == 1),
            "R" => mas.Single(m => m.ManaGenerated.Red == 1),
            "G" => mas.Single(m => m.ManaGenerated.Green == 1),
            _ => throw new ArgumentOutOfRangeException(nameof(color)),
        };

        ability.CanActivate().Should().BeTrue("land is untapped");
        var produced = ability.Activate();

        produced.White.Should().Be(white);
        produced.Blue.Should().Be(blue);
        produced.Black.Should().Be(black);
        produced.Red.Should().Be(red);
        produced.Green.Should().Be(green);
        produced.TotalValue.Should().Be(1, "each activation produces exactly 1 pip");
        land.IsTapped.Should().BeTrue("tapping Glimmervoid to activate");
    }

    // -----------------------------------------------------------------------
    // Tap gate
    // -----------------------------------------------------------------------

    [Fact]
    public void Glimmervoid_ManaAbilities_BlockedWhileTapped()
    {
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Tap();

        foreach (var ma in land.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse("land is tapped");
        }
    }

    // -----------------------------------------------------------------------
    // End-step trigger — shape and gating
    // -----------------------------------------------------------------------

    [Fact]
    public void Glimmervoid_HasOneTriggeredAbility()
    {
        var land = GlimmervoidFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the end-step sac trigger is attached");
    }

    [Fact]
    public void EndStepTrigger_FiresOnEndStep_NotOtherSteps()
    {
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice))
            .Should().BeTrue("printed trigger reads 'at the beginning of the end step'");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _alice))
            .Should().BeFalse("upkeep is not the end step");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.Draw, _alice))
            .Should().BeFalse("draw is not the end step");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice))
            .Should().BeFalse("combat is not the end step");
    }

    [Fact]
    public void EndStepTrigger_FiresOnControllerEndStep_NotOpponentEndStep()
    {
        // Glimmervoid reads "your end step" — uses OnStepBegin scoped to
        // the controller. Opponent's end step must NOT fire it.
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice))
            .Should().BeTrue("Alice's end step triggers Glimmervoid");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _bob))
            .Should().BeFalse("opponent's end step does NOT trigger Glimmervoid");
    }

    // -----------------------------------------------------------------------
    // InterveningIf — CR 603.4: condition checked at trigger time
    // -----------------------------------------------------------------------

    [Fact]
    public void EndStepTrigger_InterveningIf_DoesNotPutOnStack_WhenControllerHasArtifact()
    {
        // Controller has an artifact → intervening-if is false at trigger
        // time → CanBePutOnStack() returns false (CR 603.4).
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Ornithopter", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        // IsTriggered checks the event type only (step event matches).
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice))
            .Should().BeTrue("the trigger event matches — step is End");

        // InterveningIf blocks queueing when condition is false.
        trigger.CanBePutOnStack().Should().BeFalse(
            "controller has an artifact — intervening-if prevents queuing (CR 603.4)");
    }

    [Fact]
    public void EndStepTrigger_InterveningIf_CanPutOnStack_WhenControllerHasNoArtifacts()
    {
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No artifacts for Alice.

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice))
            .Should().BeTrue();
        trigger.CanBePutOnStack().Should().BeTrue(
            "no artifacts — intervening-if is true, trigger can queue (CR 603.4)");
    }

    // -----------------------------------------------------------------------
    // Resolution with artifact present → no sacrifice (CR 603.4 re-check)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolution_WithArtifactOnBattlefield_DoesNotSacrifice()
    {
        // Controller has an artifact when the trigger resolves.
        // Effect must re-check the condition and be a no-op.
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Ornithopter", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        land.Zone.Should().Be(ZoneType.Battlefield,
            "artifact on battlefield → condition false → no sacrifice (CR 603.4)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(land);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Resolution with no artifacts → Glimmervoid sacrificed
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolution_WithNoArtifacts_SacrificesGlimmervoid()
    {
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No artifacts.

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        land.Zone.Should().Be(ZoneType.Graveyard,
            "no artifacts → condition true → Glimmervoid sacrificed (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Full loop: trigger manager + stack resolver end-to-end
    // -----------------------------------------------------------------------

    [Fact]
    public void EndStep_WithTriggerManager_NoArtifacts_SacrificesGlimmervoid()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var land = GlimmervoidFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No artifacts.

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        land.Zone.Should().Be(ZoneType.Graveyard,
            "end-step trigger with no artifacts fires and resolves: Glimmervoid → graveyard");
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void EndStep_WithTriggerManager_ArtifactPresent_DoesNotSacrifice()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var land = GlimmervoidFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Memnite", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        // InterveningIf is false → trigger is not queued by the manager.
        triggers.PendingCount.Should().Be(0,
            "artifact present → intervening-if blocks queueing (CR 603.4)");

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        land.Zone.Should().Be(ZoneType.Battlefield,
            "no sacrifice when controller has an artifact at end-step trigger time");
    }

    // -----------------------------------------------------------------------
    // Zone guard: off-battlefield trigger effect is a no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolution_OffBattlefield_IsNoOp()
    {
        // If Glimmervoid somehow leaves the battlefield between the trigger
        // firing and resolution (destroyed in response), the effect must
        // not try to move it from the graveyard.
        var land = GlimmervoidFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        var act = () =>
        {
            foreach (var effect in trigger.Effects) effect.Execute();
        };

        act.Should().NotThrow("off-battlefield zone guard prevents double-move");
        land.Zone.Should().Be(ZoneType.Hand, "card stays wherever it was");
    }
}
