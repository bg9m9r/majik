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
/// Tests for Dark Confidant (Ravnica, {1}{B}).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost).
///   - Upkeep trigger structure (filtered to controller's own upkeep,
///     active only on the battlefield).
///   - Mechanic: upkeep moves top of library → hand and deals life loss
///     equal to the revealed card's mana value.
///   - Empty-library edge: no life loss, draw-from-empty flag set.
///   - Live wiring: when registered with a TriggerManager, an Upkeep
///     StepStartedEvent for the controller surfaces the trigger as
///     pending.
///   - NamedCardFactory dispatch.
/// </summary>
public class DarkConfidantTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DarkConfidant_IsCreature_HumanWizard_2_1_AtCost1B()
    {
        var bob = DarkConfidantFactory.Create(_alice);

        bob.Name.Should().Be("Dark Confidant");
        bob.ManaCost.Should().Be("{1}{B}");
        bob.HasType(CardType.Creature).Should().BeTrue();
        bob.HasSubtype(CardSubtype.Human).Should().BeTrue();
        bob.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        bob.BasePower.Should().Be(2);
        bob.BaseToughness.Should().Be(1);
        bob.Owner.Should().BeSameAs(_alice);
        bob.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DarkConfidant_HasUpkeepTrigger_OnlyOnBattlefield()
    {
        var bob = DarkConfidantFactory.Create(_alice);

        var triggers = bob.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    [Fact]
    public void DarkConfidant_Upkeep_RevealsTopLibrary_DrawsIt_LosesLifeEqualToMV()
    {
        // Setup: a Lightning Bolt (MV 1) on top of Alice's library, Dark
        // Confidant on the battlefield. Simulate the upkeep trigger by
        // executing its effect directly.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var bob = DarkConfidantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bob);
        bob.SetZone(ZoneType.Battlefield);

        var trigger = bob.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Bolt is now in hand.
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Library.GetCards().Should().NotContain(bolt);

        // Life loss = MV(1) = 1.
        _alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void DarkConfidant_Upkeep_FreeSpellMV_LosesZeroLife()
    {
        // A free spell (no mana cost) reveals → goes to hand → no life loss.
        var bauble = new Card("Mishra's Bauble", "") { Owner = _alice };
        _alice.Zones.Library.AddCard(bauble);
        bauble.SetZone(ZoneType.Library);

        var bob = DarkConfidantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bob);
        bob.SetZone(ZoneType.Battlefield);

        var trigger = bob.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bauble);
        _alice.LifeTotal.Should().Be(20, "free spell's mana value is 0");
    }

    [Fact]
    public void DarkConfidant_Upkeep_EmptyLibrary_NoLifeLoss_MarksDrawFromEmpty()
    {
        // Alice's library is empty. The trigger should no-op the draw and
        // not deal any life loss (there's nothing to compute MV from). The
        // "tried to draw from empty library" flag should be set per CR
        // 120.3 / 704.5b.
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        var bob = DarkConfidantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bob);
        bob.SetZone(ZoneType.Battlefield);

        var trigger = bob.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(20);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void DarkConfidant_LiveWiring_UpkeepStepRegistersPendingTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var bob = DarkConfidantFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(bob);
        bob.SetZone(ZoneType.Battlefield);

        // Bob's upkeep — Alice's Confidant does NOT trigger (only her own).
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "Dark Confidant only triggers on its controller's own upkeep");

        // Alice's upkeep — trigger surfaces as pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public void DarkConfidant_LiveWiring_EmitsRevealedEvent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var bob = DarkConfidantFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(bob);
        bob.SetZone(ZoneType.Battlefield);

        var trigger = bob.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        revealed.Should().HaveCount(1);
        revealed[0].Card.Should().BeSameAs(bolt);
        revealed[0].Reason.Should().Be("Dark Confidant");
        revealed[0].From.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DarkConfidant()
    {
        var card = NamedCardFactory.Create("Dark Confidant", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Dark Confidant");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
}
