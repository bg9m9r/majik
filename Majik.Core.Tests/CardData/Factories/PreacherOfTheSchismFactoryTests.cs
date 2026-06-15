using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PreacherOfTheSchismFactory"/>.
///
/// Preacher of the Schism (The Lost Caverns of Ixalan, {2}{B}). Creature —
/// Vampire Cleric 2/4. Oracle text (verified against Scryfall):
///   "Deathtouch
///    Whenever this creature attacks the player with the most life or tied for
///    most life, create a 1/1 white Vampire creature token with lifelink.
///    Whenever this creature attacks while you have the most life or are tied
///    for most life, you draw a card and you lose 1 life."
///
/// Covers the card's UNIQUE behaviour:
/// - Deathtouch keyword marker (CR 702.2) + combat helper.
/// - Attack-trigger #1 (defending player has the most life → mint a 1/1 white
///   Vampire token with lifelink); negative case (defender does NOT have the
///   most life → no token).
/// - Attack-trigger #2 (you have the most life → draw + lose 1 life); negative
///   case (you do NOT have the most life → no draw / no life change).
/// Plus a single _Identity assert for the non-vanilla stats.
/// </summary>
[Trait("Color", "B")]
public class PreacherOfTheSchismFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Game.GameContext GameWith(Player self, params Player[] all) =>
        new(
            self: self,
            allPlayers: all,
            activePlayer: self,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));

    /// <summary>Fire the trigger whose condition matches the supplied attack
    /// event (capturing the defender) then resolve its effect against a live
    /// GameContext.</summary>
    private void Attack(
        Creature card,
        TriggeredAbility trigger,
        object defendingPlayer,
        Player[] allPlayers)
    {
        // CR 508.1f — declare the attack so the condition captures the defender.
        var attackEvent = new CreatureAttacksEvent(card, defendingPlayer);
        trigger.Condition.Matches(attackEvent, trigger).Should().BeTrue();

        var controller = card.Controller ?? _alice;
        var ctx = ResolutionContext.For(
            controller, agent: null, GameWith(controller, allPlayers), chosenTargets: null);
        foreach (var effect in trigger.Effects)
        {
            effect.ExecuteAsync(ctx).AsTask().GetAwaiter().GetResult();
        }
    }

    private static TriggeredAbility TokenTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition.EventType == typeof(CreatureAttacksEvent)
                && t.Effects.Any(e => e.Description.Contains("Vampire token")));

    private static TriggeredAbility DrawTrigger(Creature card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Effects.Any(e => e.Description.Contains("draw a card")));

    [Fact]
    public void PreacherOfTheSchism_Identity()
    {
        var c = PreacherOfTheSchismFactory.Create(_alice);

        c.Name.Should().Be("Preacher of the Schism");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(4);
        c.ManaCost.Should().Be("{2}{B}");
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();

        // CR 202.2c — mono-black from the {B} pip.
        var colors = CardColors.GetColors(c);
        colors.Should().ContainSingle().Which.Should().Be(ManaColor.Black);
    }

    [Fact]
    public void PreacherOfTheSchism_HasDeathtouch()
    {
        var c = PreacherOfTheSchismFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Deathtouch").Should().BeTrue();
        // CR 702.2 — combat helper reports it.
        CombatAbilities.HasDeathtouch(c).Should().BeTrue();
    }

    [Fact]
    public void AttackingPlayerWithMostLife_MintsWhiteVampireTokenWithLifelink()
    {
        var c = PreacherOfTheSchismFactory.Create(_alice);
        _bob.LoseLife(3); // Alice 20, Bob 17 → defender (Bob)? not most.

        // Defender = Alice's opponent Bob. Make Bob the most-life player.
        _alice.LoseLife(5); // Alice 15, Bob 17 → Bob has the most life.

        var before = _bob.Zones.Battlefield.GetCards().OfType<Creature>().Count();
        Attack(c, TokenTrigger(c), defendingPlayer: _bob, allPlayers: new[] { _alice, _bob });

        // Token enters under Preacher's controller (Alice), CR 111.4.
        var token = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .FirstOrDefault(t => t.Name == "Vampire");
        token.Should().NotBeNull("defending player Bob has the most life");
        token!.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue();
        CardColors.GetColors(token).Should().ContainSingle().Which.Should().Be(ManaColor.White);
        _bob.Zones.Battlefield.GetCards().OfType<Creature>().Count().Should().Be(before);
    }

    [Fact]
    public void AttackingPlayerWithoutMostLife_MintsNoToken()
    {
        var c = PreacherOfTheSchismFactory.Create(_alice);
        _bob.LoseLife(8); // Alice 20, Bob 12 → Bob does NOT have the most life.

        Attack(c, TokenTrigger(c), defendingPlayer: _bob, allPlayers: new[] { _alice, _bob });

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Any(t => t.Name == "Vampire").Should().BeFalse(
                "defending player Bob does not have the most life");
    }

    [Fact]
    public void AttackingWhileYouHaveMostLife_DrawsAndLosesOneLife()
    {
        var c = PreacherOfTheSchismFactory.Create(_alice);
        _bob.LoseLife(5); // Alice 20, Bob 15 → Alice (controller) has the most.

        var top = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        _alice.Zones.Library.AddCard(top);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        Attack(c, DrawTrigger(c), defendingPlayer: _bob, allPlayers: new[] { _alice, _bob });

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1, "drew a card");
        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.LifeTotal.Should().Be(19, "started at 20 and lost 1 life");
    }

    [Fact]
    public void AttackingWhileNotMostLife_NoDrawNoLifeLoss()
    {
        var c = PreacherOfTheSchismFactory.Create(_alice);
        _alice.LoseLife(5); // Alice 15, Bob 20 → Alice does NOT have the most.

        var top = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        _alice.Zones.Library.AddCard(top);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        Attack(c, DrawTrigger(c), defendingPlayer: _bob, allPlayers: new[] { _alice, _bob });

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore, "no draw");
        _alice.LifeTotal.Should().Be(15, "no life lost");
    }
}
