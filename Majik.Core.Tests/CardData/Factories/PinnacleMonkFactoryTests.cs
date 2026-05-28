using System.Linq;
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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Pinnacle Monk (Tarkir: Dragonstorm, {3}{R}{R}
/// Creature — Djinn Monk 2/2).
///
/// Covers:
///   - Card identity: name, types, subtypes (Djinn + Monk), P/T, mana cost,
///     mana value 5, owner/controller.
///   - Ability set: Prowess KeywordAbility marker on shape-only path; Prowess
///     TriggeredAbility added on fully-wired path.
///   - Prowess mechanic: casting a noncreature spell pumps +1/+1 until EOT;
///     creature spell does NOT pump.
///   - ETB trigger shape: exactly one ETB TriggeredAbility with a 1..1
///     TargetRequest for "instant or sorcery card in your graveyard".
///   - ETB effect: instant card from graveyard moves to hand.
///   - ETB effect: sorcery card from graveyard moves to hand.
///   - ETB effect: non-instant/sorcery card is NOT a legal target (effect no-ops
///     if illegally chosen).
///   - ETB effect: empty graveyard — no-op (no crash).
///   - NamedCardFactory dispatch returns a Pinnacle Monk shape.
/// </summary>
public class PinnacleMonkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Spell helpers ─────────────────────────────────────────────────────────

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller) =>
        new(new Instant("Lightning Bolt", "R") { Owner = controller }, controller);

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller) =>
        new(new Creature("Grizzly Bears", "1G", 2, 2) { Owner = controller }, controller);

    // ── Card identity ─────────────────────────────────────────────────────────

    [Fact]
    public void PinnacleMonk_Identity_DjinnMonk_2_2_At3RR()
    {
        var pm = PinnacleMonkFactory.Create(_alice);

        pm.Name.Should().Be("Pinnacle Monk");
        pm.ManaCost.Should().Be("{3}{R}{R}");
        pm.HasType(CardType.Creature).Should().BeTrue();
        pm.HasSubtype(CardSubtype.Djinn).Should().BeTrue("Pinnacle Monk is a Djinn");
        pm.HasSubtype(CardSubtype.Monk).Should().BeTrue("Pinnacle Monk is a Monk");
        pm.BasePower.Should().Be(2);
        pm.BaseToughness.Should().Be(2);
        pm.Owner.Should().BeSameAs(_alice);
        pm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PinnacleMonk_ManaValue_IsFive()
    {
        var pm = PinnacleMonkFactory.Create(_alice);
        // {3}{R}{R} = mana value 5 (CR 202.3).
        pm.ManaCostValue.TotalValue.Should().Be(5,
            "CR 202.3 — {3}{R}{R} has mana value 5");
    }

    // ── NamedCardFactory dispatch ─────────────────────────────────────────────

    [Fact]
    public void NamedCardFactory_Dispatches_PinnacleMonk()
    {
        var card = NamedCardFactory.Create("Pinnacle Monk", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Pinnacle Monk");
        card.HasSubtype(CardSubtype.Djinn).Should().BeTrue();
        card.HasSubtype(CardSubtype.Monk).Should().BeTrue();
    }

    // ── Ability set — keyword markers + trigger wiring ────────────────────────

    [Fact]
    public void PinnacleMonk_HasProwessKeywordMarker()
    {
        var pm = PinnacleMonkFactory.Create(_alice);

        pm.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Prowess",
                "Pinnacle Monk has Prowess (CR 702.108)");
    }

    [Fact]
    public void PinnacleMonk_ShapeOnly_HasExactlyOneTriggeredAbility_TheEtb()
    {
        // Single-arg path — Prowess trigger NOT wired (no effects service).
        // Only the ETB trigger should be present.
        var pm = PinnacleMonkFactory.Create(_alice);

        var triggered = pm.Abilities.OfType<TriggeredAbility>().ToList();
        triggered.Should().HaveCount(1, "only the ETB trigger is attached on shape-only path");
    }

    [Fact]
    public void PinnacleMonk_FullyWired_HasTwoTriggeredAbilities_ProwessAndEtb()
    {
        var effects = new ContinuousEffectsService();
        var pm = PinnacleMonkFactory.Create(_alice, eventBus: null, triggers: null, effects: effects);

        pm.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Prowess trigger + ETB trigger both wired on full path");
    }

    // ── ETB trigger shape ─────────────────────────────────────────────────────

    [Fact]
    public void PinnacleMonk_EtbTrigger_Shape_SingleTarget_InstantOrSorcery_InGraveyard()
    {
        var pm = PinnacleMonkFactory.Create(_alice);

        var triggered = pm.Abilities.OfType<TriggeredAbility>().ToList();
        triggered.Should().HaveCount(1);

        var etb = triggered.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause (CR 603.4 does not apply)");
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery",
            "target is restricted to instant or sorcery card type");
        req.Description.Should().Contain("graveyard",
            "target must be in the graveyard");
    }

    // ── ETB effect: instant from graveyard → hand ─────────────────────────────

    [Fact]
    public void PinnacleMonk_EtbEffect_InstantInGraveyard_MovesToHand()
    {
        var pm = PinnacleMonkFactory.Create(_alice);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var etb = pm.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
        {
            new object[] { bolt },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        bolt.Zone.Should().Be(ZoneType.Hand,
            "ETB returns the chosen instant from graveyard to hand");
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bolt);
    }

    // ── ETB effect: sorcery from graveyard → hand ─────────────────────────────

    [Fact]
    public void PinnacleMonk_EtbEffect_SorceryInGraveyard_MovesToHand()
    {
        var pm = PinnacleMonkFactory.Create(_alice);

        var divination = new Sorcery("Divination", "2U") { Owner = _alice };
        divination.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(divination);

        var etb = pm.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
        {
            new object[] { divination },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        divination.Zone.Should().Be(ZoneType.Hand,
            "ETB returns the chosen sorcery from graveyard to hand");
        _alice.Zones.Hand.GetCards().Should().Contain(divination);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(divination);
    }

    // ── ETB effect: non-instant/sorcery card — no-op ─────────────────────────

    [Fact]
    public void PinnacleMonk_EtbEffect_NonInstantSorcery_DoesNotMoveToHand()
    {
        // CR 603.10b — if the target is not an instant or sorcery at
        // resolution, the effect no-ops (doesn't return a creature card).
        var pm = PinnacleMonkFactory.Create(_alice);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        var etb = pm.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "non-instant/sorcery target is rejected — card stays in graveyard");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── ETB effect: empty graveyard — no-op, no crash ─────────────────────────

    [Fact]
    public void PinnacleMonk_EtbEffect_EmptyGraveyard_NoOpNoCrash()
    {
        // When no target is supplied (e.g. graveyard is empty and trigger
        // should be countered on resolution per CR 603.10b), the effect
        // gracefully no-ops.
        var pm = PinnacleMonkFactory.Create(_alice);

        var etb = pm.Abilities.OfType<TriggeredAbility>().Single();
        // No targets set — ChosenTargets defaults to empty.

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("empty target list should be silently skipped");

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── Prowess pump on noncreature spell ─────────────────────────────────────

    [Fact]
    public void CastingNoncreatureSpell_PumpsMonkPlusOnePlusOneEndOfTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var pm = PinnacleMonkFactory.Create(_alice, bus, triggers, effects);
        pm.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice)));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "Prowess trigger fires on noncreature spell");
        // Resolve just the Prowess trigger (ETB trigger is not pending here).
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Prowess: Pinnacle Monk is 3/3 until end of turn (CR 702.108 / Layer 7c).
        pm.Power.Should().Be(3);
        pm.Toughness.Should().Be(3);

        // End-of-turn cleanup expires the pump (CR 514.2).
        effects.ExpireEndOfTurn();
        pm.Power.Should().Be(2);
        pm.Toughness.Should().Be(2);
    }

    [Fact]
    public void CastingCreatureSpell_DoesNotPumpPinnacleMonk()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var pm = PinnacleMonkFactory.Create(_alice, bus, triggers, effects);
        pm.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice)));

        // Neither Prowess nor ETB triggers fire for creature spells.
        // (ETB is a CardMovedEvent, not SpellCastEvent — it won't fire here.)
        var prowessTriggers = triggers.PendingCount;
        prowessTriggers.Should().Be(0,
            "Prowess only triggers on noncreature spells (CR 702.108)");

        pm.Power.Should().Be(2, "no pump — creature spell does not trigger Prowess");
        pm.Toughness.Should().Be(2);
    }
}
