using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ItThatBetraysFactory"/> (Rise of the Eldrazi,
/// {12}).
///
/// Creature — Eldrazi 11/11. Oracle text (Scryfall, verified):
///   "Annihilator 2 (Whenever this creature attacks, defending player
///    sacrifices two permanents of their choice.)
///    Whenever an opponent sacrifices a nontoken permanent, put that card
///    onto the battlefield under your control."
///
/// Covers identity, the Annihilator 2 marker + trigger, and the
/// sacrifice-steal trigger over <see cref="PermanentSacrificedEvent"/>:
/// it fires on an opponent sacrificing a nontoken permanent (and NOT on
/// the controller's own sacrifice nor on a token), and on resolution
/// pulls the sacrificed card onto the controller's battlefield.
/// </summary>
public class ItThatBetraysFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ItThatBetrays_Identity()
    {
        var c = ItThatBetraysFactory.Create(_alice);

        c.Name.Should().Be("It That Betrays");
        c.ManaCost.Should().Be("{12}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Eldrazi);
        c.BasePower.Should().Be(11);
        c.BaseToughness.Should().Be(11);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Annihilator" && k.Arg == 2);
    }

    [Fact]
    public void ItThatBetrays_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("It That Betrays", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("It That Betrays");
    }

    private static TriggeredAbility StealTrigger(Creature card) =>
        // The steal trigger is the one with no target requests that fires
        // on PermanentSacrificedEvent (the Annihilator trigger fires on
        // CreatureAttacksEvent).
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<PermanentSacrificedEvent>);

    [Fact]
    public void StealTrigger_FiresWhenOpponentSacrificesNontoken()
    {
        var itb = ItThatBetraysFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(itb);
        itb.SetZone(ZoneType.Battlefield);

        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        victim.SetOwner(_bob);
        victim.SetController(_bob);

        var e = new PermanentSacrificedEvent(victim, _bob, wasToken: false);

        StealTrigger(itb).IsTriggered(e).Should().BeTrue(
            "an opponent (Bob) sacrificed a nontoken permanent");
    }

    [Fact]
    public void StealTrigger_DoesNotFireOnOwnSacrifice()
    {
        var itb = ItThatBetraysFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(itb);
        itb.SetZone(ZoneType.Battlefield);

        var ally = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);

        var e = new PermanentSacrificedEvent(ally, _alice, wasToken: false);

        StealTrigger(itb).IsTriggered(e).Should().BeFalse(
            "It That Betrays only triggers on an OPPONENT's sacrifice");
    }

    [Fact]
    public void StealTrigger_DoesNotFireOnToken()
    {
        var itb = ItThatBetraysFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(itb);
        itb.SetZone(ZoneType.Battlefield);

        var token = new Creature("Eldrazi Spawn", "{0}", 0, 1);
        token.SetOwner(_bob);
        token.SetController(_bob);

        var e = new PermanentSacrificedEvent(token, _bob, wasToken: true);

        StealTrigger(itb).IsTriggered(e).Should().BeFalse(
            "It That Betrays only steals NONTOKEN permanents (a token in the "
            + "graveyard ceases to exist, CR 111.7)");
    }

    [Fact]
    public void StealTrigger_OnResolution_PullsSacrificedCardOntoControllerBattlefield()
    {
        var itb = ItThatBetraysFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(itb);
        itb.SetZone(ZoneType.Battlefield);

        // The sacrificed card is already in its owner's (Bob's) graveyard
        // by the time the steal trigger resolves (CR 701.16a — sacrifice
        // puts the permanent into its owner's graveyard).
        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Graveyard.AddCard(victim);
        victim.SetZone(ZoneType.Graveyard);

        var e = new PermanentSacrificedEvent(victim, _bob, wasToken: false);
        var trigger = StealTrigger(itb);
        trigger.IsTriggered(e).Should().BeTrue();

        foreach (var eff in trigger.Effects) eff.Execute();

        victim.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(victim,
            "It That Betrays puts that card onto the battlefield under ITS "
            + "controller's control");
        victim.Controller.Should().BeSameAs(_alice);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(victim);
    }

    [Fact]
    public void Annihilator2Trigger_PublishesSacrificeEvent_OnDefenderPermanent()
    {
        // End-to-end: It That Betrays' OWN Annihilator 2 sacrifices publish
        // a PermanentSacrificedEvent through the bus carrying the defender as
        // the sacrificing player — the surface its steal trigger feeds on.
        var bus = new EventBus();

        var itb = ItThatBetraysFactory.Create(_alice, triggers: null, eventBus: bus, agentSelector: null);
        _alice.Zones.Battlefield.AddCard(itb);
        itb.SetZone(ZoneType.Battlefield);

        var sacrificed = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(ev => sacrificed.Add(ev));

        var defenderPerm = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        defenderPerm.SetOwner(_bob);
        defenderPerm.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(defenderPerm);
        defenderPerm.SetZone(ZoneType.Battlefield);

        var annihilator = itb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<
                Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>);

        // Capture the defender (Bob) via the condition, then run the effect
        // — mirrors AnnihilatorTests' drive pattern.
        var attackEvent = new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(itb, _bob);
        annihilator.Condition.Matches(attackEvent, annihilator).Should().BeTrue();
        foreach (var eff in annihilator.Effects) eff.Execute();

        sacrificed.Should().ContainSingle()
            .Which.SacrificedCard.Should().BeSameAs(defenderPerm,
            "Annihilator 2 routes through the bus-aware Fx.Sacrifice");
        sacrificed[0].SacrificingPlayer.Should().BeSameAs(_bob,
            "the defending player is the sacrificing player (CR 701.16a)");
    }
}
