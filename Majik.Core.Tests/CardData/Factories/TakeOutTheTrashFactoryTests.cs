using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TakeOutTheTrashFactory"/> (Bloomburrow, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Take Out the Trash deals 3 damage to target creature or planeswalker.
///    If you control a Raccoon, you may discard a card. If you do, draw a
///    card."
///
/// Damage mode mirrors <see cref="RipApartFactory"/>'s creature/planeswalker
/// burn; the Raccoon-gated optional discard-then-draw looter mirrors
/// <see cref="FireProphecyFactory"/>'s rummage rider (discard instead of
/// bottom, gated on controlling a Raccoon — CR 205.3m).
/// </summary>
[Trait("Color", "R")]
public class TakeOutTheTrashFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams Chosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);

    private static void RunResolve(SpellDefinition def, params object[] targets)
    {
        foreach (var e in def.EffectFactory(Chosen(targets))) e.Execute();
    }

    private Creature Raccoon()
    {
        var raccoon = new Creature(
            "Valley Mightcaller", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Raccoon })
        { Owner = _alice, Controller = _alice };
        raccoon.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(raccoon);
        return raccoon;
    }

    private ICard AddHandCard(string name = "Mountain")
    {
        var card = new Land(name) { Owner = _alice, Controller = _alice };
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        return card;
    }

    private ICard AddLibraryCard(string name = "Forest")
    {
        var card = new Land(name) { Owner = _alice, Controller = _alice };
        card.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card);
        return card;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TakeOutTheTrash_Create_HasInstantShape_Red_OneR()
    {
        var card = TakeOutTheTrashFactory.Create(_alice);

        card.Name.Should().Be("Take Out the Trash");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().ContainSingle().Which.Should().Be(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{R} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Damage — target creature or planeswalker.
    // -----------------------------------------------------------------------

    [Fact]
    public void Damage_DealsThreeToCreature()
    {
        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var def = TakeOutTheTrashFactory.BuildSpellDefinition(_alice, o => o);
        RunResolve(def, target);

        target.Damage.Should().Be(3, because: "3 damage to target creature");
    }

    [Fact]
    public void Damage_RemovesThreeLoyaltyFromPlaneswalker()
    {
        var pw = new Planeswalker("Test Walker", "{2}{R}", startingLoyalty: 5)
        { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = TakeOutTheTrashFactory.BuildSpellDefinition(_alice, o => o);
        RunResolve(def, pw);

        pw.Loyalty.Should().Be(2,
            because: "3 damage to a planeswalker removes 3 loyalty (CR 306.7)");
    }

    [Fact]
    public void Damage_NoOp_OnPlayerTarget()
    {
        // CR 608.2b — a player is not a legal target; no damage redirected.
        var def = TakeOutTheTrashFactory.BuildSpellDefinition(_alice, o => o);
        RunResolve(def, _bob);

        _bob.LifeTotal.Should().Be(20,
            because: "damage is dealt only to creatures/planeswalkers, not players");
    }

    // -----------------------------------------------------------------------
    // Raccoon looter rider — "If you control a Raccoon, you may discard a
    // card. If you do, draw a card."
    // -----------------------------------------------------------------------

    [Fact]
    public void ControlsRaccoon_TrueOnlyWhenControllingARaccoon()
    {
        TakeOutTheTrashFactory.ControlsRaccoon(_alice).Should().BeFalse(
            because: "no Raccoon on the battlefield yet");

        Raccoon();

        TakeOutTheTrashFactory.ControlsRaccoon(_alice).Should().BeTrue(
            because: "Valley Mightcaller is a Raccoon under Alice's control (CR 205.3m)");
    }

    [Fact]
    public void Rider_Declined_ByDefault_NoLootEvenWithRaccoon()
    {
        Raccoon();
        var handCard = AddHandCard();
        AddLibraryCard();

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        // v1 default: mayDiscard == null → decline. Only the damage happens.
        var def = TakeOutTheTrashFactory.BuildSpellDefinition(_alice, o => o);
        RunResolve(def, target);

        target.Damage.Should().Be(3);
        _alice.Zones.Hand.GetCards().Should().Contain(handCard,
            because: "the 'you may' discard is declined by default (no loot)");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Rider_Fires_WhenRaccoonControlled_AndDiscardChosen()
    {
        Raccoon();
        var toDiscard = AddHandCard("Mountain");
        var libraryTop = AddLibraryCard("Forest");

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var def = TakeOutTheTrashFactory.BuildSpellDefinition(
            _alice, o => o, mayDiscard: () => true);
        RunResolve(def, target);

        target.Damage.Should().Be(3);
        toDiscard.Zone.Should().Be(ZoneType.Graveyard,
            because: "the chosen hand card is discarded (CR 701.8)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(toDiscard);
        // "If you do, draw a card." — the library top is drawn into hand.
        libraryTop.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(libraryTop);
    }

    [Fact]
    public void Rider_Skipped_WhenNoRaccoon_EvenIfMayDiscardTrue()
    {
        // No Raccoon on the battlefield → the whole rider is skipped (CR 608.2),
        // so the controller is never even offered the discard.
        var handCard = AddHandCard();
        AddLibraryCard();

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var def = TakeOutTheTrashFactory.BuildSpellDefinition(
            _alice, o => o, mayDiscard: () => true);
        RunResolve(def, target);

        target.Damage.Should().Be(3);
        handCard.Zone.Should().Be(ZoneType.Hand,
            because: "without a Raccoon the looter rider never fires");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
