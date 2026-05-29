using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SlingGangLieutenantFactory"/>.
///
/// Sling-Gang Lieutenant (Lorwyn, {3}{B}). Creature — Goblin 1/1.
/// Oracle (verified against Scryfall seed):
///   "When this creature enters, create two 1/1 red Goblin creature tokens.
///    Sacrifice a Goblin: Target player loses 1 life and you gain 1 life."
///
/// Coverage:
/// - Identity (name, type, Goblin subtype, cost, P/T, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - One ETB <see cref="TriggeredAbility"/> over a CardMovedEvent to the
///   battlefield, gated to this card.
/// - ETB effect mints two 1/1 red Goblin tokens under the controller.
/// - One <see cref="SlingGangLieutenantAbility"/> with a sacrifice-a-Goblin
///   cost and a single target-player request.
/// - Sacrifice cost can/can't pay; self is a legal sacrifice (no "another").
/// - Activation: sacrifices a Goblin, target player loses 1, controller
///   gains 1.
/// </summary>
public class SlingGangLieutenantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature Goblin(string name, Player owner)
    {
        var g = new Creature(name, "{R}", 1, 1, subtypes: new[] { CardSubtype.Goblin });
        g.SetOwner(owner);
        g.SetController(owner);
        owner.Zones.Battlefield.AddCard(g);
        g.SetZone(ZoneType.Battlefield);
        return g;
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void SlingGangLieutenant_Identity()
    {
        var c = SlingGangLieutenantFactory.Create(_alice);

        c.Name.Should().Be("Sling-Gang Lieutenant");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.ManaCost.Should().Be("{3}{B}");
        c.ManaCostValue.TotalValue.Should().Be(4);
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SlingGangLieutenant_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sling-Gang Lieutenant", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sling-Gang Lieutenant");
        ((Creature)c).HasSubtype(CardSubtype.Goblin).Should().BeTrue();
    }

    // ── ETB trigger — structural ────────────────────────────────────────

    [Fact]
    public void SlingGangLieutenant_HasOneEtbTrigger()
    {
        var card = SlingGangLieutenantFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the ETB Goblin-token trigger is attached.");
        triggers[0].Source.Should().BeSameAs(card);
        triggers[0].Controller.Should().BeSameAs(_alice);
        triggers[0].Condition.Should().BeOfType<EventTriggerCondition<CardMovedEvent>>();
    }

    [Fact]
    public void EtbTrigger_Matches_OnlyThisCardEnteringBattlefield()
    {
        var card = SlingGangLieutenantFactory.Create(_alice);
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

    // ── ETB effect — two 1/1 red Goblin tokens ──────────────────────────

    [Fact]
    public void CreateGoblinTokens_Builds_Two_1_1_Red_Goblins()
    {
        SlingGangLieutenantFactory.CreateGoblinTokens(_alice);

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Goblin" && c.IsToken)
            .ToList();

        tokens.Should().HaveCount(2, "the ETB effect creates two Goblin tokens (CR 111).");
        tokens.Should().OnlyContain(t => t.Power == 1 && t.Toughness == 1);
        tokens.Should().OnlyContain(t => t.HasSubtype(CardSubtype.Goblin));
        tokens.Should().OnlyContain(t => t.HasType(CardType.Creature));
        foreach (var t in tokens)
        {
            CardColors.GetColors(t).Should().Contain(ManaColor.Red,
                "the tokens are red (CR 111.4).");
        }
    }

    [Fact]
    public void SlingGangLieutenant_EtbEffect_CreatesTwoGoblinsUnderController()
    {
        var lieutenant = SlingGangLieutenantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(lieutenant);
        lieutenant.SetZone(ZoneType.Battlefield);

        var trigger = lieutenant.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Goblin" && c.IsToken)
            .ToList();

        tokens.Should().HaveCount(2);
        tokens.Should().OnlyContain(t => t.Controller == _alice);
    }

    // ── Activated ability — structural ──────────────────────────────────

    [Fact]
    public void SlingGangLieutenant_HasOneActivatedDrainAbility()
    {
        var card = SlingGangLieutenantFactory.Create(_alice);

        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1, "the sacrifice-a-Goblin drain ability is attached.");
        abilities[0].Should().BeOfType<SlingGangLieutenantAbility>();
        abilities[0].Costs.OfType<SlingGangSacrificeAGoblinCost>().Should().HaveCount(1);
        abilities[0].TargetRequests.Should().HaveCount(1, "the drain targets a player.");
        abilities[0].TargetRequests[0].MinTargets.Should().Be(1);
        abilities[0].TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── Sacrifice cost — gating ─────────────────────────────────────────

    [Fact]
    public void SacrificeCost_CannotPay_WhenNoGoblinOnBattlefield()
    {
        var card = SlingGangLieutenantFactory.Create(_alice);
        // Lieutenant itself is NOT on the battlefield yet.
        var ability = card.Abilities.OfType<SlingGangLieutenantAbility>().Single();
        ability.SacrificeChoice.CanPay(_alice).Should().BeFalse(
            "no Goblin on the battlefield to sacrifice.");
    }

    [Fact]
    public void SacrificeCost_CanPay_WithSelfOnly_NoAnotherQualifier()
    {
        var card = SlingGangLieutenantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<SlingGangLieutenantAbility>().Single();
        ability.SacrificeChoice.CanPay(_alice).Should().BeTrue(
            "the Lieutenant itself is a Goblin and a legal sacrifice (no 'another').");
    }

    // ── Activation — drain ──────────────────────────────────────────────

    [Fact]
    public void Activation_TargetPlayerLoses1_ControllerGains1()
    {
        var card = SlingGangLieutenantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // A spare Goblin token to feed the sacrifice (preferred over self).
        var fodder = Goblin("Goblin Fodder", _alice);

        var ability = card.Abilities.OfType<SlingGangLieutenantAbility>().Single();
        ability.DrainTarget = _bob;

        foreach (var c in ability.Costs) c.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        fodder.Zone.Should().Be(ZoneType.Graveyard, "the fodder Goblin was sacrificed (preferred over self).");
        card.Zone.Should().Be(ZoneType.Battlefield, "the Lieutenant survives — fodder was sacrificed first.");
        _bob.LifeTotal.Should().Be(19, "target player lost 1 life (CR 119.3).");
        _alice.LifeTotal.Should().Be(21, "controller gained 1 life (CR 119.3).");
    }

    [Fact]
    public void Activation_NoTarget_IsNoOp()
    {
        var card = SlingGangLieutenantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        Goblin("Goblin Fodder", _alice);

        var ability = card.Abilities.OfType<SlingGangLieutenantAbility>().Single();
        // No DrainTarget set.

        foreach (var e in ability.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no target chosen — drain does nothing (CR 608.2c).");
        _alice.LifeTotal.Should().Be(20, "no lifegain when the drain has no legal target.");
    }

    [Fact]
    public void Activation_SacrificesSelf_WhenOnlyGoblin()
    {
        var card = SlingGangLieutenantFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<SlingGangLieutenantAbility>().Single();
        ability.DrainTarget = _bob;

        foreach (var c in ability.Costs) c.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        card.Zone.Should().Be(ZoneType.Graveyard,
            "the Lieutenant sacrifices itself when it is the only Goblin (no 'another').");
        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(21);
    }
}
