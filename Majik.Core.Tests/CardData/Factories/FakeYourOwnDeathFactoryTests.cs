using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FakeYourOwnDeathFactory"/> — Innistrad: Midnight Hunt
/// {1}{B} Instant.
///
///   "Until end of turn, target creature gets +2/+0 and gains 'When this
///    creature dies, return it to the battlefield tapped under its owner's
///    control and you create a Treasure token.'"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, cost, type, colour) — single _Identity assert.
/// - Single 1..1 target-creature request.
/// - Resolution: target gets +2/+0 and is granted the dies-trigger.
/// - Granted creature dies → returns to the battlefield tapped under its
///   OWNER's control (driven end-to-end via TriggerManager).
/// - The Treasure is created under the CASTER's control (the granted
///   ability's "you"), NOT the creature's owner.
/// - Granted trigger is NOT Undying/Persist (no +1/+1 or -1/-1 counter).
/// - The +2/+0 pump AND the granted trigger expire at end of turn (CR 514.2).
/// - Illegal target at resolution → clean no-op (no pump, no grant).
///
/// (CardFactoryContractTests already asserts NamedCardFactory dispatch +
/// well-formedness for every implemented card, so no dispatch test here.)
/// </summary>
[Trait("Color", "B")]
public class FakeYourOwnDeathFactoryTests
{
    private readonly Player _alice = new("Alice", 20); // caster
    private readonly Player _bob = new("Bob", 20);      // creature owner

    private static Creature MakeBear(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    // =========================================================================
    // Identity (single _Identity assert)
    // =========================================================================

    [Fact]
    public void FakeYourOwnDeath_Identity_1B_Black_Instant()
    {
        var card = FakeYourOwnDeathFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Fake Your Own Death");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "the {B} pip makes it black");
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.White);
    }

    // =========================================================================
    // SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreature_NoVariableX()
    {
        var def = FakeYourOwnDeathFactory.BuildSpellDefinition(_alice, o => o!);

        def.HasVariableX.Should().BeFalse("Fake Your Own Death is not an X-spell");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1, "target creature — exactly one");
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Resolution: +2/+0 + grant the dies-trigger
    // =========================================================================

    [Fact]
    public void Resolve_GivesPlusTwoZero_AndGrantsDeathTrigger()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        var granted = FakeYourOwnDeathFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeSameAs(bear);
        bear.Power.Should().Be(4, "target creature gets +2/+0 until end of turn (CR 613.7c)");
        bear.Toughness.Should().Be(2, "+2/+0 — toughness is unchanged");
        bear.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the dies → return-tapped + Treasure trigger is granted to the target");
    }

    [Fact]
    public void GrantedDeathTrigger_HasBothActiveZones()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        FakeYourOwnDeathFactory.Resolve(_alice, bear, o => o!);

        var trig = bear.Abilities.OfType<TriggeredAbility>().Single();
        trig.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trig.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "Graveyard must be in ActiveZones — the trigger evaluates after the death zone-move");
    }

    // =========================================================================
    // End-to-end: dies → returns tapped under owner's control + Treasure
    // =========================================================================

    [Fact]
    public void GrantedCreatureDies_ReturnsTapped_UnderOwnersControl_AndCasterGetsTreasure()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Bob owns the bear; Alice casts Fake Your Own Death on it.
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        FakeYourOwnDeathFactory.Resolve(_alice, bear, o => o!, zones);
        triggers.BindCard(bear);

        // The bear dies (Battlefield → Graveyard).
        zones.MoveCardTo(bear, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "the granted dies-trigger queues on death");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield, "the creature returns to the battlefield");
        bear.IsTapped.Should().BeTrue("the creature returns tapped (printed 'tapped')");
        bear.Controller.Should().BeSameAs(_bob,
            "returns under its OWNER's control (printed 'under its owner's control')");

        // "you create a Treasure token" — "you" = the caster (Alice), NOT the
        // creature's owner (Bob). CR 603.3d / 111.10.
        _alice.Zones.Battlefield.GetCards().OfType<Artifact>()
            .Count(a => a.HasSubtype(CardSubtype.Treasure)).Should().Be(1,
                "the caster creates exactly one Treasure token");
        _bob.Zones.Battlefield.GetCards().OfType<Artifact>()
            .Any(a => a.HasSubtype(CardSubtype.Treasure)).Should().BeFalse(
                "the Treasure goes to the caster, not the creature's owner");
    }

    [Fact]
    public void GrantedCreatureDies_NoCounters_NotUndyingOrPersist()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        FakeYourOwnDeathFactory.Resolve(_alice, bear, o => o!, zones);
        triggers.BindCard(bear);

        zones.MoveCardTo(bear, ZoneType.Graveyard);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Counters.Count(Majik.Core.Counters.CounterType.MinusOneMinusOne).Should().Be(0,
            "not Persist — no -1/-1 counter");
        bear.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne).Should().Be(0,
            "not Undying — no +1/+1 counter");
    }

    // =========================================================================
    // EOT expiry of both the pump and the grant (CR 514.2)
    // =========================================================================

    [Fact]
    public void PumpAndGrant_ExpireAtEndOfTurn()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        var effects = new ContinuousEffectsService();
        bear.ActiveEffects = effects;
        PutOnBattlefield(_bob, bear);

        FakeYourOwnDeathFactory.Resolve(_alice, bear, o => o!);
        bear.Power.Should().Be(4, "pump live before cleanup");
        bear.Abilities.OfType<TriggeredAbility>().Should().ContainSingle("grant live before cleanup");

        // CR 514.2 — cleanup step expires "until end of turn" effects.
        effects.ExpireEndOfTurn();

        bear.Power.Should().Be(2, "the +2/+0 expires at end of turn (CR 514.2)");
        bear.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the until-end-of-turn grant is revoked at cleanup (CR 514.2 / CR 613.6e)");
    }

    // =========================================================================
    // Illegal target at resolution (CR 608.2b/608.2c)
    // =========================================================================

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoPump_NoGrant()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        bear.SetZone(ZoneType.Hand); // not on the battlefield

        var granted = FakeYourOwnDeathFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeNull("illegal target at resolution → spell does nothing (CR 608.2c)");
        bear.Power.Should().Be(2, "no pump on an illegal target");
        bear.Abilities.OfType<TriggeredAbility>().Should().BeEmpty("no grant on an illegal target");
    }

    [Fact]
    public void Resolve_TargetNotACreature_NoOp()
    {
        var granted = FakeYourOwnDeathFactory.Resolve(
            _alice,
            rawTarget: "not-a-creature",
            resolver: _ => "not-a-creature");

        granted.Should().BeNull("non-creature target → spell does nothing");
    }
}
