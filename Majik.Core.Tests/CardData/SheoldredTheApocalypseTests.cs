using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SheoldredTheApocalypseFactory"/> (Dominaria
/// United, {2}{B}{B}).
///
/// Covers:
/// - Identity (name, type Creature, supertype Legendary, subtypes Phyrexian
///   + Praetor, P/T 4/5, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Deathtouch keyword marker (CR 702.2) — directly on the abilities
///   collection and via CombatAbilities.
/// - Draw trigger (CR 603.1): controller's draw → +2 life + each opponent
///   -2 life.
/// - Multiple controller draws stack — the trigger fires once per drawn
///   card (CR 603.2c).
/// - Opponent's draw does NOT trigger Sheoldred ("Whenever you draw a card"
///   filters to the controller only).
/// </summary>
public class SheoldredTheApocalypseTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Sheoldred_Identity()
    {
        var c = SheoldredTheApocalypseFactory.Create(_alice);

        c.Name.Should().Be("Sheoldred, the Apocalypse");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Sheoldred is Legendary");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Praetor).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sheoldred_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sheoldred, the Apocalypse", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sheoldred, the Apocalypse");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Praetor).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Deathtouch (CR 702.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void Sheoldred_HasDeathtouchKeyword()
    {
        var c = SheoldredTheApocalypseFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Deathtouch",
            "CR 702.2 — Deathtouch is printed on Sheoldred, the Apocalypse");

        CombatAbilities.HasDeathtouch(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Draw trigger (CR 603.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Sheoldred_ControllerDraws_GainsTwoLifeAndOpponentLosesTwo()
    {
        var sheoldred = SheoldredTheApocalypseFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        var trigger = sheoldred.Abilities.OfType<TriggeredAbility>().Single();

        // The trigger fires on Alice (controller) drawing a card.
        var draw = new CardDrawnEvent(new Card("Swamp", ""), _alice);
        trigger.IsTriggered(draw).Should().BeTrue(
            "Whenever you (controller) draw a card — CR 603.1");

        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(22, "Alice gains 2 life on each of her draws");
        _bob.LifeTotal.Should().Be(18, "Bob (the opponent) loses 2 life on each of Alice's draws");
    }

    [Fact]
    public void Sheoldred_MultipleControllerDraws_StackTwoEach()
    {
        // CR 603.2c — the triggered ability fires once per drawn card. Three
        // draws ⇒ three resolutions ⇒ +6 life for Alice / -6 life for Bob.
        var sheoldred = SheoldredTheApocalypseFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        var trigger = sheoldred.Abilities.OfType<TriggeredAbility>().Single();

        for (var i = 0; i < 3; i++)
        {
            foreach (var e in trigger.Effects) e.Execute();
        }

        _alice.LifeTotal.Should().Be(26, "three draws ⇒ +6 life");
        _bob.LifeTotal.Should().Be(14, "three draws ⇒ -6 life for the opponent");
    }

    [Fact]
    public void Sheoldred_OpponentDraws_DoesNotTrigger()
    {
        // "Whenever you draw a card" — CR 603.1, the trigger condition is
        // scoped to the controller of the ability. Opponent draws must not
        // satisfy IsTriggered.
        var sheoldred = SheoldredTheApocalypseFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        var trigger = sheoldred.Abilities.OfType<TriggeredAbility>().Single();

        var bobDraws = new CardDrawnEvent(new Card("Mountain", ""), _bob);
        trigger.IsTriggered(bobDraws).Should().BeFalse(
            "Sheoldred's draw trigger fires for the controller's draws only — " +
            "opponent draws must not satisfy the condition");

        // Life totals unchanged.
        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Sheoldred_DrawTrigger_OnlyActiveOnBattlefield()
    {
        var sheoldred = SheoldredTheApocalypseFactory.Create(_alice);

        var trigger = sheoldred.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand,
            "the draw trigger is a battlefield-only ability — CR 113.6");
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
