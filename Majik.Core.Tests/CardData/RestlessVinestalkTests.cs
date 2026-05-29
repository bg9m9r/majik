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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RestlessVinestalkFactory"/> (Wilds of Eldraine
/// "Restless" creature-land cycle, G/U member). Land:
///   "This land enters tapped.
///    {T}: Add {G} or {U}.
///    {3}{G}{U}: Until end of turn, this land becomes a 5/5 green and blue
///    Plant creature with trample. It's still a land.
///    Whenever this land attacks, up to one other target creature has base
///    power and toughness 3/3 until end of turn."
///
/// Mirrors <see cref="DenOfTheBugbearTests"/> (the suggested analogue) and
/// the <see cref="LumberingFallsFactory"/> manland-cycle shape:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Mana abilities ({T}: Add {G} or {U}) + {3}{G}{U} animate ability +
///   targeted attack-trigger shape.
/// - Animate registers a <see cref="ManlandCycleAnimateEffect"/> (Creature +
///   Plant + Trample, Layer 4) + <see cref="ManlandCycleBecomesPTEffect"/>
///   (5/5, Layer 7b); both expire at end of turn.
/// - Unconditional ETB-tapped.
/// - Attack trigger sets up-to-one OTHER target creature to base P/T 3/3
///   until end of turn (CR 613.7b, expires EOT).
/// </summary>
public class RestlessVinestalkTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessVinestalk_Identity()
    {
        var land = RestlessVinestalkFactory.Create(_alice);

        land.Name.Should().Be("Restless Vinestalk");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Vinestalk is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RestlessVinestalk()
    {
        var card = NamedCardFactory.Create("Restless Vinestalk", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Restless Vinestalk");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {G} or {U} is two mana abilities");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{3}{G}{U} animate ability is wired");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack-trigger shape is attached for inspection");
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessVinestalk_AnimateAbility_HasPrintedManaCost3GU()
    {
        var land = RestlessVinestalkFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({3}{G}{U})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessVinestalk_Animate_AppliesLayer4AndLayer7bOnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessVinestalkFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 (\"It's still a land\")");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Plant,
            "Plant subtype added");
        chars.Keywords.Should().Contain("Trample",
            "the animated body has trample");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped — unconditional ("This land enters tapped.")
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessVinestalk_RegistersEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessVinestalkFactory.Create(
            _alice, effects: null, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Restless Vinestalk always enters tapped");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "up to one other target creature has base P/T 3/3"
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessVinestalk_AttackTrigger_IsUpToOneTarget()
    {
        var land = RestlessVinestalkFactory.Create(_alice);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().ContainSingle(
            "one 'up to one other target creature' request");
        var req = trigger.TargetRequests.Single();
        req.MinTargets.Should().Be(0, "up to one — optional");
        req.MaxTargets.Should().Be(1, "at most one target creature");
    }

    [Fact]
    public void RestlessVinestalk_AttackTrigger_SetsTargetBasePT_3_3_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessVinestalkFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // A 7/7 victim creature.
        var victim = new Creature("Colossus", "{6}", 7, 7);
        victim.SetOwner(_alice);
        victim.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { victim }
        });
        foreach (var e in trigger.Effects) e.Execute();

        var chars = effects.Compute(victim);
        chars.Power.Should().Be(3, "base power set to 3");
        chars.Toughness.Should().Be(3, "base toughness set to 3");

        // The effect expires at end of turn (CR 514.2 cleanup).
        effects.ExpireEndOfTurn();
        var after = effects.Compute(victim);
        after.Power.Should().Be(7, "the 3/3 set-base effect expired at end of turn");
        after.Toughness.Should().Be(7, "the 3/3 set-base effect expired at end of turn");
    }

    [Fact]
    public void RestlessVinestalk_AttackTrigger_NoTarget_IsNoOp()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessVinestalkFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        // No targets chosen ("up to one" — controller chose zero).
        var act = () =>
        {
            foreach (var e in trigger.Effects) e.Execute();
        };
        act.Should().NotThrow("up to one target — zero is legal and a no-op");
    }
}
