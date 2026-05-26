using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RenegadeRallierFactory"/> (Aether Revolt,
/// {1}{G}{W}, Creature — Human Warrior 3/2).
///
/// Covers:
/// - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch hands back the correct shape.
/// - ETB triggered ability is attached and gated by an intervening-if
///   that consults <see cref="TurnState.RevoltActive"/> (CR 603.4 /
///   CR 702.104a).
/// - Intervening-if returns FALSE without a TurnState (revolt inactive).
/// - Intervening-if returns FALSE when revolt is not active (no permanent
///   left the battlefield this turn).
/// - Intervening-if returns TRUE once a permanent the controller
///   controlled has left the battlefield this turn.
/// - Resolve effect reanimates the first permanent card with mana value
///   ≤ 2 from controller's graveyard.
/// - Resolve effect is a no-op when no eligible candidate exists (mv 3
///   creature in graveyard is excluded).
/// - Resolve effect is a no-op for instant / sorcery cards (non-permanent
///   cards are filtered out).
/// </summary>
public class RenegadeRallierTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RenegadeRallier_Identity_HumanWarrior_3_2_AtCost1GW()
    {
        var c = RenegadeRallierFactory.Create(_alice);

        c.Name.Should().Be("Renegade Rallier");
        c.ManaCost.Should().Be("{1}{G}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RenegadeRallier_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Renegade Rallier", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Renegade Rallier");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}{W}");
    }

    // -----------------------------------------------------------------------
    // Intervening-if revolt gate (CR 603.4 / CR 702.104a)
    // -----------------------------------------------------------------------

    [Fact]
    public void RenegadeRallier_InterveningIf_FalseWithoutTurnState()
    {
        var c = RenegadeRallierFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        etb.InterveningIf.Should().NotBeNull(
            "the revolt gate is wired as an intervening-if (CR 603.4)");
        etb.InterveningIf!().Should().BeFalse(
            "without a TurnState resolver revolt is treated as inactive");
    }

    [Fact]
    public void RenegadeRallier_InterveningIf_FalseWhenNoPermanentLeftThisTurn()
    {
        var turnState = new TurnState();
        var c = RenegadeRallierFactory.Create(_alice, () => turnState, zoneService: null, triggers: null);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        etb.InterveningIf!().Should().BeFalse(
            "no permanent has left the battlefield this turn — revolt inactive");
    }

    [Fact]
    public void RenegadeRallier_InterveningIf_TrueWhenAPermanentLeftThisTurn()
    {
        var turnState = new TurnState();
        turnState.RecordPermanentLeftBattlefield(_alice);

        var c = RenegadeRallierFactory.Create(_alice, () => turnState, zoneService: null, triggers: null);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        etb.InterveningIf!().Should().BeTrue(
            "a permanent the controller controlled left this turn — revolt active (CR 702.104a)");
    }

    [Fact]
    public void RenegadeRallier_InterveningIf_OnlyCountsControllerPermanents()
    {
        var turnState = new TurnState();
        var bob = new Player("Bob", 20);
        // A permanent BOB controlled died — revolt active for Bob, NOT for Alice.
        turnState.RecordPermanentLeftBattlefield(bob);

        var c = RenegadeRallierFactory.Create(_alice, () => turnState, zoneService: null, triggers: null);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        etb.InterveningIf!().Should().BeFalse(
            "revolt is per-controller — a permanent leaving an opponent's battlefield doesn't enable it (CR 702.104a)");
    }

    // -----------------------------------------------------------------------
    // Resolve effect — reanimate permanent card with mv ≤ 2
    // -----------------------------------------------------------------------

    [Fact]
    public void RenegadeRallier_Resolve_ReanimatesPermanentCardWithManaValueAtMostTwo()
    {
        var alice = new Player("Alice", 20);

        // mv-2 creature is eligible (creature is a permanent type).
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var c = RenegadeRallierFactory.Create(alice);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Grizzly Bears has mv 2 — eligible under the ≤ 2 cap");
        alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
        alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        bear.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the activator's control (CR 110.2)");
    }

    [Fact]
    public void RenegadeRallier_Resolve_NoOp_WhenAllCandidatesExceedManaValueTwo()
    {
        var alice = new Player("Alice", 20);

        // mv-3 creature is NOT eligible (exceeds ≤ 2 cap).
        var elf = new Creature("Wood Elves", "2G", 1, 1);
        elf.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(elf);
        elf.SetZone(ZoneType.Graveyard);

        var c = RenegadeRallierFactory.Create(alice);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "no eligible permanent card in graveyard → no-op (CR 117.x)");
        elf.Zone.Should().Be(ZoneType.Graveyard,
            "Wood Elves has mv 3 — outside the ≤ 2 cap, must remain in graveyard");
        alice.Zones.Battlefield.GetCards().Should().NotContain(elf);
    }

    [Fact]
    public void RenegadeRallier_Resolve_IgnoresInstantAndSorceryCards()
    {
        var alice = new Player("Alice", 20);

        // An mv-1 instant is NOT a permanent card (CR 110.4) — must be skipped.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var c = RenegadeRallierFactory.Create(alice);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        foreach (var effect in etb.Effects) effect.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "instant cards are not permanent cards (CR 110.4) — outside the reanimation filter");
        alice.Zones.Battlefield.GetCards().Should().NotContain(bolt);
    }
}
