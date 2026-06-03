using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MagesAttendantFactory"/>.
///
/// Mage's Attendant ({2}{W}). Creature — Cat Rogue 3/2.
/// Oracle (verified against Scryfall):
///   "When this creature enters, create a 1/1 blue Wizard creature token
///    with "{1}, Sacrifice this token: Counter target noncreature spell
///    unless its controller pays {1}.""
///
/// Coverage:
/// - Identity (name, type, Cat/Rogue subtypes, cost, P/T, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - One ETB <see cref="TriggeredAbility"/> over a CardMovedEvent to the
///   battlefield, gated to this card.
/// - The minted Wizard token: 1/1 blue Wizard with a {1}+sac activated
///   ability targeting a noncreature spell.
/// - The token's counter resolution: counters a noncreature spell whose
///   controller cannot pay {1}, and ignores creature spells.
/// </summary>
[Trait("Color", "W")]
public class MagesAttendantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void MagesAttendant_Identity()
    {
        var c = MagesAttendantFactory.Create(_alice);

        c.Name.Should().Be("Mage's Attendant");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{W}");
        c.ManaCostValue.TotalValue.Should().Be(3);
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MagesAttendant_Dispatch_ResolvesViaNamedFactory()
    {
        var card = NamedCardFactory.Create("Mage's Attendant", _alice);

        card.Should().BeOfType<Creature>("Mage's Attendant has a [CardName] factory.");
        card.Name.Should().Be("Mage's Attendant");
    }

    // ── ETB trigger — structural ────────────────────────────────────────

    [Fact]
    public void MagesAttendant_HasOneEtbTrigger()
    {
        var card = MagesAttendantFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the ETB Wizard-token trigger is attached.");
        triggers[0].Source.Should().BeSameAs(card);
        triggers[0].Controller.Should().BeSameAs(_alice);
        triggers[0].Condition.Should().BeOfType<EventTriggerCondition<CardMovedEvent>>();
    }

    [Fact]
    public void EtbTrigger_Matches_OnlyThisCardEnteringBattlefield()
    {
        var card = MagesAttendantFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        var cond = (EventTriggerCondition<CardMovedEvent>)trigger.Condition;

        cond.Matches(
            new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeTrue("this card entering the battlefield triggers the ability.");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        cond.Matches(
            new CardMovedEvent(other, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeFalse("another creature entering does not trigger this ability.");

        cond.Matches(
            new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard), trigger)
            .Should().BeFalse("leaving the battlefield does not trigger the ETB.");
    }

    // ── Wizard token shape ──────────────────────────────────────────────

    [Fact]
    public void CreateWizardToken_Builds_1_1_Blue_Wizard_With_CounterAbility()
    {
        var token = MagesAttendantFactory.CreateWizardToken(_alice);

        token.Name.Should().Be("Wizard");
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.IsToken.Should().BeTrue();
        token.HasType(CardType.Creature).Should().BeTrue();
        token.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        token.Owner.Should().BeSameAs(_alice);
        token.Controller.Should().BeSameAs(_alice);
        token.Zone.Should().Be(ZoneType.Battlefield,
            "the Wizard token enters the battlefield directly (CR 111.6).");
        CardColors.GetColors(token).Should().Contain(ManaColor.Blue,
            "the token is blue (CR 105.2c).");

        var activated = token.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.OfType<ManaCostCost>().Should().HaveCount(1,
            "the {1} mana cost.");
        activated.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the Sacrifice-this-token cost.");
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── ETB effect — execute and observe the token landing ──────────────

    [Fact]
    public void MagesAttendant_EtbEffect_CreatesWizardUnderController()
    {
        var attendant = MagesAttendantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(attendant);
        attendant.SetZone(ZoneType.Battlefield);

        var trigger = attendant.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var tokensOnBoard = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Wizard" && c.IsToken)
            .ToList();

        tokensOnBoard.Should().HaveCount(1, "the ETB effect creates one Wizard token.");
        tokensOnBoard[0].Power.Should().Be(1);
        tokensOnBoard[0].Toughness.Should().Be(1);
        tokensOnBoard[0].HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        CardColors.GetColors(tokensOnBoard[0]).Should().Contain(ManaColor.Blue);
        tokensOnBoard[0].Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the token carries its sac-to-counter ability.");
    }

    // ── Token counter behaviour ─────────────────────────────────────────

    [Fact]
    public void WizardToken_Counters_NoncreatureSpell_WhenControllerCannotPay()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var token = MagesAttendantFactory.CreateWizardToken(_alice, zones: null, stack: stack);
        var ability = token.Abilities.OfType<ActivatedAbility>().Single();

        // Bob casts a noncreature spell (an instant) with no mana to pay {1}.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bolt, _bob);
        stack.Push(spell);

        ability.SetChosenTargets(new List<IReadOnlyList<object>> { new object[] { spell } });

        foreach (var e in ability.Effects) e.Execute();

        stack.GetAll().Should().NotContain(spell,
            "Bob could not pay {1}, so the noncreature spell is countered (CR 701.5).");
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "a countered spell goes to its owner's graveyard (CR 701.5).");
    }

    [Fact]
    public void WizardToken_IgnoresCreatureSpell()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var token = MagesAttendantFactory.CreateWizardToken(_alice, zones: null, stack: stack);
        var ability = token.Abilities.OfType<ActivatedAbility>().Single();

        // A creature spell is not a legal "noncreature spell" target.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob };
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bears, _bob);
        stack.Push(spell);

        ability.SetChosenTargets(new List<IReadOnlyList<object>> { new object[] { spell } });

        foreach (var e in ability.Effects) e.Execute();

        stack.GetAll().Should().Contain(spell,
            "a creature spell is not countered by the noncreature-only ability (CR 608.2b).");
    }
}
