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
        // Enriched fields the portal patch path needs to render the
        // destination zone without refetching /state:
        payload.GetProperty("manaCost").GetString().Should().Be("R");
        payload.TryGetProperty("hidden", out _).Should().BeFalse();
    }

    // CardMovedEvent masking matrix (CR 706). Each row pairs a
    // (from, to) zone transition with whether a non-owner viewer must
    // see card identity. Rule: an event reveals the card iff EITHER the
    // source or destination zone is public to opponents (Battlefield /
    // Graveyard / Exile / Stack). When both are hidden (Hand / Library)
    // the move never leaves a public footprint and must stay masked.
    [Theory]
    // Library origin
    [InlineData(ZoneType.Library, ZoneType.Battlefield, true)]   // search → BF (public reveal)
    [InlineData(ZoneType.Library, ZoneType.Graveyard, true)]    // mill (public)
    [InlineData(ZoneType.Library, ZoneType.Exile, true)]        // exile from top (public)
    [InlineData(ZoneType.Library, ZoneType.Hand, false)]        // draw (hidden→hidden)
    // Hand origin
    [InlineData(ZoneType.Hand, ZoneType.Battlefield, true)]    // play / cast result (public)
    [InlineData(ZoneType.Hand, ZoneType.Graveyard, true)]      // discard (public)
    [InlineData(ZoneType.Hand, ZoneType.Exile, true)]          // exile from hand (public)
    [InlineData(ZoneType.Hand, ZoneType.Hand, false)]          // rare shuffle effect (hidden→hidden)
    [InlineData(ZoneType.Hand, ZoneType.Library, false)]       // bottom of library (hidden→hidden)
    [InlineData(ZoneType.Hand, ZoneType.Stack, true)]          // cast spell (public)
    // Battlefield origin — was always public; destination doesn't matter.
    [InlineData(ZoneType.Battlefield, ZoneType.Graveyard, true)]
    [InlineData(ZoneType.Battlefield, ZoneType.Hand, true)]
    [InlineData(ZoneType.Battlefield, ZoneType.Library, true)]
    [InlineData(ZoneType.Battlefield, ZoneType.Exile, true)]
    // Graveyard origin
    [InlineData(ZoneType.Graveyard, ZoneType.Battlefield, true)]
    [InlineData(ZoneType.Graveyard, ZoneType.Hand, true)]
    [InlineData(ZoneType.Graveyard, ZoneType.Exile, true)]
    [InlineData(ZoneType.Graveyard, ZoneType.Library, true)]
    // Exile origin
    [InlineData(ZoneType.Exile, ZoneType.Battlefield, true)]
    [InlineData(ZoneType.Exile, ZoneType.Hand, true)]
    [InlineData(ZoneType.Exile, ZoneType.Graveyard, true)]
    [InlineData(ZoneType.Exile, ZoneType.Library, true)]
    // Stack origin — counterspell, return-to-hand-while-on-stack, etc.
    [InlineData(ZoneType.Stack, ZoneType.Graveyard, true)]
    [InlineData(ZoneType.Stack, ZoneType.Hand, true)]
    [InlineData(ZoneType.Stack, ZoneType.Library, true)]
    [InlineData(ZoneType.Stack, ZoneType.Exile, true)]
    public void CardMovedEvent_MaskingMatrix_RevealsWhenEitherZoneIsPublic(
        ZoneType from, ZoneType to, bool opponentSeesCardName)
    {
        var alice = new Player("Alice");
        var bob = new Player("Bob");
        var card = new Card("Secret", "R");
        card.SetOwner(alice);
        var e = new CardMovedEvent(card, from, to);

        // Owner always sees full card identity regardless of transition.
        var ownerPayload = EventPayloadBuilder.Build(e, alice);
        ownerPayload.GetProperty("cardName").GetString().Should().Be("Secret");
        ownerPayload.TryGetProperty("hidden", out _).Should().BeFalse();

        // Opponent view: masked iff both zones are hidden.
        var opponentPayload = EventPayloadBuilder.Build(e, bob);
        if (opponentSeesCardName)
        {
            opponentPayload.GetProperty("cardName").GetString().Should().Be("Secret");
            opponentPayload.TryGetProperty("hidden", out _).Should().BeFalse();
        }
        else
        {
            opponentPayload.TryGetProperty("cardName", out _).Should().BeFalse();
            opponentPayload.TryGetProperty("cardId", out _).Should().BeFalse();
            opponentPayload.GetProperty("hidden").GetBoolean().Should().BeTrue();
            // Owner / zone metadata still flows so the opponent's UI can
            // increment the right hand-count / library-count.
            opponentPayload.GetProperty("ownerId").GetGuid().Should().Be(alice.Id);
            opponentPayload.GetProperty("from").GetString().Should().Be(from.ToString());
            opponentPayload.GetProperty("to").GetString().Should().Be(to.ToString());
        }

        // Spectator (viewer == null) is full-reveal — same as owner.
        var spectator = EventPayloadBuilder.Build(e, viewer: null);
        spectator.GetProperty("cardName").GetString().Should().Be("Secret");
    }

    [Fact]
    public void CardDrawnEvent_Owner_SeesCardIdentity()
    {
        var alice = new Player("Alice");
        var card = new Card("Lightning Bolt", "R");
        card.SetOwner(alice);
        var e = new CardDrawnEvent(card, alice);

        var payload = EventPayloadBuilder.Build(e, alice);

        payload.GetProperty("cardId").GetGuid().Should().Be(card.InstanceId);
        payload.GetProperty("cardName").GetString().Should().Be("Lightning Bolt");
        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
        payload.TryGetProperty("hidden", out _).Should().BeFalse();
    }

    [Fact]
    public void CardDrawnEvent_Opponent_GetsMaskedPayload()
    {
        // Library → Hand: always both-hidden. Non-owner viewer sees only
        // the draw count signal — never the card name or instance id.
        var alice = new Player("Alice");
        var bob = new Player("Bob");
        var card = new Card("Lightning Bolt", "R");
        card.SetOwner(alice);
        var e = new CardDrawnEvent(card, alice);

        var payload = EventPayloadBuilder.Build(e, bob);

        payload.TryGetProperty("cardId", out _).Should().BeFalse();
        payload.TryGetProperty("cardName", out _).Should().BeFalse();
        payload.GetProperty("playerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("hidden").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CardDrawnEvent_Spectator_SeesFullIdentity()
    {
        var alice = new Player("Alice");
        var card = new Card("Lightning Bolt", "R");
        card.SetOwner(alice);
        var e = new CardDrawnEvent(card, alice);

        var payload = EventPayloadBuilder.Build(e, viewer: null);

        payload.GetProperty("cardName").GetString().Should().Be("Lightning Bolt");
    }

    [Fact]
    public void RequiresPerViewerMasking_FlagsDrawAndHiddenMoves_NotPublicMoves()
    {
        var alice = new Player("Alice");
        var card = new Card("X", "");
        card.SetOwner(alice);

        // CardDrawnEvent → always per-viewer (library → hand).
        EventPayloadBuilder.RequiresPerViewerMasking(new CardDrawnEvent(card, alice))
            .Should().BeTrue();

        // CardMovedEvent — only when both zones hidden.
        EventPayloadBuilder.RequiresPerViewerMasking(
            new CardMovedEvent(card, ZoneType.Library, ZoneType.Hand)).Should().BeTrue();
        EventPayloadBuilder.RequiresPerViewerMasking(
            new CardMovedEvent(card, ZoneType.Hand, ZoneType.Library)).Should().BeTrue();
        EventPayloadBuilder.RequiresPerViewerMasking(
            new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield)).Should().BeFalse();
        EventPayloadBuilder.RequiresPerViewerMasking(
            new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard)).Should().BeFalse();
        EventPayloadBuilder.RequiresPerViewerMasking(
            new CardMovedEvent(card, ZoneType.Library, ZoneType.Battlefield)).Should().BeFalse();

        // Non-card events never need viewer masking.
        EventPayloadBuilder.RequiresPerViewerMasking(new LifeChangedEvent(alice, 20, 17))
            .Should().BeFalse();
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
    public void StackObjectAddedEvent_TriggeredAbilityPayload_MatchesStateSnapshotter()
    {
        // Regression guard: when PriorityManager.InitializeForPhase drains
        // a pending triggered ability onto the stack (Rule 603.3),
        // Stack.Push publishes a StackObjectAddedEvent. GameFacade.BridgeEvent
        // routes every GameEvent through EventPayloadBuilder, so the portal
        // can patch state.stack from the wire delta BEFORE the next priority
        // prompt is delivered. The discriminator and description here must
        // mirror StateSnapshotter.SnapshotStackObject so the patched stack
        // item matches what a fresh /state fetch would return — otherwise
        // the portal's optimistic stack render diverges from the snapshot.
        var alice = new Player("Alice");
        var source = new Majik.Core.Cards.Creature("Dredger's Insight", "2U", 1, 3)
        {
            Owner = alice,
            Zone = ZoneType.Battlefield,
        };
        var ability = new Majik.Core.Abilities.TriggeredAbility(
            source,
            alice,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(source));
        var e = new StackObjectAddedEvent(ability);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("stackId").GetGuid().Should().Be(ability.Id);
        payload.GetProperty("controllerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("kind").GetString().Should().Be("TriggeredAbility");
        payload.GetProperty("description").GetString().Should().Be("Dredger's Insight trigger");
    }

    [Fact]
    public void GameFacade_EnvelopeSubscriber_ReceivesStackObjectAddedEvent_WhenTriggerDrainsOntoStack()
    {
        // End-to-end regression: a triggered ability drained onto the
        // stack by PriorityManager.InitializeForPhase (which TriggerManager
        // routes through Stack.Push) must surface as a StackObjectAddedEvent
        // on the facade's envelope subscription. The MatchFacadeBridge
        // subscribes here, so this is the contract that guarantees the
        // populated-stack snapshot reaches the portal BEFORE the next
        // priority prompt fires — without this, the UI never gets a chance
        // to animate the trigger landing before both agents auto-pass and
        // the stack item resolves.
        //
        // The facade hides TriggerManager from public callers, so we
        // reach in via reflection to register a synthetic ETB trigger
        // on a card already on the battlefield. This is the minimum
        // surface needed to demonstrate the drain path without
        // depending on the full binder + spell-definition pipeline.
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        var envelopes = new List<Majik.Core.Api.Dtos.EventEnvelope>();
        using var _ = facade.SubscribeEnvelopes(envelopes.Add);

        var alice = facade.Alice;
        var source = new Creature("Dredger's Insight", "2U", 1, 3)
        {
            Owner = alice,
            Zone = ZoneType.Battlefield,
        };
        alice.Zones.Battlefield.AddCard(source);

        var triggers = (Majik.Core.Abilities.TriggerManager)typeof(GameFacade)
            .GetField("_triggers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;
        var priority = (Majik.Core.Game.PriorityManager)typeof(GameFacade)
            .GetField("_priority", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;
        var bus = (EventBus)typeof(GameFacade)
            .GetField("_bus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

        var ability = new Majik.Core.Abilities.TriggeredAbility(
            source, alice, Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(source));
        triggers.RegisterTriggeredAbility(ability);

        // Fire the ETB via the bus — pending list accumulates a trigger
        // that PriorityManager.InitializeForPhase will then drain.
        bus.Publish(new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        envelopes.Clear();
        priority.InitializeForPhase(alice);

        envelopes.Should().Contain(env => env.Public.Type == nameof(StackObjectAddedEvent),
            "draining a pending triggered ability publishes StackObjectAddedEvent, " +
            "which BridgeEvent must forward through SubscribeEnvelopes so the " +
            "MatchFacadeBridge can fan it out over SignalR.");

        var added = envelopes.Single(env => env.Public.Type == nameof(StackObjectAddedEvent));
        added.Public.Payload.GetProperty("kind").GetString().Should().Be("TriggeredAbility");
        added.Public.Payload.GetProperty("description").GetString().Should().Be("Dredger's Insight trigger");
        added.Public.Payload.GetProperty("controllerId").GetGuid().Should().Be(alice.Id);
        added.PerPlayer.Should().BeNull(
            "stack additions reveal the same info to both seats — no CR 706 masking needed.");
    }

    [Fact]
    public void StackObjectResolvedEvent_TriggeredAbilityPayload_MatchesStateSnapshotter()
    {
        // Symmetric guard: the resolve event must carry the same kind +
        // description as the corresponding added event so the portal can
        // unconditionally remove the matching stack item by id without
        // having to re-classify the object.
        var alice = new Player("Alice");
        var source = new Majik.Core.Cards.Creature("Dredger's Insight", "2U", 1, 3)
        {
            Owner = alice,
            Zone = ZoneType.Battlefield,
        };
        var ability = new Majik.Core.Abilities.TriggeredAbility(
            source,
            alice,
            Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf(source));
        var e = new StackObjectResolvedEvent(ability);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("stackId").GetGuid().Should().Be(ability.Id);
        payload.GetProperty("controllerId").GetGuid().Should().Be(alice.Id);
        payload.GetProperty("kind").GetString().Should().Be("TriggeredAbility");
        payload.GetProperty("description").GetString().Should().Be("Dredger's Insight trigger");
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

    // CR 505 — since Slice 3 the phase value itself distinguishes
    // PreCombatMain from PostCombatMain, so EventPayloadBuilder emits the
    // disambiguated label straight from the phase. The TurnStateType context
    // is accepted for call-site compatibility but no longer affects the label.
    [Fact]
    public void StepStartedEvent_PreCombatMain_EmitsPreCombatMain()
    {
        var alice = new Player("Alice");
        var e = new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.PreCombatMain, alice);

        var payload = EventPayloadBuilder.Build(e, viewer: null,
            turnState: Majik.Core.StateMachine.TurnStateType.PreCombatMain);

        payload.GetProperty("step").GetString().Should().Be(PhaseLabelResolver.PreCombatMain);
    }

    [Fact]
    public void StepStartedEvent_PostCombatMain_EmitsPostCombatMain_IndependentOfTurnState()
    {
        var alice = new Player("Alice");
        var e = new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.PostCombatMain, alice);

        // Turn-state deliberately left at PreCombatMain to prove the label is
        // driven by the phase value alone, not the turn state.
        var payload = EventPayloadBuilder.Build(e, viewer: null,
            turnState: Majik.Core.StateMachine.TurnStateType.PreCombatMain);

        payload.GetProperty("step").GetString().Should().Be(PhaseLabelResolver.PostCombatMain);
    }

    [Fact]
    public void StepStartedEvent_NonMain_IgnoresTurnState()
    {
        var alice = new Player("Alice");
        var e = new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.Upkeep, alice);

        var payload = EventPayloadBuilder.Build(e, viewer: null,
            turnState: Majik.Core.StateMachine.TurnStateType.PreCombatMain);

        payload.GetProperty("step").GetString().Should().Be("Upkeep");
    }

    [Fact]
    public void StepStartedEvent_PreCombatMain_WithoutTurnState_StillDisambiguated()
    {
        // Callers that don't supply a TurnStateType now still get the
        // first-class label, since the phase value is authoritative.
        var alice = new Player("Alice");
        var e = new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.PreCombatMain, alice);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("step").GetString().Should().Be(PhaseLabelResolver.PreCombatMain);
    }

    [Fact]
    public void PhaseChangedEvent_EmitsRawPhaseLabelsVerbatim()
    {
        // PhaseChangedEvent carries the PhaseState.Name strings, which are now
        // already the first-class CR 505 labels — emitted verbatim.
        var e = new PhaseChangedEvent(previousPhase: "Draw", currentPhase: PhaseLabelResolver.PreCombatMain);

        var payload = EventPayloadBuilder.Build(e, viewer: null,
            turnState: Majik.Core.StateMachine.TurnStateType.PreCombatMain);

        payload.GetProperty("from").GetString().Should().Be("Draw");
        payload.GetProperty("to").GetString().Should().Be(PhaseLabelResolver.PreCombatMain);
    }

    [Fact]
    public void TurnStateChangedEvent_EmitsFromAndToTurnStateNames()
    {
        var e = new TurnStateChangedEvent(
            Majik.Core.StateMachine.TurnStateType.TurnBeginning,
            Majik.Core.StateMachine.TurnStateType.PreCombatMain);

        var payload = EventPayloadBuilder.Build(e);

        payload.GetProperty("from").GetString().Should().Be("TurnBeginning");
        payload.GetProperty("to").GetString().Should().Be("PreCombatMain");
    }
}
