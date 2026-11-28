using System.Text.Json;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Event payloads for the player-facing log and animations: ids plus display
/// names, so a client needs no state lookup. CR 706 — fields that would reveal
/// hidden information are masked per viewer (surveil's library-bound cards, a
/// trigger whose source is in a hidden zone); CR 708.2 — a face-down
/// permanent has no name.
/// </summary>
public class EventPayloadLongTailTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private T Put<T>(T card, Player owner, ZoneType zone) where T : Card
    {
        card.SetOwner(owner); card.SetController(owner);
        owner.Zones.GetZone(zone).AddCard(card); card.SetZone(zone);
        return card;
    }

    private static Guid G(JsonElement p, string name) => p.GetProperty(name).GetGuid();
    private static string S(JsonElement p, string name) => p.GetProperty(name).GetString()!;

    [Fact]
    public void PermanentTapped_CarriesThePermanentItsControllerAndWhoTappedIt()
    {
        var bear = Put(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob, ZoneType.Battlefield);
        var p = EventPayloadBuilder.Build(new PermanentTappedEvent(bear, _alice));
        G(p, "permanentId").Should().Be(bear.InstanceId);
        S(p, "permanentName").Should().Be("Grizzly Bears");
        G(p, "controllerId").Should().Be(_bob.Id);
        G(p, "causedById").Should().Be(_alice.Id);
    }

    [Fact]
    public void TriggeredAbilityTriggered_CarriesSourceControllerAndText_MaskedWhenTheSourceIsHidden()
    {
        var source = Put(new Creature("Pinger", "{R}", 1, 1), _alice, ZoneType.Battlefield);
        var trigger = new TriggeredAbility(source, _alice, Triggers.OnEnterBattlefieldSelf(source),
            effects: new IEffect[] { new Effect("Deal 1 damage to any target.", () => { }) });
        var ev = new TriggeredAbilityTriggeredEvent(trigger, new GameStartedEvent());
        var p = EventPayloadBuilder.Build(ev, _bob);
        G(p, "abilityId").Should().Be(trigger.Id);
        G(p, "sourceId").Should().Be(source.InstanceId);
        S(p, "sourceName").Should().Be("Pinger");
        G(p, "controllerId").Should().Be(_alice.Id);
        S(p, "text").Should().Be("Deal 1 damage to any target.");

        // CR 706 — a trigger of a card in its owner's hand: an opponent learns
        // that something triggered, not what.
        var inHand = Put(new Creature("Hidden Thing", "{G}", 1, 1), _alice, ZoneType.Hand);
        var handTrigger = new TriggeredAbility(inHand, _alice, Triggers.OnEnterBattlefieldSelf(inHand),
            effects: new IEffect[] { new Effect("Secret.", () => { }) });
        var hev = new TriggeredAbilityTriggeredEvent(handTrigger, new GameStartedEvent());
        EventPayloadBuilder.RequiresPerViewerMasking(hev).Should().BeTrue();
        var masked = EventPayloadBuilder.Build(hev, _bob);
        masked.GetProperty("hidden").GetBoolean().Should().BeTrue();
        masked.TryGetProperty("sourceName", out _).Should().BeFalse();
        masked.TryGetProperty("text", out _).Should().BeFalse();
        G(masked, "controllerId").Should().Be(_alice.Id);
        S(EventPayloadBuilder.Build(hev, _alice), "sourceName").Should().Be("Hidden Thing");
    }

    private (Majik.Core.Combat.Combat combat, Creature attacker, Creature blocker) Combat()
    {
        var attacker = Put(new Creature("Goblin Guide", "{R}", 2, 2), _alice, ZoneType.Battlefield);
        var blocker = Put(new Creature("Wall", "{1}", 0, 4), _bob, ZoneType.Battlefield);
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        var a = new Attacker(attacker, _bob);
        combat.AddAttacker(a);
        combat.TransitionToDeclaringBlockers();
        a.AddBlocker(new Blocker(blocker, a));
        return (combat, attacker, blocker);
    }

    [Fact]
    public void AttackersDeclared_ListsEachAttackerAndWhatItAttacks()
    {
        var (combat, attacker, _) = Combat();
        var p = EventPayloadBuilder.Build(new AttackersDeclaredEvent(combat));
        G(p, "attackingPlayerId").Should().Be(_alice.Id);
        var a = p.GetProperty("attackers").EnumerateArray().Should().ContainSingle().Subject;
        G(a, "attackerId").Should().Be(attacker.InstanceId);
        S(a, "attackerName").Should().Be("Goblin Guide");
        G(a, "defendingPlayerId").Should().Be(_bob.Id);
    }

    [Fact]
    public void BlockersDeclared_ListsEachBlockerAndTheAttackerItBlocks()
    {
        var (combat, attacker, blocker) = Combat();
        var p = EventPayloadBuilder.Build(new BlockersDeclaredEvent(combat));
        G(p, "defendingPlayerId").Should().Be(_bob.Id);
        var b = p.GetProperty("blockers").EnumerateArray().Should().ContainSingle().Subject;
        G(b, "blockerId").Should().Be(blocker.InstanceId);
        S(b, "blockerName").Should().Be("Wall");
        G(b, "attackerId").Should().Be(attacker.InstanceId);
    }

    [Fact]
    public void CreatureAttacks_CarriesAttackerControllerAndDefender()
    {
        var attacker = Put(new Creature("Goblin Guide", "{R}", 2, 2), _alice, ZoneType.Battlefield);
        var p = EventPayloadBuilder.Build(new CreatureAttacksEvent(attacker, _bob));
        G(p, "attackerId").Should().Be(attacker.InstanceId);
        S(p, "attackerName").Should().Be("Goblin Guide");
        G(p, "controllerId").Should().Be(_alice.Id);
        G(p, "defendingPlayerId").Should().Be(_bob.Id);
        p.GetProperty("defendingPermanentId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void CreatureBlocks_CarriesBlockerAndAttacker()
    {
        var attacker = Put(new Creature("Goblin Guide", "{R}", 2, 2), _alice, ZoneType.Battlefield);
        var blocker = Put(new Creature("Wall", "{1}", 0, 4), _bob, ZoneType.Battlefield);
        var p = EventPayloadBuilder.Build(new CreatureBlocksEvent(blocker, attacker));
        G(p, "blockerId").Should().Be(blocker.InstanceId);
        S(p, "blockerName").Should().Be("Wall");
        G(p, "blockerControllerId").Should().Be(_bob.Id);
        G(p, "attackerId").Should().Be(attacker.InstanceId);
        S(p, "attackerName").Should().Be("Goblin Guide");
    }

    [Fact]
    public void Discarded_CarriesPlayerCardAndWhetherItWasACost()
    {
        var card = Put(new Creature("Bear", "{1}{G}", 2, 2), _alice, ZoneType.Graveyard);
        var p = EventPayloadBuilder.Build(new DiscardedEvent(_alice, card, wasCost: true));
        G(p, "playerId").Should().Be(_alice.Id);
        G(p, "cardId").Should().Be(card.InstanceId);
        S(p, "cardName").Should().Be("Bear");
        p.GetProperty("wasCost").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void PermanentSacrificed_CarriesPlayerCardAndTokenFlag()
    {
        var card = Put(new Creature("Bear", "{1}{G}", 2, 2), _alice, ZoneType.Graveyard);
        var p = EventPayloadBuilder.Build(new PermanentSacrificedEvent(card, _alice, wasToken: false));
        G(p, "playerId").Should().Be(_alice.Id);
        G(p, "cardId").Should().Be(card.InstanceId);
        S(p, "cardName").Should().Be("Bear");
        p.GetProperty("wasToken").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void SpellCountered_CarriesTheSpellItsCardAndBothControllers()
    {
        var bolt = Put(new Instant("Lightning Bolt", "{R}"), _alice, ZoneType.Graveyard);
        var spell = new Spell(bolt, _alice);
        var p = EventPayloadBuilder.Build(new SpellCounteredEvent(spell, _bob));
        G(p, "stackId").Should().Be(spell.Id);
        G(p, "cardId").Should().Be(bolt.InstanceId);
        S(p, "cardName").Should().Be("Lightning Bolt");
        G(p, "controllerId").Should().Be(_alice.Id);
        G(p, "counteringControllerId").Should().Be(_bob.Id);
    }

    [Fact]
    public void TriggeredAbilityCountered_CarriesTheAbilityItsSourceAndReason()
    {
        var source = Put(new Creature("Pinger", "{R}", 1, 1), _alice, ZoneType.Battlefield);
        var trigger = new TriggeredAbility(source, _alice, Triggers.OnEnterBattlefieldSelf(source));
        var p = EventPayloadBuilder.Build(new TriggeredAbilityCounteredEvent(trigger, "intervening if"));
        G(p, "stackId").Should().Be(trigger.Id);
        G(p, "sourceId").Should().Be(source.InstanceId);
        S(p, "sourceName").Should().Be("Pinger");
        G(p, "controllerId").Should().Be(_alice.Id);
        S(p, "reason").Should().Be("intervening if");
    }

    [Fact]
    public void ManaAbilityActivated_CarriesPlayerSourceAndMana()
    {
        var land = Put(new Land("Mountain"), _alice, ZoneType.Battlefield);
        var ability = new Majik.Core.Abilities.ManaAbility(land, _alice, ManaCost.Parse("R"));
        var p = EventPayloadBuilder.Build(new ManaAbilityActivatedEvent(ability, _alice, ManaCost.Parse("R")));
        G(p, "playerId").Should().Be(_alice.Id);
        G(p, "sourceId").Should().Be(land.InstanceId);
        S(p, "sourceName").Should().Be("Mountain");
        S(p, "mana").Should().Be("{R}", "C17: mana on the wire is in brace form (CR 107.4)");
    }

    [Fact]
    public void LifePaid_CarriesPlayerAmountAndCostFlag()
    {
        var p = EventPayloadBuilder.Build(new LifePaidEvent(_alice, 2, wasCost: true));
        G(p, "playerId").Should().Be(_alice.Id);
        p.GetProperty("amount").GetInt32().Should().Be(2);
        p.GetProperty("wasCost").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CostsPaid_CarriesTheStackObjectAndEachCostsDescription()
    {
        var bolt = Put(new Instant("Lightning Bolt", "{R}"), _alice, ZoneType.Stack);
        var spell = new Spell(bolt, _alice);
        var cost = new DiscardACardCost();
        var p = EventPayloadBuilder.Build(new CostsPaidEvent(spell, new ICost[] { cost }));
        G(p, "stackId").Should().Be(spell.Id);
        G(p, "controllerId").Should().Be(_alice.Id);
        p.GetProperty("costs").EnumerateArray().Select(e => e.GetString()).Should().Equal(cost.Description);
    }

    [Fact]
    public void TargetsChosen_CarriesTheStackObjectAndTargetRefs()
    {
        var bolt = Put(new Instant("Lightning Bolt", "{R}"), _alice, ZoneType.Stack);
        var spell = new Spell(bolt, _alice);
        var bear = Put(new Creature("Bear", "{1}{G}", 2, 2), _bob, ZoneType.Battlefield);
        var p = EventPayloadBuilder.Build(new TargetsChosenEvent(spell, new ITarget[] { Target.Permanent(bear), Target.Player(_bob) }));
        G(p, "stackId").Should().Be(spell.Id);
        G(p, "controllerId").Should().Be(_alice.Id);
        var targets = p.GetProperty("targets").EnumerateArray().ToList();
        targets.Select(t => (G(t, "id"), S(t, "kind"))).Should().Equal((bear.InstanceId, "Card"), (_bob.Id, "Player"));
    }

    [Fact]
    public void PriorityReceivedAndPassed_CarryThePlayer()
    {
        G(EventPayloadBuilder.Build(new PriorityReceivedEvent(_alice)), "playerId").Should().Be(_alice.Id);
        G(EventPayloadBuilder.Build(new PriorityPassedEvent(_bob)), "playerId").Should().Be(_bob.Id);
    }

    [Fact]
    public void CombatStartedAndEnded_CarryTheActivePlayer()
    {
        G(EventPayloadBuilder.Build(new CombatStartedEvent(_alice)), "activePlayerId").Should().Be(_alice.Id);
        var (combat, _, _) = Combat();
        G(EventPayloadBuilder.Build(new CombatEndedEvent(combat)), "activePlayerId").Should().Be(_alice.Id);
    }

    [Fact]
    public void LibraryShuffled_CarriesPlayerCountAndReason()
    {
        var p = EventPayloadBuilder.Build(new LibraryShuffledEvent(_bob, "fetch", 40));
        G(p, "playerId").Should().Be(_bob.Id);
        p.GetProperty("cardCount").GetInt32().Should().Be(40);
        S(p, "reason").Should().Be("fetch");
    }

    [Fact]
    public void Surveil_ShowsGraveyardBoundCardsToAll_AndLibraryBoundCardsOnlyToTheSurveilingPlayer()
    {
        var binned = Put(new Creature("Binned", "{1}", 1, 1), _alice, ZoneType.Graveyard);
        var kept = Put(new Creature("Kept", "{1}", 1, 1), _alice, ZoneType.Library);
        var ev = new SurveilEvent(_alice, 2, new ICard[] { binned, kept });
        EventPayloadBuilder.RequiresPerViewerMasking(ev).Should().BeTrue();

        var mine = EventPayloadBuilder.Build(ev, _alice);
        G(mine, "playerId").Should().Be(_alice.Id);
        mine.GetProperty("n").GetInt32().Should().Be(2);
        mine.GetProperty("toGraveyard").EnumerateArray().Select(c => S(c, "cardName")).Should().Equal("Binned");
        mine.GetProperty("toTopCount").GetInt32().Should().Be(1);
        mine.GetProperty("toTop").EnumerateArray().Select(c => G(c, "cardId")).Should().Equal(kept.InstanceId);

        // CR 706 — the opponent sees what went to the graveyard (public) and
        // how many stayed on top, never which.
        var theirs = EventPayloadBuilder.Build(ev, _bob);
        theirs.GetProperty("toGraveyard").EnumerateArray().Select(c => G(c, "cardId")).Should().Equal(binned.InstanceId);
        theirs.GetProperty("toTopCount").GetInt32().Should().Be(1);
        theirs.TryGetProperty("toTop", out _).Should().BeFalse();
    }

    [Fact]
    public void CardCycled_CarriesPlayerAndCard()
    {
        var card = Put(new Creature("Street Wraith", "{3}{B}{B}", 3, 4), _alice, ZoneType.Graveyard);
        var p = EventPayloadBuilder.Build(new CardCycledEvent(card, _alice));
        G(p, "playerId").Should().Be(_alice.Id);
        G(p, "cardId").Should().Be(card.InstanceId);
        S(p, "cardName").Should().Be("Street Wraith");
    }

    [Fact]
    public void ExtraTurnAdded_CarriesThePlayer()
    {
        G(EventPayloadBuilder.Build(new ExtraTurnAddedEvent(_bob)), "playerId").Should().Be(_bob.Id);
    }

    private IReadOnlyList<GameEvent> OneOfEach()
    {
        var bear = Put(new Creature("Bear", "{1}{G}", 2, 2), _bob, ZoneType.Battlefield);
        var bolt = Put(new Instant("Lightning Bolt", "{R}"), _alice, ZoneType.Stack);
        var spell = new Spell(bolt, _alice);
        var land = Put(new Land("Mountain"), _alice, ZoneType.Battlefield);
        var trigger = new TriggeredAbility(bear, _bob, Triggers.OnEnterBattlefieldSelf(bear));
        var (combat, attacker, blocker) = Combat();
        return new GameEvent[]
        {
            new PermanentTappedEvent(bear, _alice),
            new TriggeredAbilityTriggeredEvent(trigger, new GameStartedEvent()),
            new AttackersDeclaredEvent(combat), new BlockersDeclaredEvent(combat),
            new CreatureAttacksEvent(attacker, _bob), new CreatureBlocksEvent(blocker, attacker),
            new DiscardedEvent(_alice, bolt, false), new PermanentSacrificedEvent(bear, _bob, false),
            new SpellCounteredEvent(spell, _bob), new TriggeredAbilityCounteredEvent(trigger, "r"),
            new ManaAbilityActivatedEvent(new Majik.Core.Abilities.ManaAbility(land, _alice, ManaCost.Parse("R")), _alice, ManaCost.Parse("R")),
            new LifePaidEvent(_alice, 1, true), new CostsPaidEvent(spell, new ICost[] { new DiscardACardCost() }),
            new TargetsChosenEvent(spell, new ITarget[] { Target.Permanent(bear) }),
            new PriorityReceivedEvent(_alice), new PriorityPassedEvent(_alice),
            new CombatStartedEvent(_alice), new CombatEndedEvent(combat),
            new LibraryShuffledEvent(_alice, "r", 3), new SurveilEvent(_alice, 1, new ICard[] { bear }),
            new CardCycledEvent(bolt, _alice), new ExtraTurnAddedEvent(_alice),
        };
    }

    [Fact]
    public void BuildingThePayloads_DoesNotTickTheLogicalClock()
    {
        var clock = new Majik.Core.Game.LogicalClock();
        using var _ = Majik.Core.Game.LogicalClockScope.Push(clock);
        var events = OneOfEach();
        var before = clock.NextOrder();
        foreach (var e in events)
        {
            EventPayloadBuilder.Build(e);
            EventPayloadBuilder.Build(e, _alice);
            EventPayloadBuilder.Build(e, _bob);
            EventPayloadBuilder.RequiresPerViewerMasking(e);
        }
        clock.NextOrder().Should().Be(before + 1, "payload building must not consume logical-clock ticks");
    }

    [Fact]
    public void AFaceDownPermanent_IsNotNamed()
    {
        // CR 708.2 — a face-down permanent has no name.
        var morph = Put(new Creature("Secret Morph", "{3}", 2, 2), _bob, ZoneType.Battlefield);
        morph.MarkFaceDown();
        var ev = new PermanentTappedEvent(morph);
        EventPayloadBuilder.RequiresPerViewerMasking(ev).Should().BeTrue();
        S(EventPayloadBuilder.Build(ev, _alice), "permanentName").Should().Be("Face-down permanent");
        // CR 708.6 - its controller may look at it.
        S(EventPayloadBuilder.Build(ev, _bob), "permanentName").Should().Be("Secret Morph");
    }

    [Fact]
    public void DamageDealt_NamesItsSource_AndMasksAFaceDownSource()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        var p = EventPayloadBuilder.Build(new DamageDealtEvent(bolt, null, null, _bob, 3, DamageType.Spell));
        p.GetProperty("sourceInstanceId").GetGuid().Should().Be(bolt.InstanceId);
        p.GetProperty("sourceName").GetString().Should().Be("Lightning Bolt");

        var byPlayer = EventPayloadBuilder.Build(new DamageDealtEvent(null, _alice, null, _bob, 1, DamageType.Spell));
        byPlayer.GetProperty("sourceName").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        EventPayloadBuilder.RequiresPerViewerMasking(
            new DamageDealtEvent(bolt, null, null, _bob, 3, DamageType.Spell)).Should().BeFalse();
    }

    [Theory]
    [InlineData("R", "{R}")]
    [InlineData("GC", "{C}{G}")]
    [InlineData("{2}", "{2}")]
    public void ManaAbilityActivated_ManaIsInBraceForm(string produced, string wire)
    {
        var land = Put(new Land("Some Land"), _alice, ZoneType.Battlefield);
        var ability = new Majik.Core.Abilities.ManaAbility(land, _alice, ManaCost.Parse(produced));
        S(EventPayloadBuilder.Build(new ManaAbilityActivatedEvent(ability, _alice, ManaCost.Parse(produced))), "mana")
            .Should().Be(wire);
    }

    [Fact]
    public void CostsPaid_AManaCost_IsInBraceForm()
    {
        var bolt = Put(new Instant("Lightning Bolt", "{R}"), _alice, ZoneType.Stack);
        var spell = new Spell(bolt, _alice);
        var p = EventPayloadBuilder.Build(new CostsPaidEvent(spell, new ICost[] { new ManaCostCost("2R") }));
        p.GetProperty("costs").EnumerateArray().Select(e => e.GetString()).Should().Equal("{2}{R}");
    }

    [Theory]
    [InlineData("1WU", "{1}{W}{U}")]
    [InlineData("{2}{R}", "{2}{R}")]
    [InlineData("{R/G}{R/G}", "{R/G}{R/G}")]
    [InlineData("{1}{R} // {2}{U}", "{1}{R} // {2}{U}")]
    [InlineData("", "")]
    public void ACardsManaCost_IsInBraceForm_InSnapshotsAndMoves(string printed, string wire)
    {
        var bear = Put(new Creature("Bear", printed, 2, 2), _alice, ZoneType.Battlefield);
        Majik.Core.Api.StateSnapshotter.SnapshotCard(bear, _alice).ManaCost.Should().Be(wire);
        var moved = EventPayloadBuilder.Build(new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard));
        (moved.TryGetProperty("manaCost", out var mc) ? mc.GetString() ?? "" : "").Should().Be(wire);
    }
}