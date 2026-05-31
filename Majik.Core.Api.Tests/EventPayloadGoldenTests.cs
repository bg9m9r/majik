using System.Text.Json;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// PLAN 07 — golden-JSON tests locking the EXACT camelCase wire shape
/// (key set + values) of each typed payload record. These complement the
/// behavioural assertions in <see cref="EventPayloadTests"/> by pinning the
/// full property set, so a record-field rename or an accidental
/// null-serialization change is caught immediately. The frontend reducer
/// reads these exact keys.
/// </summary>
public class EventPayloadGoldenTests
{
    private static IReadOnlyList<string> Keys(JsonElement e)
        => e.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();

    [Fact]
    public void LifeChangedPayload_Golden()
    {
        var alice = new Player("Alice");
        var payload = EventPayloadBuilder.Build(new LifeChangedEvent(alice, 20, 17));

        Keys(payload).Should().BeEquivalentTo(new[] { "playerId", "previous", "current" });
        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("previous").GetInt32().Should().Be(20);
        payload.GetProperty("current").GetInt32().Should().Be(17);
    }

    [Fact]
    public void CardRevealedPayload_Golden()
    {
        var alice = new Player("Alice");
        var card = new Card("Lightning Bolt", "R") { Owner = alice };
        var payload = EventPayloadBuilder.Build(
            new CardRevealedEvent(card, alice, ZoneType.Hand, "reveal-effect"));

        Keys(payload).Should().BeEquivalentTo(
            new[] { "cardId", "cardName", "playerId", "from", "reason" });
        payload.GetProperty("cardId").GetGuid().Should().Be(card.InstanceId);
        payload.GetProperty("cardName").GetString().Should().Be("Lightning Bolt");
        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("from").GetString().Should().Be("Hand");
        payload.GetProperty("reason").GetString().Should().Be("reveal-effect");
    }

    [Fact]
    public void PhaseStartedPayload_Golden()
    {
        var alice = new Player("Alice");
        var payload = EventPayloadBuilder.Build(
            new PhaseStartedEvent(Majik.Core.StateMachine.PhaseStateType.PreCombatMain, alice));

        Keys(payload).Should().BeEquivalentTo(new[] { "phase", "playerId" });
        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("phase").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void PhaseEndedPayload_Golden()
    {
        var alice = new Player("Alice");
        var payload = EventPayloadBuilder.Build(
            new PhaseEndedEvent(Majik.Core.StateMachine.PhaseStateType.PreCombatMain, alice));

        Keys(payload).Should().BeEquivalentTo(new[] { "phase", "playerId" });
    }

    [Fact]
    public void StepStartedPayload_Golden()
    {
        var alice = new Player("Alice");
        var payload = EventPayloadBuilder.Build(
            new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.Upkeep, alice));

        Keys(payload).Should().BeEquivalentTo(new[] { "step", "playerId" });
        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
    }

    [Fact]
    public void StepEndedPayload_Golden()
    {
        var alice = new Player("Alice");
        var payload = EventPayloadBuilder.Build(
            new StepEndedEvent(Majik.Core.StateMachine.PhaseStateType.Upkeep, alice));

        Keys(payload).Should().BeEquivalentTo(new[] { "step", "playerId" });
    }

    [Fact]
    public void PhaseChangedPayload_Golden()
    {
        var payload = EventPayloadBuilder.Build(
            new PhaseChangedEvent("Draw", "PreCombatMain"));

        Keys(payload).Should().BeEquivalentTo(new[] { "from", "to" });
        payload.GetProperty("from").GetString().Should().Be("Draw");
        payload.GetProperty("to").GetString().Should().Be("PreCombatMain");
    }

    [Fact]
    public void TurnStateChangedPayload_Golden()
    {
        var payload = EventPayloadBuilder.Build(new TurnStateChangedEvent(
            Majik.Core.StateMachine.TurnStateType.TurnBeginning,
            Majik.Core.StateMachine.TurnStateType.PreCombatMain));

        Keys(payload).Should().BeEquivalentTo(new[] { "from", "to" });
        payload.GetProperty("from").GetString().Should().Be("TurnBeginning");
        payload.GetProperty("to").GetString().Should().Be("PreCombatMain");
    }

    [Fact]
    public void TurnStartedPayload_Golden()
    {
        var alice = new Player("Alice");
        var payload = EventPayloadBuilder.Build(new TurnStartedEvent(alice, 3));

        Keys(payload).Should().BeEquivalentTo(new[] { "playerId", "turn" });
        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("turn").GetInt32().Should().Be(3);
    }

    [Fact]
    public void TurnEndedPayload_Golden()
    {
        var alice = new Player("Alice");
        var payload = EventPayloadBuilder.Build(new TurnEndedEvent(alice, 3));

        Keys(payload).Should().BeEquivalentTo(new[] { "playerId", "turn" });
    }

    [Fact]
    public void ExtraPhaseAddedPayload_Golden()
    {
        var payload = EventPayloadBuilder.Build(
            new ExtraPhaseAddedEvent(Majik.Core.StateMachine.PhaseStateType.PostCombatMain));

        Keys(payload).Should().BeEquivalentTo(new[] { "phase" });
        payload.GetProperty("phase").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void PlayerLostPayload_Golden()
    {
        var alice = new Player("Alice");
        var payload = EventPayloadBuilder.Build(new PlayerLostEvent(alice));

        Keys(payload).Should().BeEquivalentTo(new[] { "playerId" });
        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
    }

    [Fact]
    public void StackObjectPayload_SpellCast_Golden()
    {
        var alice = new Player("Alice");
        var bolt = new Instant("Lightning Bolt", "R") { Owner = alice };
        var spell = new Majik.Core.Spells.Spell(bolt, alice);
        var payload = EventPayloadBuilder.Build(new SpellCastEvent(spell));

        // SpellCast carries the backing-card identity.
        Keys(payload).Should().BeEquivalentTo(
            new[] { "stackId", "controllerId", "kind", "description", "cardId", "cardName" });
        payload.GetProperty("kind").GetString().Should().Be("Spell");
        payload.GetProperty("cardId").GetGuid().Should().Be(bolt.InstanceId);
        payload.GetProperty("cardName").GetString().Should().Be("Lightning Bolt");
        payload.GetProperty("description").GetString().Should().Be("Lightning Bolt");
    }

    [Fact]
    public void StackObjectPayload_StackObjectAdded_Golden_NoCardKeys()
    {
        // StackObjectAdded/Resolved deliberately omit card identity even for
        // a spell — the null CardId/CardName drop out (legacy wire parity).
        var alice = new Player("Alice");
        var bolt = new Instant("Bolt", "R") { Owner = alice };
        var spell = new Majik.Core.Spells.Spell(bolt, alice);
        var payload = EventPayloadBuilder.Build(new StackObjectAddedEvent(spell));

        Keys(payload).Should().BeEquivalentTo(
            new[] { "stackId", "controllerId", "kind", "description" });
        payload.TryGetProperty("cardId", out _).Should().BeFalse();
        payload.TryGetProperty("cardName", out _).Should().BeFalse();
    }

    [Fact]
    public void DamageDealtPayload_Golden()
    {
        var caster = new Player("Caster");
        var victim = new Player("Victim");
        var e = new DamageDealtEvent(
            sourceCard: null, sourcePlayer: caster,
            targetCard: null, targetPlayer: victim,
            amount: 3, damageType: DamageType.Spell);
        var payload = EventPayloadBuilder.Build(e);

        Keys(payload).Should().BeEquivalentTo(new[]
        {
            "sourceInstanceId", "targetInstanceId", "targetIsPlayer", "amount", "damageType",
        });
        payload.GetProperty("sourceInstanceId").GetGuid().Should().Be(caster.Id);
        payload.GetProperty("targetInstanceId").GetGuid().Should().Be(victim.Id);
        payload.GetProperty("targetIsPlayer").GetBoolean().Should().BeTrue();
        payload.GetProperty("amount").GetInt32().Should().Be(3);
        payload.GetProperty("damageType").GetString().Should().Be("Spell");
    }

    [Fact]
    public void CounterAddedPayload_Golden()
    {
        var alice = new Player("Alice");
        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = alice,
            Controller = alice,
            Zone = ZoneType.Battlefield,
        };
        var e = new CounterAddedEvent(bear, Majik.Core.Counters.CounterType.PlusOnePlusOne, 2);
        var payload = EventPayloadBuilder.Build(e);

        // controllerId present (non-null) on this path.
        Keys(payload).Should().BeEquivalentTo(new[]
        {
            "targetInstanceId", "counterType", "amount", "controllerId",
        });
        payload.GetProperty("targetInstanceId").GetGuid().Should().Be(bear.InstanceId);
        payload.GetProperty("counterType").GetString().Should().Be("+1/+1");
        payload.GetProperty("amount").GetInt32().Should().Be(2);
        payload.GetProperty("controllerId").GetGuid().Should().Be(alice.Id);
    }

    [Fact]
    public void CardMovedPayload_MaskedGolden_ExactlyFourKeys()
    {
        var alice = new Player("Alice");
        var bob = new Player("Bob");
        var card = new Creature("Tarmogoyf", "1G", 4, 5) { Owner = alice };
        var payload = EventPayloadBuilder.Build(
            new CardMovedEvent(card, ZoneType.Hand, ZoneType.Library), bob);

        Keys(payload).Should().BeEquivalentTo(new[] { "ownerId", "from", "to", "hidden" });
        payload.GetProperty("hidden").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CardMovedPayload_RevealedGolden_NoHiddenKey()
    {
        var alice = new Player("Alice");
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = alice };
        var payload = EventPayloadBuilder.Build(
            new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield), viewer: null);

        Keys(payload).Should().BeEquivalentTo(new[]
        {
            "cardId", "cardName", "ownerId", "manaCost", "types", "from", "to",
            "power", "toughness", "tapped", "summoningSickness", "abilities",
            "producedManaColors", "counters",
        });
        payload.TryGetProperty("hidden", out _).Should().BeFalse();
    }

    [Fact]
    public void CardMovedPayload_RevealedLand_DropsNullPowerToughness()
    {
        // A non-creature (no P/T) revealed move drops power/toughness via
        // null-omitting serialization — the portal reads them defensively.
        var alice = new Player("Alice");
        var land = new Card("Forest", "") { Owner = alice };
        var payload = EventPayloadBuilder.Build(
            new CardMovedEvent(land, ZoneType.Hand, ZoneType.Graveyard), viewer: null);

        payload.TryGetProperty("power", out _).Should().BeFalse();
        payload.TryGetProperty("toughness", out _).Should().BeFalse();
        payload.GetProperty("cardName").GetString().Should().Be("Forest");
    }

    [Fact]
    public void CardDrawnPayload_MaskedGolden_ExactlyTwoKeys()
    {
        var alice = new Player("Alice");
        var bob = new Player("Bob");
        var card = new Card("Island", "") { Owner = alice };
        var payload = EventPayloadBuilder.Build(new CardDrawnEvent(card, alice), bob);

        Keys(payload).Should().BeEquivalentTo(new[] { "playerId", "hidden" });
        payload.GetProperty("hidden").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CardDrawnPayload_RevealedGolden_NoHiddenKey()
    {
        var alice = new Player("Alice");
        var card = new Card("Lightning Bolt", "R") { Owner = alice };
        var payload = EventPayloadBuilder.Build(new CardDrawnEvent(card, alice), viewer: null);

        Keys(payload).Should().BeEquivalentTo(
            new[] { "cardId", "cardName", "playerId", "manaCost", "types" });
        payload.TryGetProperty("hidden", out _).Should().BeFalse();
    }
}
