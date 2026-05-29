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
/// Tests for <see cref="RestlessVentsFactory"/> (March of the Machine
/// manland cycle, B/R member — sibling of
/// <see cref="DenOfTheBugbearFactory"/>). Land:
///   "This land enters tapped.
///    {T}: Add {B} or {R}.
///    {1}{B}{R}: Until end of turn, this land becomes a 2/3 black and red
///    Insect creature with menace. It's still a land.
///    Whenever this land attacks, you may discard a card. If you do, draw
///    a card."
///
/// Mirrors <see cref="DenOfTheBugbearTests"/> (the suggested analogue):
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Dual mana ability ({T}: Add {B} or {R}) + animate ability +
///   attack-trigger shape.
/// - Animate registers a <see cref="ManlandCycleAnimateEffect"/> +
///   <see cref="ManlandCycleBecomesPTEffect"/>:
///     * Adds Creature type + Insect subtype + Menace keyword on Layer 4.
///     * Records 2/3 base P/T on Layer 7b.
/// - Unconditional ETB-tapped.
/// - Attack trigger is a rummage loot (discard a card, if you do draw one).
/// </summary>
public class RestlessVentsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessVents_Identity()
    {
        var land = RestlessVentsFactory.Create(_alice);

        land.Name.Should().Be("Restless Vents");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Vents is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RestlessVents()
    {
        var card = NamedCardFactory.Create("Restless Vents", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Restless Vents");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {B} or {R} is two mana abilities");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{1}{B}{R} animate ability is wired");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack-trigger loot shape is attached for inspection");
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessVents_AnimateAbility_HasPrintedManaCost1BR()
    {
        var land = RestlessVentsFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({1}{B}{R})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessVents_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessVentsFactory.Create(_alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 (\"It's still a land\")");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Insect,
            "Insect subtype added");
        chars.Keywords.Should().Contain("Menace",
            "the animated body has menace");
    }

    // -----------------------------------------------------------------------
    // ETB tapped (unconditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessVents_RegistersEntersTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessVentsFactory.Create(_alice, effects: null, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Restless Vents always enters tapped");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — rummage loot (discard a card, if you do draw one)
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessVents_AttackTrigger_DiscardsThenDraws()
    {
        var land = RestlessVentsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Seed hand + library so the rummage can both discard and draw.
        var toDiscard = NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Hand.AddCard(toDiscard);
        toDiscard.SetZone(ZoneType.Hand);

        var toDraw = NamedCardFactory.Create("Swamp", _alice);
        _alice.Zones.Library.AddCard(toDraw);
        toDraw.SetZone(ZoneType.Library);

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // One discarded, one drawn → net hand size unchanged.
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "rummage discards one then draws one — net zero");
        _alice.Zones.Graveyard.GetCards().Should().Contain(toDiscard,
            "the discarded card is in the graveyard");
        _alice.Zones.Hand.GetCards().Should().Contain(toDraw,
            "the drawn card entered hand");
    }

    [Fact]
    public void RestlessVents_AttackTrigger_EmptyHand_DrawsNothing()
    {
        // "you may discard a card. If you do, draw a card." With no card to
        // discard, the "if you do" clause fails and no draw happens.
        var land = RestlessVentsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var toDraw = NamedCardFactory.Create("Swamp", _alice);
        _alice.Zones.Library.AddCard(toDraw);
        toDraw.SetZone(ZoneType.Library);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no discard means no draw (intervening 'if you do')");
        _alice.Zones.Library.GetCards().Should().Contain(toDraw,
            "the would-be drawn card stays in the library");
    }
}
