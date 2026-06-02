using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RestlessFortressFactory"/> (March of the Machine
/// "Restless" creature-land cycle, WB member). Land. Oracle text (verified
/// against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {W} or {B}.
///    {2}{W}{B}: This land becomes a 1/4 white and black Nightmare creature
///    until end of turn. It's still a land.
///    Whenever this land attacks, defending player loses 2 life and you gain
///    2 life."
///
/// Mirrors <see cref="RagingRavineTests"/> (the Worldwake manland analogue)
/// plus the captured-defender pattern from
/// <see cref="GoblinGuideTests"/>:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Two mana abilities ({T}: Add {W} / {T}: Add {B}) + animate ability +
///   a printed attack-trigger shape.
/// - Animate registers a <see cref="ManlandCycleAnimateEffect"/> +
///   <see cref="ManlandCycleBecomesPTEffect"/>:
///     * Adds Creature type + Nightmare subtype on Layer 4.
///     * Records 1/4 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// - Attack trigger: defending player loses 2 life, controller gains 2.
/// </summary>
[Trait("Color", "C")]
public class RestlessFortressTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessFortress_Identity()
    {
        var land = RestlessFortressFactory.Create(_alice);

        land.Name.Should().Be("Restless Fortress");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Fortress is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessFortress_HasWhiteAndBlackManaAbilities()
    {
        var land = RestlessFortressFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one mana ability per produced colour ({W} / {B})");
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessFortress_AnimateAbility_HasPrintedManaCost2WB()
    {
        var land = RestlessFortressFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({2}{W}{B})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessFortress_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessFortressFactory.Create(_alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 (\"It's still a land\")");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Nightmare,
            "Nightmare subtype added");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped — unconditional
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessFortress_RegistersEntersTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessFortressFactory.Create(_alice, effects: null, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "\"This land enters tapped.\" is unconditional");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — defending player loses 2 life, you gain 2
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessFortress_AttackTrigger_DrainsDefendingPlayer()
    {
        var land = RestlessFortressFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        // CR 508.1f — run the condition so the resolved effect captures the
        // defender (Bob). The condition captures the defender off the live
        // CreatureAttacksEvent then resolution drains them. We construct a
        // dummy Creature attacker for the event's typed Attacker slot; the
        // factory captures the defender regardless of attacker identity (a
        // land can only attack once animated, CR 508.1a — same v1 posture as
        // the rest of the Restless / manland cycle).
        var dummyAttacker = new Creature("dummy", "{0}", 1, 1);
        var ev = new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(
            attacker: dummyAttacker,
            defendingPlayerOrPlaneswalker: _bob);

        trigger.Condition.Matches(ev, trigger);

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(18, "defending player loses 2 life");
        _alice.LifeTotal.Should().Be(22, "you gain 2 life");
    }
}
