using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BastionOfRemembranceFactory"/> (Ikoria:
/// Lair of Behemoths, {2}{B}).
///
/// Enchantment. Oracle text (Scryfall, verified):
///   "When this enchantment enters, create a 1/1 white Human Soldier
///    creature token.
///    Whenever a creature you control dies, each opponent loses 1 life
///    and you gain 1 life."
///
/// Covers:
/// - Identity (Enchantment, {2}{B}, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB token trigger (CR 603.6e) fires when the enchantment itself
///   enters the battlefield, and produces a 1/1 white Human Soldier.
/// - Aristocrat death trigger (CR 603.1 + CR 700.4) fires Battlefield →
///   Graveyard for a creature you control.
/// - Death trigger does NOT fire for an opponent's creature.
/// - Death trigger does NOT fire on non-creature permanents.
/// - Death trigger does NOT fire on non-graveyard destinations.
/// - Drain side: each opponent loses 1 + controller gains 1 (with
///   resolver); lifegain still fires without a resolver.
/// </summary>
public class BastionOfRemembranceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BastionOfRemembrance_Identity()
    {
        var c = BastionOfRemembranceFactory.Create(_alice);

        c.Name.Should().Be("Bastion of Remembrance");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Bastion has an ETB token trigger plus a 'creature you control dies' drain trigger");
    }

    [Fact]
    public void BastionOfRemembrance_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Bastion of Remembrance", _alice);

        c.Should().BeOfType<Enchantment>();
        c.Name.Should().Be("Bastion of Remembrance");
    }

    [Fact]
    public void BastionOfRemembrance_EntersBattlefield_TriggersTokenCreation()
    {
        var bastion = BastionOfRemembranceFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(bastion);
        bastion.SetZone(ZoneType.Battlefield);

        // CR 603.6e — self-ETB trigger fires when this enchantment enters.
        var etbEvent = new CardMovedEvent(bastion, ZoneType.Hand, ZoneType.Battlefield);

        var etbTrigger = bastion.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(etbEvent));

        foreach (var e in etbTrigger.Effects) e.Execute();

        var token = _alice.Zones.Battlefield.GetCards().OfType<Creature>().Single();
        token.IsToken.Should().BeTrue();
        token.Name.Should().Be("Human Soldier");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.Subtypes.Should().Contain(CardSubtype.Human);
        token.Subtypes.Should().Contain(CardSubtype.Soldier);
        CardColors.GetColors(token).Should().Contain(ManaColor.White);
        token.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BastionOfRemembrance_OwnCreatureDies_DrainsAndGains()
    {
        var bastion = BastionOfRemembranceFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(bastion);
        bastion.SetZone(ZoneType.Battlefield);

        // Alice's own creature dies (CR 700.4 — Battlefield → Graveyard).
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = bastion.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        ContextResolve.Resolve(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life");
    }

    [Fact]
    public void BastionOfRemembrance_OpponentCreatureDies_DoesNotFire()
    {
        var bastion = BastionOfRemembranceFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(bastion);
        bastion.SetZone(ZoneType.Battlefield);

        var bobsBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);

        var diesEvent = new CardMovedEvent(bobsBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var drainTrigger = bastion.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(
                new CardMovedEvent(
                    NewControlledBear(), ZoneType.Battlefield, ZoneType.Graveyard)));

        drainTrigger.IsTriggered(diesEvent).Should().BeFalse(
            "CR 603.1 — 'a creature you control dies' filters out opponent-controlled deaths");
    }

    [Fact]
    public void BastionOfRemembrance_NonCreatureDies_DoesNotFire()
    {
        var bastion = BastionOfRemembranceFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(bastion);
        bastion.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Trinket", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var moveEvent = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var drainTrigger = bastion.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(
                new CardMovedEvent(
                    NewControlledBear(), ZoneType.Battlefield, ZoneType.Graveyard)));

        drainTrigger.IsTriggered(moveEvent).Should().BeFalse(
            "the drain trigger reads 'a creature you control' — an artifact dying is irrelevant");
    }

    [Fact]
    public void BastionOfRemembrance_NonGraveyardDestination_DoesNotFire()
    {
        var bastion = BastionOfRemembranceFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(bastion);
        bastion.SetZone(ZoneType.Battlefield);

        var bear = NewControlledBear();

        // Battlefield → Exile is NOT death (CR 700.4 — dying requires
        // graveyard as destination).
        var exileEvent = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Exile);

        var drainTrigger = bastion.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(
                new CardMovedEvent(
                    NewControlledBear(), ZoneType.Battlefield, ZoneType.Graveyard)));

        drainTrigger.IsTriggered(exileEvent).Should().BeFalse(
            "CR 700.4 — 'dies' requires Battlefield → Graveyard; exile bypasses the trigger");
    }

    [Fact]
    public void BastionOfRemembrance_OwnCreatureDies_WithoutResolver_GainsLifeOnly()
    {
        // Single-arg dispatcher path — no opponentResolver wired. The
        // opponent-drain side silently no-ops but the controller's
        // lifegain still fires (CR 119.3 — two discrete life-change events).
        var bastion = BastionOfRemembranceFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(bastion);
        bastion.SetZone(ZoneType.Battlefield);

        var aliceBear = NewControlledBear();

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = bastion.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "lifegain side fires unconditionally on resolution");
        _bob.LifeTotal.Should().Be(20, "no opponentResolver ⇒ opponent-drain silently no-ops");
    }

    private Creature NewControlledBear()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        return bear;
    }
}
