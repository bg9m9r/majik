using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StromkirkOccultistFactory"/> (Eldritch Moon,
/// {2}{R}).
///
/// Creature — Vampire Horror 3/2. Oracle text (verified against Scryfall
/// 2026-06-14):
///   "Trample
///    Whenever this creature deals combat damage to a player, exile the top
///    card of your library. Until end of turn, you may play that card.
///    Madness {1}{R}"
///
/// Covers the card's UNIQUE non-madness behaviour:
/// - Identity (name, type, mana cost, P/T, Vampire + Horror subtypes, Trample).
/// - Combat-damage-to-a-player trigger: exiles the top card of the
///   controller's library and grants a runtime exile-cast (impulse) on it.
/// - The trigger does NOT fire on combat damage to a creature.
/// - NamedCardFactory dispatch.
///
/// Madness {1}{R} is intrinsic (CR 702.35) so it is intentionally not tested.
/// </summary>
[Trait("Color", "R")]
public class StromkirkOccultistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void StromkirkOccultist_Identity_WithTrample()
    {
        var c = StromkirkOccultistFactory.Create(_alice);

        c.Name.Should().Be("Stromkirk Occultist");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue("Stromkirk Occultist is a Vampire");
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue("Stromkirk Occultist is a Horror");
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        CombatAbilities.HasTrample(c).Should().BeTrue("Stromkirk Occultist has Trample");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StromkirkOccultist()
    {
        var c = NamedCardFactory.Create("Stromkirk Occultist", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Stromkirk Occultist");
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
    }

    [Fact]
    public void CombatDamageToPlayer_ExilesTopOfControllerLibrary_AndGrantsImpulse()
    {
        var c = StromkirkOccultistFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var topCard = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(c, _bob, 3);

        trigger.IsTriggered(dmgEvent).Should().BeTrue(
            "dealing combat damage to a player matches the trigger");

        foreach (var e in trigger.Effects) e.Execute();

        topCard.Zone.Should().Be(ZoneType.Exile, "the top card of YOUR library is exiled");
        _alice.Zones.Exile.GetCards().Should().Contain(topCard);
        topCard.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the controller may play the exiled card");
        topCard.RuntimeExileCastCost.Should().NotBeNull(
            "the impulse grant carries the card's printed mana cost");
    }

    [Fact]
    public void CombatDamageToCreature_DoesNotFire()
    {
        var c = StromkirkOccultistFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var blocker = new Creature("Blocker", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(c, (ICard)blocker, 3);

        trigger.IsTriggered(dmgEvent).Should().BeFalse(
            "combat damage to a creature does not match — TargetPlayer is null");
    }

    [Fact]
    public void CombatDamageToPlayer_EmptyLibrary_NoOp()
    {
        var c = StromkirkOccultistFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(c, _bob, 3);

        trigger.IsTriggered(dmgEvent).Should().BeTrue();

        // Empty library — resolving the impulse is a clean no-op (CR 120.3).
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }
}
