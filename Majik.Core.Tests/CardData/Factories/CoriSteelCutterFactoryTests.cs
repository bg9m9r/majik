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
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CoriSteelCutterFactory"/> — Cori-Steel Cutter
/// (Tarkir: Dragonstorm, Artifact — Equipment {1}{R}).
///
/// Oracle text (Scryfall, 2025-04-11):
///   "Equipped creature gets +1/+1 and has trample and haste.
///    Flurry — Whenever you cast your second spell each turn, create a 1/1
///    white Monk creature token with prowess. You may attach this Equipment
///    to it.
///    Equip {1}{R}"
///
/// Covers:
/// - Card identity (name, Artifact + Equipment subtype, mana cost, owner /
///   controller).
/// - Ability set: a single Flurry <see cref="TriggeredAbility"/> + a single
///   Equip <see cref="ActivatedAbility"/>.
/// - Static +1/+1 + granted trample + haste apply at Layers 6 / 7c while
///   attached.
/// - Flurry trigger fires on the controller's second cast each turn,
///   spawns a 1/1 Monk token with the Prowess keyword marker, and
///   attaches Cori-Steel Cutter to that token.
/// - Equip {1}{R} attaches to the first controller-side creature.
/// - <see cref="NamedCardFactory"/> dispatch entry.
/// </summary>
public class CoriSteelCutterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Lightning Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CoriSteelCutter_Identity_ArtifactEquipment_AtCost1R()
    {
        var c = CoriSteelCutterFactory.Create(_alice);

        c.Name.Should().Be("Cori-Steel Cutter");
        c.ManaCost.Should().Be("{1}{R}");
        c.Should().BeOfType<Artifact>();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Cori-Steel Cutter is an Equipment (CR 301.5)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability set
    // -----------------------------------------------------------------------

    [Fact]
    public void CoriSteelCutter_HasOneTriggeredAbility_Flurry()
    {
        var c = CoriSteelCutterFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Flurry (second-spell-each-turn) is the only triggered ability");
    }

    [Fact]
    public void CoriSteelCutter_HasOneActivatedAbility_Equip()
    {
        var c = CoriSteelCutterFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Equip {1}{R} is the only activated ability");
    }

    // -----------------------------------------------------------------------
    // Static +1/+1 + trample + haste while attached
    // -----------------------------------------------------------------------

    /// <summary>
    /// "Equipped creature gets +1/+1 and has trample and haste."
    /// CR 613 — Layer 7c for the +1/+1 boost, Layer 6 for the granted
    /// keywords. The two AttachedBoostEffects gate on the source being on
    /// the battlefield AND attached.
    /// </summary>
    [Fact]
    public void CoriSteelCutter_WhileAttached_PumpsEquippedCreaturePlusOnePlusOne_AndGrantsTrampleHaste()
    {
        var effects = new ContinuousEffectsService();
        var cutter = CoriSteelCutterFactory.Create(_alice, effects, zoneService: null, eventBus: null, triggers: null);

        // Wire a bearer creature on the battlefield.
        var bearer = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bearer.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bearer);
        bearer.ActiveEffects = effects;

        // Put Cori-Steel Cutter onto the battlefield and attach it.
        cutter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cutter);
        cutter.AttachTo(bearer);

        // CR 613 Layer 7c — the +1/+1 lands on the bearer.
        bearer.Power.Should().Be(3, "+1 to base 2 power while equipped");
        bearer.Toughness.Should().Be(3, "+1 to base 2 toughness while equipped");

        // CR 613.1c — granted Trample + Haste keywords visible on the
        // working keyword set (the layers compute populates
        // CreatureCharacteristics.Keywords).
        var chars = effects.Compute(bearer);
        chars.Keywords.Should().Contain("Trample",
            "Cori-Steel Cutter grants trample to the equipped creature (Layer 6)");
        chars.Keywords.Should().Contain("Haste",
            "Cori-Steel Cutter grants haste to the equipped creature (Layer 6)");
    }

    [Fact]
    public void CoriSteelCutter_WhileNotAttached_DoesNotBuffOtherCreatures()
    {
        var effects = new ContinuousEffectsService();
        var cutter = CoriSteelCutterFactory.Create(_alice, effects, zoneService: null, eventBus: null, triggers: null);

        var loner = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        loner.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(loner);
        loner.ActiveEffects = effects;

        cutter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cutter);
        // No AttachTo call — the equipment is unattached.

        loner.Power.Should().Be(2);
        loner.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Flurry trigger — second spell each turn
    // -----------------------------------------------------------------------

    /// <summary>
    /// Flurry — "Whenever you cast your second spell each turn, create a 1/1
    /// white Monk creature token with prowess. You may attach this Equipment
    /// to it." CR 603.1 + Tarkir: Dragonstorm Flurry keyword. The trigger
    /// only fires on the exact transition to 2 casts per turn; the first
    /// cast must publish its own SpellCastEvent so the predicate increments.
    /// </summary>
    [Fact]
    public void Flurry_SecondSpellThisTurn_SpawnsMonkTokenAndAttachesCutter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);
        var effects = new ContinuousEffectsService();

        var cutter = CoriSteelCutterFactory.Create(_alice, effects, zones, bus, triggers);
        cutter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cutter);
        triggers.BindCard(cutter);

        // First cast — predicate increments to 1, no trigger fires.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(0,
            "Flurry requires the controller's SECOND cast — first cast is silent");

        // Second cast — predicate increments to 2 and the Flurry trigger
        // fires.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Shock")));
        triggers.PendingCount.Should().Be(1,
            "Flurry trigger queues on the controller's second cast this turn");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // One new creature on Alice's battlefield — the Monk token.
        var newCreatures = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();
        newCreatures.Should().HaveCount(1, "Flurry spawns exactly one Monk token");

        var token = newCreatures[0];
        token.Name.Should().Be("Monk");
        token.IsToken.Should().BeTrue("CR 111 — Flurry creates a token");
        token.HasSubtype(CardSubtype.Monk).Should().BeTrue(
            "the spawned token carries the Monk creature subtype");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.Controller.Should().BeSameAs(_alice);

        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(a => a.Keyword == "Prowess",
                "CR 702.108 — printed token has Prowess; KeywordAbility marker " +
                "attached via TokenFactory's Keywords list (live pump deferred)");

        // CR 117 — the may-clause is auto-accepted at v1; the Equipment
        // ends up attached to the new Monk token.
        cutter.AttachedTo.Should().BeSameAs(token,
            "Flurry auto-accepts the may-clause and attaches Cori-Steel Cutter " +
            "to the spawned Monk token");
    }

    /// <summary>
    /// Per-turn count must reset on <see cref="TurnStartedEvent"/> (CR 500.1).
    /// After a new turn, the very next cast must count as the first again —
    /// the second cast of the new turn fires Flurry, not the second cast
    /// across the lifetime of the card.
    /// </summary>
    [Fact]
    public void Flurry_PerTurnCount_ResetsOnTurnStart()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var cutter = CoriSteelCutterFactory.Create(_alice, effects, zoneService: null, eventBus: bus, triggers: triggers);
        cutter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cutter);
        triggers.BindCard(cutter);

        // Burn the first two casts in turn 1 — fires once.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice)));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice)));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // New turn — count resets.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        // First cast of turn 2 must NOT fire Flurry (the counter starts at 0
        // for the new turn).
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice)));
        triggers.PendingCount.Should().Be(0,
            "TurnStartedEvent must reset the per-turn cast count so the first " +
            "cast of the new turn is once again the first cast");

        // Second cast of turn 2 fires.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice)));
        triggers.PendingCount.Should().Be(1,
            "the second cast of the new turn fires Flurry");
    }

    // -----------------------------------------------------------------------
    // Equip {1}{R}
    // -----------------------------------------------------------------------

    /// <summary>
    /// Equip {1}{R} — "Attach to target creature you control." (CR 702.6).
    /// v1 picker is deterministic: the first controller-side creature on
    /// the battlefield.
    /// </summary>
    [Fact]
    public void EquipActivation_AttachesToFirstControllerSideCreature()
    {
        var cutter = CoriSteelCutterFactory.Create(_alice);
        cutter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cutter);

        var bearer = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bearer.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bearer);

        var equip = cutter.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in equip.Effects)
        {
            effect.Execute();
        }

        cutter.AttachedTo.Should().BeSameAs(bearer,
            "Equip attaches Cori-Steel Cutter to the first controller-side creature");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_DispatchesCoriSteelCutter()
    {
        var card = NamedCardFactory.Create("Cori-Steel Cutter", _alice);

        card.Should().BeOfType<Artifact>("Cori-Steel Cutter is an Artifact — Equipment");
        card.Name.Should().Be("Cori-Steel Cutter");
        card.HasSubtype(CardSubtype.Equipment).Should().BeTrue();

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the dispatcher returns a Cori-Steel Cutter shape with the Flurry trigger attached");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the dispatcher returns a Cori-Steel Cutter shape with the Equip activated ability attached");
    }
}
