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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TrespassersCurseFactory"/>.
///
/// Card: Trespasser's Curse — Enchantment — Aura Curse {1}{B}
/// (Shadows over Innistrad).
///   "Enchant player.
///    Whenever a creature enters under enchanted player's control, that
///    player loses 1 life and you gain 1 life."
///
/// Covers:
///   - Identity (name, type, mana cost, Aura + Curse subtypes).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Side-channel enchanted-player storage.
///   - Trigger fires on creature ETB under enchanted player's control;
///     does NOT fire for creatures controlled by anyone else, NOR for
///     non-creature ETBs, NOR when no player is enchanted.
///   - Resolution: enchanted player loses 1, controller gains 1.
/// </summary>
[Trait("Color", "B")]
public class TrespassersCurseFactoryTests
{
    private readonly Player _alice = new("Alice", 20); // curse controller
    private readonly Player _bob = new("Bob", 20);     // enchanted player
    private readonly Player _carol = new("Carol", 20); // unrelated

    [Fact]
    public void TrespassersCurse_Identity()
    {
        var c = TrespassersCurseFactory.Create(_alice);

        c.Name.Should().Be("Trespasser's Curse");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.HasSubtype(CardSubtype.Curse).Should().BeTrue();
        c.IsAura.Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesTrespassersCurse()
    {
        var card = NamedCardFactory.Create("Trespasser's Curse", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Trespasser's Curse");
        card.HasSubtype(CardSubtype.Curse).Should().BeTrue();
    }

    [Fact]
    public void EnchantedPlayer_Roundtrips()
    {
        var curse = TrespassersCurseFactory.Create(_alice);
        TrespassersCurseFactory.GetEnchantedPlayer(curse).Should().BeNull();

        TrespassersCurseFactory.SetEnchantedPlayer(curse, _bob);
        TrespassersCurseFactory.GetEnchantedPlayer(curse).Should().BeSameAs(_bob);
    }

    [Fact]
    public void Trigger_FiresOnCreatureEnteringUnderEnchantedPlayer()
    {
        var curse = TrespassersCurseFactory.Create(_alice);
        curse.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(curse);
        TrespassersCurseFactory.SetEnchantedPlayer(curse, _bob);

        var trigger = curse.Abilities.OfType<TriggeredAbility>().First();

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);

        var entersEvent = new CardMovedEvent(bobBear, ZoneType.Hand, ZoneType.Battlefield);
        trigger.Condition.Matches(entersEvent, trigger).Should().BeTrue();
    }

    [Fact]
    public void Trigger_DoesNotFire_WhenCreatureEntersUnderDifferentController()
    {
        var curse = TrespassersCurseFactory.Create(_alice);
        curse.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(curse);
        TrespassersCurseFactory.SetEnchantedPlayer(curse, _bob);

        var trigger = curse.Abilities.OfType<TriggeredAbility>().First();

        // Carol's creature — not under enchanted player's control.
        var carolBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        carolBear.SetOwner(_carol);
        carolBear.SetController(_carol);

        var entersEvent = new CardMovedEvent(carolBear, ZoneType.Hand, ZoneType.Battlefield);
        trigger.Condition.Matches(entersEvent, trigger).Should().BeFalse();

        // Even the curse-controller's own creature — does NOT fire (curse
        // enchants Bob, not Alice).
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);
        var aliceEvent = new CardMovedEvent(aliceBear, ZoneType.Hand, ZoneType.Battlefield);
        trigger.Condition.Matches(aliceEvent, trigger).Should().BeFalse();
    }

    [Fact]
    public void Trigger_DoesNotFire_ForNonCreatureETB()
    {
        var curse = TrespassersCurseFactory.Create(_alice);
        curse.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(curse);
        TrespassersCurseFactory.SetEnchantedPlayer(curse, _bob);

        var trigger = curse.Abilities.OfType<TriggeredAbility>().First();

        var bobEnchant = new Enchantment("Some Enchantment", "{B}");
        bobEnchant.SetOwner(_bob);
        bobEnchant.SetController(_bob);

        var entersEvent = new CardMovedEvent(bobEnchant, ZoneType.Hand, ZoneType.Battlefield);
        trigger.Condition.Matches(entersEvent, trigger).Should().BeFalse();
    }

    [Fact]
    public void Trigger_DoesNotFire_WhenNoPlayerIsEnchanted()
    {
        // Defensive: factory built without SetEnchantedPlayer → trigger is
        // dormant rather than firing on every creature ETB.
        var curse = TrespassersCurseFactory.Create(_alice);
        curse.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(curse);

        var trigger = curse.Abilities.OfType<TriggeredAbility>().First();

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);

        var entersEvent = new CardMovedEvent(bobBear, ZoneType.Hand, ZoneType.Battlefield);
        trigger.Condition.Matches(entersEvent, trigger).Should().BeFalse();
    }

    [Fact]
    public void Trigger_OnResolve_DrainsEnchantedPlayer_AndGainsForController()
    {
        var curse = TrespassersCurseFactory.Create(_alice);
        curse.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(curse);
        TrespassersCurseFactory.SetEnchantedPlayer(curse, _bob);

        var trigger = curse.Abilities.OfType<TriggeredAbility>().First();

        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void Trigger_OnResolve_NoOp_WhenNoEnchantedPlayer()
    {
        var curse = TrespassersCurseFactory.Create(_alice);
        curse.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(curse);

        var trigger = curse.Abilities.OfType<TriggeredAbility>().First();

        var act = () =>
        {
            foreach (var effect in trigger.Effects) effect.Execute();
        };
        act.Should().NotThrow();

        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Trigger_OnResolve_NoOp_OnLifeSwingForLostPlayers()
    {
        // Defensive: if enchanted player has already lost (life 0), drain
        // is skipped rather than throwing.
        var curse = TrespassersCurseFactory.Create(_alice);
        curse.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(curse);
        TrespassersCurseFactory.SetEnchantedPlayer(curse, _bob);

        _bob.LoseLife(20);
        _bob.HasLost.Should().BeTrue();

        var trigger = curse.Abilities.OfType<TriggeredAbility>().First();
        var act = () =>
        {
            foreach (var effect in trigger.Effects) effect.Execute();
        };
        act.Should().NotThrow();
    }
}
