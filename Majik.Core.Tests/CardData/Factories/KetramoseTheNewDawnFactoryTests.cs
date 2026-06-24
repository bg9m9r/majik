using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KetramoseTheNewDawnFactory"/> (Aetherdrift,
/// {1}{W}{B}).
///
/// Legendary Creature — God 4/4. Oracle text (verified against Scryfall):
///   "Menace, lifelink, indestructible
///    Ketramose can't attack or block unless there are seven or more cards
///    in exile.
///    Whenever one or more cards are put into exile from graveyards and/or
///    the battlefield during your turn, you draw a card and lose 1 life."
///
/// Covers (unique behaviour only — dispatch + well-formedness are asserted
/// for every implemented card by CardFactoryContractTests):
///   - Identity / shape (mana cost, P/T, supertype, subtype).
///   - Menace + Lifelink + Indestructible keyword markers.
///   - "Can't attack or block unless seven or more cards in exile"
///     predicate-mode CombatRestrictionEffects (CannotAttack + CannotBlock),
///     gated to Ketramose, evaluated against a live exile count.
///   - Exile trigger: condition filtering (ToZone == Exile from Graveyard /
///     Battlefield, controller's turn) + resolve (draw + lose 1 life).
/// </summary>
[Trait("Color", "M")]
public class KetramoseTheNewDawnFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_ShipsLegendaryGodShape()
    {
        var ketramose = KetramoseTheNewDawnFactory.Create(_alice);

        ketramose.Should().BeOfType<Creature>();
        ketramose.Name.Should().Be("Ketramose, the New Dawn");
        ketramose.Power.Should().Be(4);
        ketramose.Toughness.Should().Be(4);
        ketramose.ManaCost.Should().Be("{1}{W}{B}");
        ketramose.ManaCostValue.TotalValue.Should().Be(3);
        ketramose.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ketramose.HasSubtype(CardSubtype.God).Should().BeTrue();
        ketramose.Owner.Should().BeSameAs(_alice);
        ketramose.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------------
    // Keyword markers
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_AttachesMenace_Lifelink_Indestructible()
    {
        var ketramose = KetramoseTheNewDawnFactory.Create(_alice);

        var keywords = ketramose.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        keywords.Should().Contain("Menace");
        keywords.Should().Contain("Lifelink");
        keywords.Should().Contain("Indestructible");
    }

    // -------------------------------------------------------------------------
    // Can't attack / block unless seven or more cards in exile
    // -------------------------------------------------------------------------

    [Fact]
    public void FewerThanSevenInExile_KetramoseCannotAttackOrBlock()
    {
        var effects = new ContinuousEffectsService();
        var exileCount = 6;
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, effects, triggers: null,
            exileCardCount: () => exileCount, isControllersTurn: null);
        _alice.Zones.Battlefield.AddCard(ketramose);
        ketramose.SetZone(ZoneType.Battlefield);

        effects.HasRestriction(ketramose, CombatRestriction.CannotAttack)
            .Should().BeTrue("six cards in exile < seven — can't attack");
        effects.HasRestriction(ketramose, CombatRestriction.CannotBlock)
            .Should().BeTrue("six cards in exile < seven — can't block");
    }

    [Fact]
    public void SevenOrMoreInExile_KetramoseCanAttackAndBlock()
    {
        var effects = new ContinuousEffectsService();
        var exileCount = 7;
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, effects, triggers: null,
            exileCardCount: () => exileCount, isControllersTurn: null);
        _alice.Zones.Battlefield.AddCard(ketramose);
        ketramose.SetZone(ZoneType.Battlefield);

        effects.HasRestriction(ketramose, CombatRestriction.CannotAttack)
            .Should().BeFalse("seven cards in exile satisfies 'seven or more'");
        effects.HasRestriction(ketramose, CombatRestriction.CannotBlock)
            .Should().BeFalse("seven cards in exile satisfies 'seven or more'");
    }

    [Fact]
    public void Restriction_RisesToSeven_LiftsImmediately()
    {
        var effects = new ContinuousEffectsService();
        var exileCount = 6;
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, effects, triggers: null,
            exileCardCount: () => exileCount, isControllersTurn: null);
        _alice.Zones.Battlefield.AddCard(ketramose);
        ketramose.SetZone(ZoneType.Battlefield);

        effects.HasRestriction(ketramose, CombatRestriction.CannotAttack).Should().BeTrue();

        // A seventh card hits exile — the predicate re-reads the live count.
        exileCount = 7;

        effects.HasRestriction(ketramose, CombatRestriction.CannotAttack)
            .Should().BeFalse("predicate re-reads the live exile count every pass");
    }

    [Fact]
    public void Restriction_GatedToKetramoseOnly_NotOtherCreatures()
    {
        var effects = new ContinuousEffectsService();
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, effects, triggers: null,
            exileCardCount: () => 0, isControllersTurn: null);
        _alice.Zones.Battlefield.AddCard(ketramose);
        ketramose.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        effects.HasRestriction(ketramose, CombatRestriction.CannotAttack).Should().BeTrue();
        effects.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeFalse("the restriction is scoped to Ketramose only");
    }

    [Fact]
    public void Restriction_SuppressedOffBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, effects, triggers: null,
            exileCardCount: () => 0, isControllersTurn: null);
        // Not on the battlefield — static restriction is suppressed
        // (CR 603.6e). Empty exile would otherwise lock it.

        effects.HasRestriction(ketramose, CombatRestriction.CannotAttack)
            .Should().BeFalse("static restriction functions only on the battlefield");
    }

    // -------------------------------------------------------------------------
    // Exile trigger — condition
    // -------------------------------------------------------------------------

    [Fact]
    public void ExileTrigger_FiresOnExileFromGraveyard_DuringYourTurn()
    {
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, continuousEffects: null, triggers: null,
            exileCardCount: null, isControllersTurn: () => true);

        var trigger = ketramose.Abilities.OfType<TriggeredAbility>().Single();
        var moved = new Card("Some Card", "");
        var evt = new CardMovedEvent(moved, ZoneType.Graveyard, ZoneType.Exile, _alice);

        trigger.Condition.Matches(evt, trigger).Should().BeTrue();
    }

    [Fact]
    public void ExileTrigger_FiresOnExileFromBattlefield_DuringYourTurn()
    {
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, continuousEffects: null, triggers: null,
            exileCardCount: null, isControllersTurn: () => true);

        var trigger = ketramose.Abilities.OfType<TriggeredAbility>().Single();
        var moved = new Card("Some Card", "");
        var evt = new CardMovedEvent(moved, ZoneType.Battlefield, ZoneType.Exile, _alice);

        trigger.Condition.Matches(evt, trigger).Should().BeTrue();
    }

    [Fact]
    public void ExileTrigger_DoesNotFireOnExileFromHand()
    {
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, continuousEffects: null, triggers: null,
            exileCardCount: null, isControllersTurn: () => true);

        var trigger = ketramose.Abilities.OfType<TriggeredAbility>().Single();
        var moved = new Card("Some Card", "");
        // Hand → Exile is not "from graveyards and/or the battlefield".
        var evt = new CardMovedEvent(moved, ZoneType.Hand, ZoneType.Exile, _alice);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse();
    }

    [Fact]
    public void ExileTrigger_DoesNotFireOnMoveToGraveyard()
    {
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, continuousEffects: null, triggers: null,
            exileCardCount: null, isControllersTurn: () => true);

        var trigger = ketramose.Abilities.OfType<TriggeredAbility>().Single();
        var moved = new Card("Some Card", "");
        // Battlefield → Graveyard (a normal death) is not an exile.
        var evt = new CardMovedEvent(moved, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse();
    }

    [Fact]
    public void ExileTrigger_DoesNotFireOutsideYourTurn()
    {
        var ketramose = KetramoseTheNewDawnFactory.Create(
            _alice, continuousEffects: null, triggers: null,
            exileCardCount: null, isControllersTurn: () => false);

        var trigger = ketramose.Abilities.OfType<TriggeredAbility>().Single();
        var moved = new Card("Some Card", "");
        var evt = new CardMovedEvent(moved, ZoneType.Battlefield, ZoneType.Exile, _alice);

        trigger.Condition.Matches(evt, trigger)
            .Should().BeFalse("the trigger only fires during the controller's turn");
    }

    // -------------------------------------------------------------------------
    // Exile trigger — resolve
    // -------------------------------------------------------------------------

    [Fact]
    public void ExileTrigger_Resolve_DrawsOneAndLosesOneLife()
    {
        var ketramose = KetramoseTheNewDawnFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ketramose);
        ketramose.SetZone(ZoneType.Battlefield);

        // Seed a library card so the draw has something to take.
        var top = new Card("Library Card", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        var handBefore = _alice.Zones.Hand.GetCards().Count();
        var lifeBefore = _alice.LifeTotal;

        var trigger = ketramose.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1, "draw a card");
        _alice.LifeTotal.Should().Be(lifeBefore - 1, "lose 1 life");
    }
}
