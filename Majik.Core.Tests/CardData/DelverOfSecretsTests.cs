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
/// Tests for Delver of Secrets — DFC front face (Innistrad, {U}).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost).
///   - MdfcState attached with correct front / back face names (CR 711).
///   - Upkeep trigger structure (filtered to controller's own upkeep,
///     active only on the battlefield).
///   - Mechanic: upkeep peek + transform when top is instant or sorcery.
///   - Non-instant/sorcery top: no transform, no reveal event.
///   - Empty-library edge: no transform, no draw-from-empty flag.
///   - Live wiring: TriggerManager surfaces the trigger as pending on the
///     controller's Upkeep StepStartedEvent.
///   - Reveal event emission on the trigger flip case.
///   - NamedCardFactory dispatch.
/// </summary>
public class DelverOfSecretsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DelverOfSecrets_IsCreature_HumanWizard_1_1_AtCostU()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);

        delver.Name.Should().Be("Delver of Secrets");
        delver.ManaCost.Should().Be("{U}");
        delver.HasType(CardType.Creature).Should().BeTrue();
        delver.HasSubtype(CardSubtype.Human).Should().BeTrue();
        delver.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        delver.BasePower.Should().Be(1);
        delver.BaseToughness.Should().Be(1);
        delver.Owner.Should().BeSameAs(_alice);
        delver.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DelverOfSecrets_HasMdfcStateOnFrontFace()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);

        delver.MdfcState.Should().NotBeNull("DFC card must carry an MdfcState (CR 711)");
        delver.MdfcState!.FrontFaceName.Should().Be("Delver of Secrets");
        delver.MdfcState.BackFaceName.Should().Be("Insectile Aberration");
        delver.MdfcState.IsBackFace.Should().BeFalse("starts on the front face");
        delver.MdfcState.ActiveFaceName.Should().Be("Delver of Secrets");
    }

    [Fact]
    public void DelverOfSecrets_HasUpkeepTrigger_OnlyOnBattlefield()
    {
        var delver = DelverOfSecretsFactory.Create(_alice);

        var triggers = delver.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    [Fact]
    public void DelverOfSecrets_Upkeep_TopIsInstant_Transforms()
    {
        // Lightning Bolt (Instant) on top of Alice's library → Delver flips.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var delver = DelverOfSecretsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(delver);
        delver.SetZone(ZoneType.Battlefield);

        var trigger = delver.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Bolt stays on top of library — peek does not move it.
        _alice.Zones.Library.GetCards().Should().Contain(bolt);
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);

        // MdfcState flipped to back face.
        delver.MdfcState!.IsBackFace.Should().BeTrue(
            "CR 701.28 — instant on top transforms Delver to Insectile Aberration");
        delver.MdfcState.ActiveFaceName.Should().Be("Insectile Aberration");
    }

    [Fact]
    public void DelverOfSecrets_Upkeep_TopIsSorcery_Transforms()
    {
        var divination = new Sorcery("Divination", "2U") { Owner = _alice };
        _alice.Zones.Library.AddCard(divination);
        divination.SetZone(ZoneType.Library);

        var delver = DelverOfSecretsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(delver);
        delver.SetZone(ZoneType.Battlefield);

        var trigger = delver.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        delver.MdfcState!.IsBackFace.Should().BeTrue(
            "sorcery on top also flips Delver");
    }

    [Fact]
    public void DelverOfSecrets_Upkeep_TopIsCreature_DoesNotTransform()
    {
        // A non-instant/sorcery on top → no flip, no reveal.
        var grizzly = new Creature(
            "Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear }) { Owner = _alice };
        _alice.Zones.Library.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Library);

        var delver = DelverOfSecretsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(delver);
        delver.SetZone(ZoneType.Battlefield);

        var trigger = delver.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        delver.MdfcState!.IsBackFace.Should().BeFalse(
            "non-instant/sorcery top card does not transform Delver");
        delver.MdfcState.ActiveFaceName.Should().Be("Delver of Secrets");
    }

    [Fact]
    public void DelverOfSecrets_Upkeep_EmptyLibrary_NoTransform_NoDrawFromEmpty()
    {
        // Looking at an empty library is not a draw — no transform and no
        // "tried to draw from empty library" flag (CR 701.19 vs CR 120.3).
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        var delver = DelverOfSecretsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(delver);
        delver.SetZone(ZoneType.Battlefield);

        var trigger = delver.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        delver.MdfcState!.IsBackFace.Should().BeFalse();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
            "looking is not drawing");
    }

    [Fact]
    public void DelverOfSecrets_LiveWiring_UpkeepStepRegistersPendingTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var delver = DelverOfSecretsFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(delver);
        delver.SetZone(ZoneType.Battlefield);

        // Bob's upkeep — Alice's Delver does NOT trigger (only her own).
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "Delver only triggers on its controller's own upkeep");

        // Alice's upkeep — trigger surfaces as pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public void DelverOfSecrets_EmitsRevealedEvent_WhenInstantOnTop()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var delver = DelverOfSecretsFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(delver);
        delver.SetZone(ZoneType.Battlefield);

        var trigger = delver.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        revealed.Should().HaveCount(1);
        revealed[0].Card.Should().BeSameAs(bolt);
        revealed[0].Reason.Should().Be("Delver of Secrets");
        revealed[0].From.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void DelverOfSecrets_EmitsNoRevealedEvent_WhenNonInstantSorceryOnTop()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var grizzly = new Creature(
            "Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear }) { Owner = _alice };
        _alice.Zones.Library.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Library);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var delver = DelverOfSecretsFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(delver);
        delver.SetZone(ZoneType.Battlefield);

        var trigger = delver.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        revealed.Should().BeEmpty(
            "v1 only emits reveal when the trigger condition (instant/sorcery) is satisfied");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DelverOfSecrets_AsCreatureWithMdfc()
    {
        var card = NamedCardFactory.Create("Delver of Secrets", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Delver of Secrets");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);

        var delver = (Creature)card;
        delver.BasePower.Should().Be(1);
        delver.BaseToughness.Should().Be(1);

        delver.MdfcState.Should().NotBeNull(
            "DelverOfSecretsFactory must attach an MdfcState (CR 711)");
        delver.MdfcState!.FrontFaceName.Should().Be("Delver of Secrets");
        delver.MdfcState.BackFaceName.Should().Be("Insectile Aberration");
    }
}
