using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Ruination Guide (Battle for Zendikar, {2}{U}).
///
/// Creature — Eldrazi Drone 3/2 (colorless — Devoid). Oracle text (verified
/// against Scryfall 2026-06-02):
///   "Devoid (This card has no color.)
///    Ingest (Whenever this creature deals combat damage to a player, that
///    player exiles the top card of their library.)
///    Other colorless creatures you control get +1/+0."
///
/// Covers:
///   - Card shape: name, Creature, Eldrazi + Drone subtypes, {2}{U}, 3/2.
///   - Devoid: colorless despite the {U} pip (CardColors.GetColors empty).
///   - Ingest combat trigger: damaging a player exiles the top of THEIR
///     library; damaging a creature does not fire; empty library is a no-op.
///   - Colorless anthem: +1/+0 to OTHER colorless creatures you control;
///     coloured creatures are unaffected; opponent's colorless creatures are
///     unaffected; Ruination Guide does not buff itself; LTB lifts the bonus.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "C")]
public class RuinationGuideFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RuinationGuide_IsEldraziDrone_3_2_AtCost2U()
    {
        var c = RuinationGuideFactory.Create(_alice);

        c.Name.Should().Be("Ruination Guide");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RuinationGuide_IsColorless_ViaDevoid()
    {
        var c = RuinationGuideFactory.Create(_alice);

        // CR 702.114 — Devoid: colorless despite the {U} pip.
        CardColors.GetColors(c).Should().BeEmpty(
            "Devoid makes Ruination Guide colorless regardless of the {U} pip");
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Devoid");
    }

    [Fact]
    public void RuinationGuide_HasIngestKeywordMarker_AndCombatTrigger()
    {
        var c = RuinationGuideFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Ingest");

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Ingest is the one combat-damage trigger");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RuinationGuide()
    {
        var card = NamedCardFactory.Create("Ruination Guide", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ruination Guide");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(2);
    }

    [Fact]
    public void Ingest_CombatDamageToPlayer_ExilesTopOfTheirLibrary()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Bob's library top — Ingest should exile this card.
        var topCard = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var guide = RuinationGuideFactory.Create(_alice, continuousEffects: null, triggers);
        _alice.Zones.Battlefield.AddCard(guide);
        guide.SetZone(ZoneType.Battlefield);

        // Fire combat damage to Bob.
        bus.Publish(new CombatDamageDealtEvent(guide, _bob, 3));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(topCard,
            "the damaged player exiles the top card of THEIR library (Ingest)");
        _bob.Zones.Library.GetCards().Should().NotContain(topCard);
        topCard.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Ingest_CombatDamageToCreature_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var topCard = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var blocker = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };
        _bob.Zones.Battlefield.AddCard(blocker);
        blocker.SetZone(ZoneType.Battlefield);

        var guide = RuinationGuideFactory.Create(_alice, continuousEffects: null, triggers);
        _alice.Zones.Battlefield.AddCard(guide);
        guide.SetZone(ZoneType.Battlefield);

        // Combat damage to a CREATURE — "to a player" gate must reject this.
        bus.Publish(new CombatDamageDealtEvent(guide, blocker, 3));
        triggers.PutPendingTriggersOnStack(_alice);

        stack.IsEmpty.Should().BeTrue("Ingest only fires on damage to a player");
        _bob.Zones.Library.GetCards().Should().Contain(topCard);
        _bob.Zones.Exile.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void Ingest_EmptyLibrary_IsNoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var guide = RuinationGuideFactory.Create(_alice, continuousEffects: null, triggers);
        _alice.Zones.Battlefield.AddCard(guide);
        guide.SetZone(ZoneType.Battlefield);

        // Bob's library is empty — exiling is a no-op (CR 120.3).
        bus.Publish(new CombatDamageDealtEvent(guide, _bob, 3));
        triggers.PutPendingTriggersOnStack(_alice);
        var act = () => stack.Pop()!.Resolve();

        act.Should().NotThrow("exiling from an empty library is simply a no-op");
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Anthem_BuffsOtherControllerColorlessCreatures()
    {
        var svc = new ContinuousEffectsService();

        // A colorless creature (no pips → empty color set) Alice controls.
        var drone = MakeCreature("Hedron Crawler", _alice, svc, 1, 1, manaCost: "{3}");

        var guide = RuinationGuideFactory.Create(_alice, svc, triggers: null);
        guide.SetZone(ZoneType.Battlefield);
        guide.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(guide);

        drone.GetPower().Should().Be(2,
            "Ruination Guide grants +1/+0 to other colorless creatures you control");
        drone.GetToughness().Should().Be(1, "+1/+0 — toughness is unchanged");
    }

    [Fact]
    public void Anthem_DoesNotBuffColoredCreatures()
    {
        var svc = new ContinuousEffectsService();

        var greenBear = MakeCreature("Grizzly Bears", _alice, svc, 2, 2, manaCost: "{1}{G}");

        var guide = RuinationGuideFactory.Create(_alice, svc, triggers: null);
        guide.SetZone(ZoneType.Battlefield);
        guide.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(guide);

        greenBear.GetPower().Should().Be(2, "the anthem only buffs COLORLESS creatures");
        greenBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Anthem_DoesNotBuffOpponentColorlessCreatures()
    {
        var svc = new ContinuousEffectsService();

        var bobDrone = MakeCreature("Bob's Drone", _bob, svc, 1, 1, manaCost: "{2}");

        var guide = RuinationGuideFactory.Create(_alice, svc, triggers: null);
        guide.SetZone(ZoneType.Battlefield);
        guide.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(guide);

        bobDrone.GetPower().Should().Be(1,
            "the anthem keys on 'you control' — opponent's colorless creatures are unaffected");
        bobDrone.GetToughness().Should().Be(1);
    }

    [Fact]
    public void Anthem_DoesNotBuffItself_OtherClause()
    {
        var svc = new ContinuousEffectsService();

        var guide = RuinationGuideFactory.Create(_alice, svc, triggers: null);
        guide.SetZone(ZoneType.Battlefield);
        guide.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(guide);

        // Ruination Guide is itself colorless (Devoid) but "OTHER" excludes it.
        guide.GetPower().Should().Be(3,
            "'OTHER colorless creatures' — Ruination Guide does not buff itself");
        guide.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Anthem_LeavingBattlefield_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var drone = MakeCreature("Hedron Crawler", _alice, svc, 1, 1, manaCost: "{3}");

        var guide = RuinationGuideFactory.Create(_alice, svc, triggers: null);
        guide.SetZone(ZoneType.Battlefield);
        guide.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(guide);

        drone.GetPower().Should().Be(2);

        // Ruination Guide leaves the battlefield — IsActive gate flips false.
        guide.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(guide);
        _alice.Zones.Graveyard.AddCard(guide);

        drone.GetPower().Should().Be(1,
            "the anthem's IsActive gates on the source being on the battlefield");
        drone.GetToughness().Should().Be(1);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Creature MakeCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t, string manaCost)
    {
        var c = new Creature(name, manaCost, p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = svc;
        return c;
    }
}
