using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SythisHarvestsHandFactory"/> and dispatcher
/// wiring.
///
/// Card text (Theros Beyond Death, {G}{W}):
///   "Constellation — Whenever an enchantment enters under your control,
///    you gain 1 life and draw a card."
/// </summary>
public class SythisHarvestsHandTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Sythis_Identity()
    {
        var c = SythisHarvestsHandFactory.Create(_alice);

        c.Name.Should().Be("Sythis, Harvest's Hand");
        c.ManaCost.Should().Be("{G}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue(
            "Sythis is a Legendary Enchantment Creature (CR 205.2a)");
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Nymph).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sythis_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sythis, Harvest's Hand", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sythis, Harvest's Hand");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Nymph).Should().BeTrue();
        ((Creature)c).Power.Should().Be(1);
        ((Creature)c).Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Constellation trigger — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void EnchantmentEntersUnderControl_GainsLifeAndDraws()
    {
        // Seed a top-of-library card so the draw has something to pull.
        var topOfDeck = new Card("Top Card", "");
        topOfDeck.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topOfDeck);
        topOfDeck.SetZone(ZoneType.Library);

        // An enchantment entering Alice's battlefield.
        var ench = new Enchantment("Plain Enchantment", "{2}");
        ench.SetOwner(_alice);
        ench.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ench);
        ench.SetZone(ZoneType.Battlefield);

        var startingLife = _alice.LifeTotal;

        var sythis = SythisHarvestsHandFactory.Create(_alice);
        var trigger = sythis.Abilities.OfType<TriggeredAbility>().Single();

        // Condition matches an enchantment-entering event for Alice.
        var movedEvent = new CardMovedEvent(
            card: ench, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(movedEvent, ability: null!).Should().BeTrue();

        // Fire the effect — controller should gain 1 life AND draw a card.
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(startingLife + 1,
            "constellation grants 1 life on each enchantment ETB (CR 119)");
        _alice.Zones.Hand.GetCards().Should().Contain(topOfDeck,
            "constellation draws the top of the controller's library (CR 121)");
        _alice.Zones.Library.GetCards().Should().NotContain(topOfDeck);
    }

    [Fact]
    public void AuraEnchantmentEntering_AlsoTriggersConstellation()
    {
        // Auras carry the Enchantment card type plus the Aura subtype
        // (CR 303.1) — constellation should fire for them too.
        var sythis = SythisHarvestsHandFactory.Create(_alice);
        var trigger = sythis.Abilities.OfType<TriggeredAbility>().Single();

        var aura = new Enchantment(
            "Test Aura", "{1}{W}",
            subtypes: new[] { CardSubtype.Aura });
        aura.SetOwner(_alice);
        aura.SetController(_alice);

        var e = new CardMovedEvent(
            card: aura, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(e, ability: null!).Should().BeTrue(
            "Auras carry CardType.Enchantment, so constellation fires for them too");
    }

    // -----------------------------------------------------------------------
    // Constellation trigger — condition negatives
    // -----------------------------------------------------------------------

    [Fact]
    public void NonEnchantmentEntering_DoesNotMatchTriggerCondition()
    {
        var sythis = SythisHarvestsHandFactory.Create(_alice);
        var trigger = sythis.Abilities.OfType<TriggeredAbility>().Single();

        // A creature entering — must not match (no Enchantment type).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        var bearEvent = new CardMovedEvent(
            card: bear, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(bearEvent, ability: null!).Should().BeFalse(
            "constellation triggers only on enchantments");

        // An artifact entering — must not match.
        var doodad = new Artifact("Random Doodad", "{2}");
        doodad.SetOwner(_alice);
        doodad.SetController(_alice);
        var doodadEvent = new CardMovedEvent(
            card: doodad, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(doodadEvent, ability: null!).Should().BeFalse(
            "constellation triggers only on enchantments, not bare artifacts");
    }

    [Fact]
    public void OpponentEnchantmentEntering_DoesNotMatchTriggerCondition()
    {
        var sythis = SythisHarvestsHandFactory.Create(_alice);
        var trigger = sythis.Abilities.OfType<TriggeredAbility>().Single();

        // Enchantment entering under BOB's control — must not match.
        var bobEnch = new Enchantment("Bob's Enchantment", "{2}");
        bobEnch.SetOwner(_bob);
        bobEnch.SetController(_bob);

        var e = new CardMovedEvent(
            card: bobEnch, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(e, ability: null!).Should().BeFalse(
            "constellation reads 'under YOUR control' — opponent enchantments don't qualify");
    }
}
