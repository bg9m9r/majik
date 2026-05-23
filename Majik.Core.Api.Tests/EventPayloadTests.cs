using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>Locks the EventPayloadBuilder mapping. Wire-format payload
/// shapes are part of the API contract — changes here are visible to
/// the frontend.</summary>
public class EventPayloadTests
{
    [Fact]
    public void CardMovedEvent_PayloadContainsCardIdAndZones()
    {
        var card = new Card("Lightning Bolt", "R");
        var e = new CardMovedEvent(card, ZoneType.Hand, ZoneType.Stack);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("cardId").GetGuid().Should().Be(card.InstanceId);
        payload.GetProperty("cardName").GetString().Should().Be("Lightning Bolt");
        payload.GetProperty("from").GetString().Should().Be("Hand");
        payload.GetProperty("to").GetString().Should().Be("Stack");
    }

    [Fact]
    public void LifeChangedEvent_PayloadCarriesPreviousAndCurrent()
    {
        var alice = new Player("Alice");
        var e = new LifeChangedEvent(alice, 20, 17);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("previous").GetInt32().Should().Be(20);
        payload.GetProperty("current").GetInt32().Should().Be(17);
    }

    [Fact]
    public void SpellCastEvent_PayloadCarriesStackItemFields()
    {
        // Spell payload must carry enough for the frontend to append a
        // StackItem entry to its snapshot without refetching: stack id,
        // controller, kind discriminator + display description.
        var alice = new Player("Alice");
        var bolt = new Instant("Lightning Bolt", "R") { Owner = alice };
        var spell = new Majik.Core.Spells.Spell(bolt, alice);
        var e = new SpellCastEvent(spell);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("stackId").GetGuid().Should().Be(spell.Id);
        payload.GetProperty("controllerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("cardId").GetGuid().Should().Be(bolt.InstanceId);
        payload.GetProperty("cardName").GetString().Should().Be("Lightning Bolt");
        payload.GetProperty("kind").GetString().Should().Be("Spell");
        payload.GetProperty("description").GetString().Should().Be("Lightning Bolt");
    }

    [Fact]
    public void StackObjectAddedEvent_SpellPayloadMirrorsStackObjectDto()
    {
        // The payload `kind` + `description` strings must match
        // StateSnapshotter.SnapshotStackObject so the portal can patch
        // state.stack directly from the event delta.
        var alice = new Player("Alice");
        var bolt = new Instant("Bolt", "R") { Owner = alice };
        var spell = new Majik.Core.Spells.Spell(bolt, alice);
        var e = new StackObjectAddedEvent(spell);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("stackId").GetGuid().Should().Be(spell.Id);
        payload.GetProperty("controllerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("kind").GetString().Should().Be("Spell");
        payload.GetProperty("description").GetString().Should().Be("Bolt");
    }

    [Fact]
    public void StackObjectResolvedEvent_SpellPayloadMirrorsStackObjectDto()
    {
        var alice = new Player("Alice");
        var bolt = new Instant("Bolt", "R") { Owner = alice };
        var spell = new Majik.Core.Spells.Spell(bolt, alice);
        var e = new StackObjectResolvedEvent(spell);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("stackId").GetGuid().Should().Be(spell.Id);
        payload.GetProperty("controllerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("kind").GetString().Should().Be("Spell");
        payload.GetProperty("description").GetString().Should().Be("Bolt");
    }

    [Fact]
    public void CardRevealedEvent_PayloadCarriesCardPlayerSourceAndReason()
    {
        // CR 701.16 reveals — payload must let the portal flash the opponent's
        // card briefly: cardId for animation continuity, cardName for the
        // tooltip, playerId so the right hand is highlighted, from so the
        // client can sanity-check the source zone, reason for UI affordance.
        var bob = new Player("Bob");
        var bolt = new Instant("Lightning Bolt", "R") { Owner = bob };
        var e = new CardRevealedEvent(bolt, bob, ZoneType.Hand, "Thoughtseize");

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("cardId").GetGuid().Should().Be(bolt.InstanceId);
        payload.GetProperty("cardName").GetString().Should().Be("Lightning Bolt");
        payload.GetProperty("playerId").GetGuid().Should().Be(bob.Id);
        payload.GetProperty("from").GetString().Should().Be("Hand");
        payload.GetProperty("reason").GetString().Should().Be("Thoughtseize");
    }

    [Fact]
    public void CombatDamageDealtEvent_PayloadCarriesSourceTargetAndDamageType()
    {
        // Per-source/per-target damage payload (CR 119, CR 510). Frontend
        // reads sourceInstanceId/targetInstanceId to animate the damage
        // ping; targetIsPlayer + damageType drive which animation runs.
        var attacker = new Creature("Bear", "1G", 2, 2);
        var blocker = new Creature("Squirrel", "G", 1, 1);
        var e = new CombatDamageDealtEvent(attacker, blocker, 2);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("sourceInstanceId").GetGuid().Should().Be(attacker.InstanceId);
        payload.GetProperty("targetInstanceId").GetGuid().Should().Be(blocker.InstanceId);
        payload.GetProperty("targetIsPlayer").GetBoolean().Should().BeFalse();
        payload.GetProperty("amount").GetInt32().Should().Be(2);
        payload.GetProperty("damageType").GetString().Should().Be("Combat");
    }

    [Fact]
    public void CombatDamageDealtEvent_PlayerTarget_PayloadFlagsPlayer()
    {
        var attacker = new Creature("Bear", "1G", 2, 2);
        var victim = new Player("Victim");
        var e = new CombatDamageDealtEvent(attacker, victim, 2);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("sourceInstanceId").GetGuid().Should().Be(attacker.InstanceId);
        payload.GetProperty("targetInstanceId").GetGuid().Should().Be(victim.Id);
        payload.GetProperty("targetIsPlayer").GetBoolean().Should().BeTrue();
        payload.GetProperty("amount").GetInt32().Should().Be(2);
        payload.GetProperty("damageType").GetString().Should().Be("Combat");
    }

    [Fact]
    public void DamageDealtEvent_SpellDamage_SerializesSpellDamageType()
    {
        var caster = new Player("Caster");
        var target = new Creature("Victim", "1", 3, 3);
        var e = new DamageDealtEvent(
            sourceCard: null, sourcePlayer: caster,
            targetCard: target, targetPlayer: null,
            amount: 3, damageType: DamageType.Spell);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("sourceInstanceId").GetGuid().Should().Be(caster.Id);
        payload.GetProperty("targetInstanceId").GetGuid().Should().Be(target.InstanceId);
        payload.GetProperty("targetIsPlayer").GetBoolean().Should().BeFalse();
        payload.GetProperty("amount").GetInt32().Should().Be(3);
        payload.GetProperty("damageType").GetString().Should().Be("Spell");
    }

    [Fact]
    public void UnknownEvent_FallsBackToEmptyPayload()
    {
        // GameStartedEvent is the only known no-fields event but still
        // exercises the fallback path.
        var payload = EventPayloadBuilder.Build(new GameStartedEvent());

        payload.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        payload.EnumerateObject().Should().BeEmpty();
    }
}
