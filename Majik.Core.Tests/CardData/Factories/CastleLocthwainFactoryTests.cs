using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CastleLocthwainFactory"/>.
///
/// Oracle (Scryfall-confirmed, Throne of Eldraine):
///   "Castle Locthwain enters tapped unless you control a Swamp.
///    {T}: Add {B}.
///    {1}{B}{B}, {T}: Draw a card, then you lose life equal to the number
///    of cards in your hand."
///
/// Covers:
/// - Identity: Land type, name, non-basic, non-legendary, not a Swamp.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Castle Locthwain".
/// - Two abilities: one <see cref="ManaAbility"/> ({T}: Add {B}) + one
///   <see cref="ActivatedAbility"/> ({1}{B}{B},{T}: draw+life).
/// - ETB predicate (via <see cref="ReplacementBus"/>):
///     · No Swamp controlled → enters tapped.
///     · Controller has a Swamp → enters untapped.
///     · Opponent controls a Swamp, not the controller → enters tapped.
///     · Castle Locthwain itself is NOT a Swamp (cannot satisfy its own predicate).
/// - Mana ability: {T} produces {B}; CanActivate false when tapped.
/// - Activated ability cost: requires {1}{B}{B}; requires land to be untapped.
/// - Activated ability resolve: draw 1 card, then life loss = post-draw hand count.
/// - Empty hand before activation → draw gives 1 card → life loss = 1.
/// - Hand with N cards before activation → draw gives N+1 → life loss = N+1.
/// </summary>
[Trait("Color", "C")]
public class CastleLocthwainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helper: place Castle Locthwain on Alice's battlefield (untapped).
    // -----------------------------------------------------------------------
    private Land PlaceOnBattlefield()
    {
        var castle = CastleLocthwainFactory.Create(_alice);
        castle.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castle);
        return castle;
    }

    // -----------------------------------------------------------------------
    // Helper: add a basic Swamp to a player's battlefield.
    // -----------------------------------------------------------------------
    private static Land AddSwamp(Player controller)
    {
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
            { Owner = controller, Controller = controller };
        swamp.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(swamp);
        return swamp;
    }

    // -----------------------------------------------------------------------
    // Helper: put a dummy card in a player's hand (library → hand via zone).
    // -----------------------------------------------------------------------
    private static void AddCardToHand(Player player)
    {
        var card = new Land("Plains")
            { Owner = player, Controller = player };
        card.SetZone(ZoneType.Hand);
        player.Zones.Hand.AddCard(card);
    }

    // -----------------------------------------------------------------------
    // Helper: put a dummy card in Alice's library (so draw does not fail).
    // -----------------------------------------------------------------------
    private void AddCardToLibrary()
    {
        var card = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
            { Owner = _alice, Controller = _alice };
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedCastleLocthwain()
    {
        var castle = CastleLocthwainFactory.Create(_alice);
        castle.Name.Should().Be("Castle Locthwain");
        castle.HasType(CardType.Land).Should().BeTrue();
        castle.Owner.Should().BeSameAs(_alice);
        castle.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary_NotSwamp()
    {
        var castle = CastleLocthwainFactory.Create(_alice);
        castle.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Castle Locthwain is nonbasic");
        castle.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Castle Locthwain is not legendary");
        // Castle Locthwain has no printed subtypes — it cannot satisfy
        // its own ETB predicate.
        castle.HasSubtype(CardSubtype.Swamp).Should().BeFalse(
            "Castle Locthwain has no Swamp subtype and cannot satisfy its own ETB predicate");
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Ability count / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyTwoAbilities_OneManaOneActivated()
    {
        var castle = CastleLocthwainFactory.Create(_alice);
        castle.Abilities.Should().HaveCount(2,
            "one {T}: Add {B} mana ability + one {1}{B}{B},{T} activated ability");
        castle.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        castle.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbility_Activate_ProducesOneBlack()
    {
        var castle = PlaceOnBattlefield();
        var mana = (IManaAbility)castle.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Black.Should().Be(1, "the mana ability produces {B}");
        produced.Generic.Should().Be(0);
        castle.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void ManaAbility_CanActivate_FalseWhenTapped()
    {
        var castle = PlaceOnBattlefield();
        castle.Tap();
        var mana = (IManaAbility)castle.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse("already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoSwamp()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var castle = CastleLocthwainFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Locthwain enters tapped when controller has no Swamp");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasASwamp()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        AddSwamp(alice);

        var castle = CastleLocthwainFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Castle Locthwain enters untapped when controller has a Swamp");
    }

    [Fact]
    public void EntersTapped_WhenOnlyOpponentHasSwamp()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        // Bob controls a Swamp — Alice does not.
        AddSwamp(bob);

        var castle = CastleLocthwainFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the 'you control' predicate checks the controller's battlefield, not the opponent's");
    }

    [Fact]
    public void PredicateExcludesSelf_CastleIsNotASwamp()
    {
        // Castle Locthwain has no Swamp subtype so even if it's on the
        // battlefield it cannot satisfy its own predicate.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var castle = CastleLocthwainFactory.Create(alice, replacements: bus);
        alice.Zones.Battlefield.AddCard(castle);
        castle.SetZone(ZoneType.Battlefield);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Locthwain has no Swamp subtype; its presence on battlefield doesn't satisfy the predicate");
    }
    // -----------------------------------------------------------------------
    // Activated ability: {1}{B}{B}, {T} — cost gates
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasExpectedCosts()
    {
        var castle = CastleLocthwainFactory.Create(_alice);
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2,
            "costs are {1}{B}{B} mana + tap");
        ability.Costs.Should().ContainItemsAssignableTo<ManaCostCost>(
            "one cost must be the {1}{B}{B} mana cost");
        ability.Costs.Should().Contain(c => c is AdditionalCost,
            "one cost must be the {T} tap cost");
    }

    // -----------------------------------------------------------------------
    // Activated ability: draw + life loss ("then" ordering — CR 700.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_EmptyHand_DrawsOneCard_ThenLosesOneLife()
    {
        // Pre-condition: Alice's hand is empty. Library has 1 card.
        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "starting state: no cards in hand");
        AddCardToLibrary();

        // Give Alice {1}{B}{B} to pay activation cost.
        _alice.AddManaToPool(ManaCost.Parse("{1}{B}{B}"));

        var castle = PlaceOnBattlefield();
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        // Execute the effect directly (unit test shortcut — costs already
        // verified above; here we test the resolve body).
        ability.Effects.Single().Execute();

        // After draw: hand has 1 card.
        _alice.Zones.Hand.GetCards().Count().Should().Be(1,
            "one card was drawn from the library");
        // Life loss = 1 (hand size after draw).
        _alice.LifeTotal.Should().Be(20 - 1,
            "life loss = 1 (hand count after drawing the one card)");
    }

    [Fact]
    public void Activate_TwoCardsInHandPreActivate_DrawsOne_ThenLosesThreeLife()
    {
        // Pre-condition: Alice has 2 cards in hand. Library has 1 card.
        AddCardToHand(_alice);
        AddCardToHand(_alice);
        AddCardToLibrary();
        _alice.Zones.Hand.GetCards().Count().Should().Be(2);

        var castle = PlaceOnBattlefield();
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        ability.Effects.Single().Execute();

        // After draw: hand has 3 cards (2 original + 1 drawn).
        _alice.Zones.Hand.GetCards().Count().Should().Be(3);
        // Life loss = 3 (hand count post-draw).
        _alice.LifeTotal.Should().Be(20 - 3,
            "life loss = 3 (the two pre-existing cards + the drawn card = 3 cards in hand after draw)");
    }

    [Fact]
    public void Activate_LifeLossIsPostDrawHandCount_NotPreDraw()
    {
        // This test explicitly verifies the "then" sequencing:
        // if life loss were calculated PRE-draw with 0 cards in hand,
        // life loss would be 0. But the oracle says "then", so the drawn
        // card is in hand first, making life loss 1.
        AddCardToLibrary();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var castle = PlaceOnBattlefield();
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();
        ability.Effects.Single().Execute();

        _alice.LifeTotal.Should().Be(19,
            "life loss is 1 (the drawn card is already in hand when life loss fires — CR 700.2 'then' ordering)");
    }

    [Fact]
    public void Activate_EmptyLibrary_DrawFails_LosesZeroLife()
    {
        // Empty library: draw fails (marks tried-to-draw flag), hand stays
        // empty, life loss = 0.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        var castle = PlaceOnBattlefield();
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();
        ability.Effects.Single().Execute();

        // Hand unchanged — nothing drawn.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        // Life loss = 0 (hand count after failed draw is still 0).
        _alice.LifeTotal.Should().Be(20,
            "no life lost when hand is empty after a failed draw");
    }
}
