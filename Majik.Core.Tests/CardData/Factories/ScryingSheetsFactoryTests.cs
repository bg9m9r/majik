using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ScryingSheetsFactory"/>.
///
/// Oracle (Scryfall-confirmed, Coldsnap):
///   "Snow Land
///    {T}: Add {C}.
///    {1}{S}, {T}: Look at the top card of your library. If that card is
///    snow, you may reveal it and put it into your hand. ({S} can be paid
///    with one mana from a snow source.)"
///
/// Covers:
/// - Identity: Snow Land, name, non-basic, non-legendary.
/// - Two abilities: one <see cref="ManaAbility"/> ({T}: Add {C}) + one
///   <see cref="ActivatedAbility"/> ({1}{S},{T}: look + conditional reveal).
/// - Mana ability: {T} produces {C}.
/// - Activated ability cost: {1}{S} mana + tap.
/// - Resolve: snow top card → revealed + moved to hand (CR 701.16).
/// - Resolve: non-snow top card → stays on top of library, hand unchanged.
/// - Resolve: empty library → clean no-op.
/// </summary>
[Trait("Color", "C")]
public class ScryingSheetsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land PlaceOnBattlefield()
    {
        var sheets = ScryingSheetsFactory.Create(_alice);
        sheets.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sheets);
        return sheets;
    }

    private void AddCardToTopOfLibrary(string name, bool snow)
    {
        var supertypes = snow ? new[] { CardSupertype.Snow } : null;
        var card = new Land(name, supertypes: supertypes)
            { Owner = _alice, Controller = _alice };
        card.SetZone(ZoneType.Library);
        // Library.AddCard inserts at position 0 (top), so the most recently
        // added card is the top card.
        _alice.Zones.Library.AddCard(card);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsSnowLand_NamedScryingSheets()
    {
        var sheets = ScryingSheetsFactory.Create(_alice);
        sheets.Name.Should().Be("Scrying Sheets");
        sheets.HasType(CardType.Land).Should().BeTrue();
        sheets.HasSupertype(CardSupertype.Snow).Should().BeTrue("Scrying Sheets is a Snow Land");
        sheets.HasSupertype(CardSupertype.Basic).Should().BeFalse("Scrying Sheets is nonbasic");
        sheets.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        sheets.Owner.Should().BeSameAs(_alice);
        sheets.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedFactory_Dispatch_ResolvesScryingSheets()
    {
        var card = NamedCardFactory.Create("Scrying Sheets", _alice);
        card.Should().NotBeNull();
        card!.Name.Should().Be("Scrying Sheets");
        card.HasSupertype(CardSupertype.Snow).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyTwoAbilities_OneManaOneActivated()
    {
        var sheets = ScryingSheetsFactory.Create(_alice);
        sheets.Abilities.Should().HaveCount(2,
            "one {T}: Add {C} mana ability + one {1}{S},{T} activated ability");
        sheets.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        sheets.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ManaAbility_Activate_ProducesOneColorless()
    {
        var sheets = PlaceOnBattlefield();
        var mana = (IManaAbility)sheets.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.TotalValue.Should().Be(1, "the mana ability produces one {C}");
        produced.Generic.Should().Be(1, "{C} colorless mana is tracked as generic in the pool");
        sheets.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void ActivatedAbility_HasExpectedCosts()
    {
        var sheets = ScryingSheetsFactory.Create(_alice);
        var ability = sheets.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "costs are {1}{S} mana + tap");
        ability.Costs.Should().ContainItemsAssignableTo<ManaCostCost>(
            "one cost must be the {1}{S} mana cost");
        ability.Costs.Should().Contain(c => c is AdditionalCost,
            "one cost must be the {T} tap cost");
    }

    // -----------------------------------------------------------------------
    // Resolve: snow top card → revealed + put into hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_SnowTopCard_RevealsAndPutsIntoHand()
    {
        AddCardToTopOfLibrary("Snow-Covered Island", snow: true);
        var sheets = PlaceOnBattlefield();
        var ability = sheets.Abilities.OfType<ActivatedAbility>().Single();

        ability.Effects.Single().Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle(
            "a snow top card is revealed and put into hand");
        _alice.Zones.Hand.GetCards().Single().Name.Should().Be("Snow-Covered Island");
        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "the snow card was removed from the top of the library");
    }

    [Fact]
    public void LookAndReveal_SnowTopCard_PublishesCardRevealedEvent()
    {
        AddCardToTopOfLibrary("Snow-Covered Island", snow: true);

        CardRevealedEvent? captured = null;
        var bus = new Majik.Core.Events.EventBus();
        bus.Subscribe<CardRevealedEvent>(e => captured = e);

        ScryingSheetsFactory.LookAndReveal(_alice, bus);

        captured.Should().NotBeNull("a snow top card is revealed (CR 701.16)");
        captured!.Card.Name.Should().Be("Snow-Covered Island");
        captured.From.Should().Be(ZoneType.Library);
        captured.Player.Should().BeSameAs(_alice);

        _alice.Zones.Hand.GetCards().Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    // Resolve: non-snow top card → stays on top of library
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_NonSnowTopCard_StaysOnTopOfLibrary()
    {
        AddCardToTopOfLibrary("Island", snow: false);
        var sheets = PlaceOnBattlefield();
        var ability = sheets.Abilities.OfType<ActivatedAbility>().Single();

        ability.Effects.Single().Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "a non-snow top card cannot be put into hand");
        _alice.Zones.Library.GetCards().Should().ContainSingle(
            "the non-snow card stays on top of the library");
        _alice.Zones.Library.GetCards().Single().Name.Should().Be("Island");
    }

    // -----------------------------------------------------------------------
    // Resolve: empty library → clean no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_EmptyLibrary_IsCleanNoOp()
    {
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        var sheets = PlaceOnBattlefield();
        var ability = sheets.Abilities.OfType<ActivatedAbility>().Single();

        ability.Effects.Single().Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
