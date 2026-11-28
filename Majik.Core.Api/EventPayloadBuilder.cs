using System.Text.Json;
using System.Text.Json.Serialization;
using Majik.Core.Abilities;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Api;

/// <summary>
/// Maps engine <see cref="GameEvent"/> instances to lean JSON payloads
/// for transport. Engine events carry live object references (Player,
/// ICard) that we deliberately don't serialize directly — clients see
/// stable identifiers (Guids) and primitive fields only.
///
/// Unknown event types fall back to an empty payload; the client still
/// receives <c>Type</c> + <c>EventId</c> on the envelope so future
/// payload additions are non-breaking.
///
/// CR 706 hidden information: <see cref="CardMovedEvent"/> and
/// <see cref="CardDrawnEvent"/> sometimes describe a move between zones
/// that are hidden to a viewer (library / hand). The
/// <see cref="Build(GameEvent, Player)"/> overload masks card identity
/// for non-owner viewers when BOTH the source and destination zones are
/// hidden to opponents (Hand or Library) — i.e. the card was never
/// publicly visible at the time of the move. Any other transition
/// (movement that touches Battlefield, Graveyard, Exile, or Stack on
/// either side) reveals the card because the identity was already public
/// at the time of the move. The full-reveal overload (<c>viewer = null</c>)
/// keeps spectator / debug snapshot behavior.
/// </summary>
public static class EventPayloadBuilder
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // PLAN 07 — the dual-shape card payload records (CardMovedPayload /
    // CardDrawnPayload) carry the union of the masked + revealed field
    // sets, with the variant-specific fields nullable. Serializing with
    // WhenWritingNull collapses each constructed record back to EXACTLY
    // the historical key set:
    //   * masked CardMoved → {from, to, ownerId, hidden}
    //   * revealed CardMoved → identity + permanent fields, no `hidden`
    //     (a non-creature land's null power/toughness drop out, matching
    //     the CardSnapshotDto contract the portal reducer reads
    //     defensively via pickNumber).
    // Only these two records use it; every other payload keeps the
    // legacy Opts (null fields serialized verbatim) so non-dual-shape
    // shapes are byte-identical to the pre-PLAN-07 anonymous objects.
    private static readonly JsonSerializerOptions NullOmittingOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Full-reveal payload (no viewer scoping). Used for the
    /// spectator broadcast and any consumer outside the per-recipient
    /// SignalR routing path.</summary>
    public static JsonElement Build(GameEvent e) => Build(e, viewer: null, turnState: null);

    /// <summary>Viewer-scoped payload. <paramref name="viewer"/> = null
    /// means full reveal (spectator). Non-null applies CR 706 masking
    /// rules to <see cref="CardMovedEvent"/> / <see cref="CardDrawnEvent"/>;
    /// the rest of the payload set is public and ignores the viewer.</summary>
    public static JsonElement Build(GameEvent e, Player? viewer) => Build(e, viewer, turnState: null);

    /// <summary>Viewer-scoped payload with turn-state context. Since Slice 3
    /// the engine carries the precombat / postcombat distinction as
    /// first-class phase values, so phase / step labels are already
    /// disambiguated and the <paramref name="turnState"/> argument is retained
    /// only for call-site compatibility (it no longer affects the labels).</summary>
    public static JsonElement Build(GameEvent e, Player? viewer, PhaseStateType? turnState) =>
        Build(e, viewer, turnState, stack: null);

    /// <param name="stack">The live stack, when known: lets a move to the stack
    /// name the stack entry the card becomes (CR 601.2a).</param>
    internal static JsonElement Build(GameEvent e, Player? viewer, PhaseStateType? turnState,
        Majik.Core.Stack.Stack? stack) => e switch
    {
        CardMovedEvent x => BuildCardMoved(x, viewer, stack),
        CardDrawnEvent x => BuildCardDrawn(x, viewer),
        // CardRevealedEvent: hand-reveal effects (CR 701.16) are public
        // by definition — the controller chose to show the card to all
        // players. No per-viewer masking required.
        CardRevealedEvent x => Serialize(new CardRevealedPayload(
            CardId: x.Card.InstanceId,
            CardName: x.Card.Name,
            PlayerId: x.Player.Id,
            From: x.From.ToString(),
            Reason: x.Reason)),
        LifeChangedEvent x => Serialize(new LifeChangedPayload(
            PlayerId: x.Player.Id,
            Previous: x.PreviousLife,
            Current: x.NewLife)),
        GameStateChangedEvent x => Serialize(new GameStateChangedPayload(
            From: x.PreviousState?.ToString(),
            To: x.CurrentState.ToString())),
        PhaseStateChangedEvent x => Serialize(new PhaseStateChangedPayload(
            From: x.PreviousState?.ToString(),
            To: x.CurrentState.ToString())),
        StepStartedEvent x => Serialize(new StepStartedPayload(
            Step: PhaseLabelResolver.Resolve(x.StepType, turnState),
            PlayerId: x.Player.Id)),
        TurnStartedEvent x => Serialize(new TurnStartedPayload(
            PlayerId: x.Player.Id,
            Turn: x.TurnNumber)),
        TurnEndedEvent x => Serialize(new TurnEndedPayload(
            PlayerId: x.Player.Id,
            Turn: x.TurnNumber)),
        ExtraPhaseAddedEvent x => Serialize(new ExtraPhaseAddedPayload(
            Phase: x.PhaseType.ToString())),
        PlayerLostEvent x => Serialize(new PlayerLostPayload(
            PlayerId: x.Player.Id)),
        // SpellCastEvent / StackObject*Event payloads mirror StackObjectDto
        // (see Dtos.cs + StateSnapshotter.SnapshotStackObject) so the
        // frontend can patch `state.stack` from the wire delta without
        // re-fetching /state. `kind` matches the StackObjectDto.Kind
        // discriminator ("Spell" | "TriggeredAbility" | "ActivatedAbility")
        // and `description` mirrors the same composition rules used by the
        // snapshotter — keep these two builders in sync.
        // SpellCast carries the backing-card identity; StackObjectAdded /
        // Resolved deliberately do NOT (matching the historical wire) — the
        // null CardId/CardName drop out via NullOmittingStack serialization.
        SpellCastEvent x => SerializeStack(new StackObjectPayload(
            StackId: x.Spell.Id,
            ControllerId: x.Spell.Controller.Id,
            Kind: "Spell",
            Description: (x.Spell as ISpell)?.Card?.Name ?? "",
            CardId: (x.Spell as ISpell)?.Card?.InstanceId,
            CardName: (x.Spell as ISpell)?.Card?.Name)),
        StackObjectAddedEvent x => SerializeStack(new StackObjectPayload(
            StackId: x.EntryId ?? x.StackObject.Id,
            ControllerId: x.StackObject.Controller.Id,
            Kind: StackKind(x.StackObject),
            Description: StackDescription(x.StackObject, viewer))),
        StackObjectResolvedEvent x => SerializeStack(new StackObjectPayload(
            StackId: x.EntryId ?? x.StackObject.Id,
            ControllerId: x.StackObject.Controller.Id,
            Kind: StackKind(x.StackObject),
            Description: StackDescription(x.StackObject, viewer))),
        // Per-creature / per-player damage payload (CR 119, CR 510).
        // Frontend needs source + target Guids so it can draw a damage
        // animation from the attacker (or spell) to the victim — life
        // flash alone isn't enough at the per-creature level. We map
        // every DamageDealtEvent subclass (Combat / Spell / Ability)
        // to a single wire shape so the portal can render uniformly.
        DamageDealtEvent x => Serialize(new DamageDealtPayload(
            SourceInstanceId: x.SourceInstanceId,
            TargetInstanceId: x.TargetInstanceId,
            TargetIsPlayer: x.TargetIsPlayer,
            Amount: x.Amount,
            DamageType: x.DamageType.ToString())),
        // PLAN 04 — CR 121 / CR 614 counter placement. Lean payload so the
        // portal reducer can bump the target's counter badge in place
        // (display only — P/T are recomputed authoritatively by the next
        // snapshot, never derived from counters in the reducer). Counter
        // placement is public information (a counter on a battlefield
        // permanent is visible to all players), so no per-viewer masking.
        CounterAddedEvent x => Serialize(new CounterAddedPayload(
            TargetInstanceId: x.Target.InstanceId,
            CounterType: x.CounterType.Name,
            Amount: x.Amount,
            ControllerId: x.Controller?.Id)),
        // CR 613 — continuous (layer) effect entering / leaving the active
        // set. Log-only surface for the portal action log: source identity,
        // CR-613 Layer name, human-readable Description. Public info
        // (battlefield) — no per-viewer masking. A floating effect (no
        // source) projects Guid.Empty / "" so the wire keys are always present.
        ContinuousEffectAddedEvent x => Serialize(new ContinuousEffectAddedPayload(
            SourceInstanceId: x.Effect.Source?.InstanceId ?? Guid.Empty,
            SourceName: DisplayName(x.Effect.Source, viewer),
            Layer: x.Effect.Layer.ToString(),
            Description: x.Effect.Description)),
        ContinuousEffectRemovedEvent x => Serialize(new ContinuousEffectRemovedPayload(
            SourceInstanceId: x.Effect.Source?.InstanceId ?? Guid.Empty,
            SourceName: DisplayName(x.Effect.Source, viewer),
            Layer: x.Effect.Layer.ToString(),
            Description: x.Effect.Description)),
        // CR 602 — activated ability put on the stack. Carries controller +
        // source identity so the portal can render the game-log entry
        // ("Alice activated Bloodstained Mire") without refetching /state.
        AbilityActivatedEvent x => Serialize(new AbilityActivatedPayload(
            ControllerId: x.Ability.Controller.Id,
            SourceInstanceId: (x.Ability.Source as ICard)?.InstanceId ?? Guid.Empty,
            SourceName: DisplayName(x.Ability.Source as ICard, viewer))),
        // Long-tail log / animation payloads: ids plus display names, no state
        // lookups, no layer recomputation (names and ids are plain reads).
        PermanentTappedEvent x => Serialize(new PermanentTappedPayload(
            PermanentId: x.Permanent.InstanceId,
            PermanentName: DisplayName(x.Permanent, viewer),
            ControllerId: x.Permanent.Controller?.Id,
            CausedById: x.CausedBy?.Id)),
        TriggeredAbilityTriggeredEvent x => BuildTriggered(x, viewer),
        AttackersDeclaredEvent x => Serialize(new AttackersDeclaredPayload(
            AttackingPlayerId: x.Combat.AttackingPlayer.Id,
            Attackers: x.Combat.Attackers.Select(a => new DeclaredAttackerPayload(
                AttackerId: a.Creature.InstanceId,
                AttackerName: DisplayName(a.Creature, viewer),
                DefendingPlayerId: a.TargetPlayer?.Id,
                DefendingPermanentId: a.TargetPlaneswalker?.InstanceId)).ToList())),
        BlockersDeclaredEvent x => Serialize(new BlockersDeclaredPayload(
            DefendingPlayerId: x.Combat.DefendingPlayer?.Id,
            Blockers: x.Combat.GetAllBlockers().Select(b => new DeclaredBlockerPayload(
                BlockerId: b.Creature.InstanceId,
                BlockerName: DisplayName(b.Creature, viewer),
                AttackerId: b.BlockedAttacker.Creature.InstanceId)).ToList())),
        CreatureAttacksEvent x => Serialize(new CreatureAttacksPayload(
            AttackerId: x.Attacker.InstanceId,
            AttackerName: DisplayName(x.Attacker, viewer),
            ControllerId: x.Attacker.Controller?.Id,
            DefendingPlayerId: x.DefendingPlayer?.Id,
            DefendingPermanentId: (x.DefendingPlayerOrPlaneswalker as ICard)?.InstanceId)),
        CreatureBlocksEvent x => Serialize(new CreatureBlocksPayload(
            BlockerId: x.Blocker.InstanceId,
            BlockerName: DisplayName(x.Blocker, viewer),
            BlockerControllerId: x.Blocker.Controller?.Id,
            AttackerId: x.BlockedAttacker.InstanceId,
            AttackerName: DisplayName(x.BlockedAttacker, viewer))),
        // CR 701.9b — a discarded card is revealed on its way to the
        // graveyard; public.
        DiscardedEvent x => Serialize(new DiscardedPayload(
            PlayerId: x.Player.Id,
            CardId: x.Card.InstanceId,
            CardName: x.Card.Name,
            WasCost: x.WasCost)),
        // CR 708.9 — a face-down permanent leaving the battlefield is revealed.
        PermanentSacrificedEvent x => Serialize(new PermanentSacrificedPayload(
            PlayerId: x.SacrificingPlayer.Id,
            CardId: x.SacrificedCard.InstanceId,
            CardName: x.SacrificedCard.Name,
            WasToken: x.WasToken)),
        SpellCounteredEvent x => Serialize(new SpellCounteredPayload(
            StackId: x.CounteredSpell.Id,
            CardId: x.CounteredSpell.Card?.InstanceId,
            CardName: x.CounteredSpell.Card?.Name ?? "",
            ControllerId: x.CounteredSpell.Controller.Id,
            CounteringControllerId: x.CounteringController?.Id,
            CounteringStackId: x.CounteringObject is null ? null : x.CounteringEntryId ?? x.CounteringObject.Id,
            CounteringName: CounteringName(x.CounteringObject, viewer),
            Reason: x.Reason.ToString())),
        TriggeredAbilityCounteredEvent x => Serialize(new TriggeredAbilityCounteredPayload(
            StackId: x.EntryId ?? x.Ability.Id,
            SourceId: (x.Ability.Source as ICard)?.InstanceId,
            SourceName: DisplayName(x.Ability.Source as ICard, viewer),
            ControllerId: x.Ability.Controller.Id,
            Reason: x.Reason)),
        ManaAbilityActivatedEvent x => Serialize(new ManaAbilityActivatedPayload(
            PlayerId: x.Player.Id,
            SourceId: (x.Source as ICard)?.InstanceId,
            SourceName: DisplayName(x.Source as ICard, viewer),
            Mana: x.ManaGenerated.ToString())),
        LifePaidEvent x => Serialize(new LifePaidPayload(
            PlayerId: x.Player.Id,
            Amount: x.Amount,
            WasCost: x.WasCost)),
        CostsPaidEvent x => Serialize(new CostsPaidPayload(
            StackId: x.StackObject.Id,
            ControllerId: x.StackObject.Controller.Id,
            Costs: x.Costs.Select(c => c.Description).ToList())),
        TargetsChosenEvent x => Serialize(new TargetsChosenPayload(
            StackId: x.StackObject.Id,
            ControllerId: x.StackObject.Controller.Id,
            Targets: StateSnapshotter.TargetRefs(x.Targets.Select(TargetObject), viewer))),
        PriorityReceivedEvent x => Serialize(new PriorityPlayerPayload(x.Player.Id)),
        PriorityPassedEvent x => Serialize(new PriorityPlayerPayload(x.Player.Id)),
        CombatStartedEvent x => Serialize(new CombatPhasePayload(x.ActivePlayer.Id)),
        CombatEndedEvent x => Serialize(new CombatPhasePayload(x.Combat.AttackingPlayer.Id)),
        // CR 701.20 — only the player and the count; no card identity.
        LibraryShuffledEvent x => Serialize(new LibraryShuffledPayload(
            PlayerId: x.Player.Id,
            CardCount: x.CardCount,
            Reason: x.Reason)),
        SurveilEvent x => BuildSurveil(x, viewer),
        CardCycledEvent x => Serialize(new CardCycledPayload(
            PlayerId: x.Player.Id,
            CardId: x.Card.InstanceId,
            CardName: x.Card.Name)),
        ExtraTurnAddedEvent x => Serialize(new ExtraTurnAddedPayload(x.Player.Id)),
        // GameStartedEvent has no fields; the envelope's Type is the whole message.
        GameStartedEvent => Empty(),
        _ => Empty(),
    };

    /// <summary>True iff <paramref name="e"/>'s payload varies per viewer
    /// (CR 706). Bridge code uses this to decide between group broadcast
    /// and per-recipient publish — group fan-out of a payload that masks
    /// for some viewers but not others would leak the unmasked variant
    /// to the wrong seat.</summary>
    public static bool RequiresPerViewerMasking(GameEvent e) => e switch
    {
        CardMovedEvent x => BothZonesHidden(x.FromZone, x.ToZone) || EntersFaceDown(x)
            || IntoFaceDownExile(x) || (x.LeftFaceDownExile && IsHiddenZone(x.ToZone)),
        CardDrawnEvent => true, // library → hand: always both hidden
        // Library-bound cards are shown only to the surveilling player.
        SurveilEvent => true,
        TriggeredAbilityTriggeredEvent x => x.Ability.Source is ICard c
            && (IsHiddenZone(c.Zone) || IsFaceDown(c) || StateSnapshotter.IsExiledFaceDown(c)),
        TargetsChosenEvent x => x.Targets.Select(TargetObject)
            .Any(o => o is ICard c && (IsHiddenZone(c.Zone) || StateSnapshotter.IsExiledFaceDown(c))),
        // CR 708.2 — a face-down permanent's name is masked for non-controllers.
        PermanentTappedEvent x => IsFaceDown(x.Permanent),
        CreatureAttacksEvent x => IsFaceDown(x.Attacker),
        CreatureBlocksEvent x => IsFaceDown(x.Blocker) || IsFaceDown(x.BlockedAttacker),
        AttackersDeclaredEvent x => x.Combat.Attackers.Any(a => IsFaceDown(a.Creature)),
        BlockersDeclaredEvent x => x.Combat.GetAllBlockers().Any(b => IsFaceDown(b.Creature)),
        TriggeredAbilityCounteredEvent x => IsFaceDown(x.Ability.Source as ICard),
        SpellCounteredEvent x => x.CounteringObject is { } by && IsFaceDown(AbilitySource(by)),
        ManaAbilityActivatedEvent x => IsFaceDown(x.Source as ICard),
        AbilityActivatedEvent x => IsFaceDown(x.Ability.Source as ICard),
        ContinuousEffectAddedEvent x => IsFaceDown(x.Effect.Source),
        ContinuousEffectRemovedEvent x => IsFaceDown(x.Effect.Source),
        StackObjectAddedEvent x => IsFaceDown(AbilitySource(x.StackObject)),
        StackObjectResolvedEvent x => IsFaceDown(AbilitySource(x.StackObject)),
        _ => false,
    };

    /// <summary>Shown for a face-down permanent to anyone but its controller.</summary>
    internal const string FaceDownName = StateSnapshotter.FaceDownName;

    /// <summary>Shown for a card exiled face down to anyone who may not look at it.</summary>
    internal const string FaceDownExileName = StateSnapshotter.FaceDownExileName;

    private static bool IsFaceDown(ICard? card) => StateSnapshotter.IsFaceDown(card);

    /// <summary>The display name of what countered a spell: a spell's card name,
    /// an ability's source name (masked for a face-down source, CR 708.2).</summary>
    private static string? CounteringName(IStackObject? by, Player? viewer) => by switch
    {
        null => null,
        ISpell s => s.Card?.Name ?? "",
        _ => DisplayName(AbilitySource(by), viewer),
    };

    private static ICard? AbilitySource(IStackObject obj) => obj switch
    {
        ITriggeredAbility t => t.Source as ICard,
        IActivatedAbility a => a.Source as ICard,
        _ => null,
    };

    // CR 708.2 / 708.6 — a face-down permanent has no name; its controller may
    // look at it (and sees the card it represents). A null viewer is the
    // all-revealed (spectator/debug) view. Same rule as the snapshot.
    private static string DisplayName(ICard? card, Player? viewer) => StateSnapshotter.DisplayName(card, viewer);

    // CR 708.2 — a face-down permanent entering the battlefield (manifest,
    // cloak): its payload is the face-down 2/2 for anyone who may not look at it.
    private static bool EntersFaceDown(CardMovedEvent x) =>
        x.ToZone == ZoneType.Battlefield && IsFaceDown(x.Card);

    // CR 406.3 — a card put into exile face down.
    private static bool IntoFaceDownExile(CardMovedEvent x) =>
        x.ToZone == ZoneType.Exile && StateSnapshotter.IsExiledFaceDown(x.Card);

    private static object TargetObject(Majik.Core.Targeting.ITarget t)
        => t is Majik.Core.Targeting.Target { TargetObject: { } o } ? o : t;

    /// <summary>The trigger's text as the order-triggers prompt shows it: its
    /// effects' descriptions, else "&lt;source&gt; trigger".</summary>
    internal static string TriggerText(ITriggeredAbility trigger)
    {
        var sourceName = (trigger.Source as ICard)?.Name ?? "";
        var effectText = (trigger as TriggeredAbility)?.Effects
            .Select(e => e.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();
        return effectText is { Count: > 0 }
            ? string.Join(" ", effectText)
            : (sourceName.Length > 0 ? $"{sourceName} trigger" : "triggered ability");
    }

    private static JsonElement BuildTriggered(TriggeredAbilityTriggeredEvent x, Player? viewer)
    {
        var source = x.Ability.Source as ICard;
        // CR 706 — a trigger from a card in a hidden zone (a library, another
        // player's hand) tells that viewer only that something triggered.
        if (source != null && StateSnapshotter.IsHiddenFrom(source, viewer))
        {
            return SerializeCard(new TriggeredAbilityTriggeredPayload(
                AbilityId: x.Ability.Id,
                ControllerId: x.Ability.Controller.Id,
                Hidden: true));
        }
        bool nameHidden = DisplayName(source, viewer) == FaceDownName && IsFaceDown(source);
        return SerializeCard(new TriggeredAbilityTriggeredPayload(
            AbilityId: x.Ability.Id,
            ControllerId: x.Ability.Controller.Id,
            SourceId: source?.InstanceId,
            SourceName: DisplayName(source, viewer),
            // The text of a face-down permanent's ability would name it.
            Text: nameHidden ? "triggered ability" : TriggerText(x.Ability)));
    }

    private static JsonElement BuildSurveil(SurveilEvent x, Player? viewer)
    {
        // CR 701.42a — cards put into the graveyard are public; the rest stay
        // on top of the library, hidden from everyone but the surveilling
        // player (who looked at them).
        var toGraveyard = x.Cards.Where(c => c.Zone != ZoneType.Library)
            .Select(c => new CardRefPayload(c.InstanceId, c.Name)).ToList();
        var toTop = x.Cards.Where(c => c.Zone == ZoneType.Library).ToList();
        bool seesTop = viewer == null || ReferenceEquals(viewer, x.Player) || viewer.Id == x.Player.Id;
        return SerializeCard(new SurveilPayload(
            PlayerId: x.Player.Id,
            N: x.N,
            ToGraveyard: toGraveyard,
            ToTopCount: toTop.Count,
            ToTop: seesTop ? toTop.Select(c => new CardRefPayload(c.InstanceId, c.Name)).ToList() : null));
    }

    private static JsonElement BuildCardMoved(CardMovedEvent x, Player? viewer, Majik.Core.Stack.Stack? stack)
    {
        var ownerId = x.Card.Owner?.Id;
        bool mask = ShouldMaskCardForViewer(viewer, ownerId, x.FromZone, x.ToZone)
            // CR 406.3 / 706 — from face-down exile into a hidden zone: a
            // non-owner never saw the card and still may not.
            || (x.LeftFaceDownExile && IsHiddenZone(x.ToZone)
                && viewer != null && (!ownerId.HasValue || viewer.Id != ownerId.Value));

        if (mask)
        {
            // CR 706 — masked variant MUST stay exactly {ownerId, from, to,
            // hidden}. No enrichment ever crosses the masking boundary; an
            // opponent must not learn the card's identity or its permanent
            // fields from a Hand→Library (etc.) move. Constructing the
            // record with only these fields populated + NullOmittingOpts
            // drops every other (null) property so the wire stays exactly
            // four keys.
            return SerializeCard(new CardMovedPayload(
                From: x.FromZone.ToString(),
                To: x.ToZone.ToString(),
                OwnerId: ownerId,
                Hidden: true));
        }

        // PLAN 04 — REVEALED branch only. Enrich with the same permanent
        // fields a snapshot carries (shared StateSnapshotter.BuildPermanentFields,
        // so snapshot + event agree) so the portal reducer can apply an ETB in
        // place instead of forcing a full GET /state. A → Battlefield move
        // always touches a public zone, so it is already the revealed branch —
        // the enrichment never appears on a masked variant.
        if (IntoFaceDownExile(x))
        {
            // CR 406.3 — the face-down exile shape the snapshot gives this
            // viewer: a looker sees the card, everyone else only that it is
            // a face-down card.
            var looks = ((Card)x.Card).MayLookAtFaceDownExile(viewer);
            if (!looks)
            {
                return SerializeCard(new CardMovedPayload(
                    From: x.FromZone.ToString(),
                    To: x.ToZone.ToString(),
                    OwnerId: ownerId,
                    CardId: x.Card.InstanceId,
                    CardName: FaceDownExileName,
                    IsFaceDown: true));
            }
            return SerializeCard(new CardMovedPayload(
                From: x.FromZone.ToString(),
                To: x.ToZone.ToString(),
                OwnerId: ownerId,
                CardId: x.Card.InstanceId,
                CardName: x.Card.Name,
                ManaCost: x.Card.ManaCost,
                Types: x.Card.CardTypes.Select(t => t.ToString()).ToList(),
                IsFaceDown: true));
        }

        if (EntersFaceDown(x))
        {
            // CR 708.2 — the same face-down view the snapshot gives this viewer.
            var fd = StateSnapshotter.SnapshotCard(x.Card, viewer);
            return SerializeCard(new CardMovedPayload(
                From: x.FromZone.ToString(),
                To: x.ToZone.ToString(),
                OwnerId: ownerId,
                CardId: fd.InstanceId,
                CardName: fd.Name,
                ManaCost: fd.ManaCost,
                Types: fd.Types,
                Power: fd.Power,
                Toughness: fd.Toughness,
                Tapped: fd.Tapped,
                SummoningSickness: fd.SummoningSickness,
                Abilities: fd.Abilities,
                ProducedManaColors: fd.ProducedManaColors,
                Counters: fd.Counters,
                IsFaceDown: true));
        }

        var f = StateSnapshotter.BuildPermanentFields(x.Card);
        return SerializeCard(new CardMovedPayload(
            From: x.FromZone.ToString(),
            To: x.ToZone.ToString(),
            OwnerId: ownerId,
            CardId: x.Card.InstanceId,
            CardName: x.Card.Name,
            ManaCost: x.Card.ManaCost,
            Types: x.Card.CardTypes.Select(t => t.ToString()).ToList(),
            Power: f.Power,
            Toughness: f.Toughness,
            Tapped: f.Tapped,
            SummoningSickness: f.SummoningSickness,
            Abilities: f.Abilities,
            ProducedManaColors: f.ProducedManaColors,
            Counters: f.Counters,
            StackEntryId: x.ToZone == ZoneType.Stack ? StackEntryOf(x.Card, stack) : null));
    }

    /// <summary>CR 601.2a — the stack entry a card moved to the stack became: the
    /// cast in progress (whose id the spell keeps), else the pushed spell.</summary>
    private static Guid? StackEntryOf(ICard card, Majik.Core.Stack.Stack? stack)
    {
        if (stack is null) return null;
        if (stack.BeingCast.LastOrDefault(c => ReferenceEquals(c.Card, card)) is { } cast) return cast.Id;
        foreach (var (obj, id) in stack.GetAll().Zip(stack.GetEntryIds()))
            if (obj is ISpell s && ReferenceEquals(s.Card, card)) return id;
        return null;
    }

    private static JsonElement BuildCardDrawn(CardDrawnEvent x, Player? viewer)
    {
        var ownerId = x.Player.Id;
        // Library → Hand is always hidden→hidden. Owner sees the card;
        // opponent gets count-only.
        bool mask = viewer != null && viewer.Id != ownerId;

        if (mask)
        {
            return SerializeCard(new CardDrawnPayload(
                PlayerId: ownerId,
                Hidden: true));
        }

        return SerializeCard(new CardDrawnPayload(
            PlayerId: ownerId,
            CardId: x.Card.InstanceId,
            CardName: x.Card.Name,
            ManaCost: x.Card.ManaCost,
            Types: x.Card.CardTypes.Select(t => t.ToString()).ToList()));
    }

    private static bool ShouldMaskCardForViewer(Player? viewer, Guid? ownerId, ZoneType from, ZoneType to)
    {
        // Spectator / full-reveal view: never mask.
        if (viewer == null) return false;
        // Owner sees their own private zones.
        if (ownerId.HasValue && viewer.Id == ownerId.Value) return false;
        // Non-owner: mask only when BOTH zones are hidden information
        // (Hand or Library). Any transition that touches a public zone
        // reveals the card identity — it was already visible to the
        // opponent at the time of the move.
        return BothZonesHidden(from, to);
    }

    private static bool BothZonesHidden(ZoneType a, ZoneType b)
        => IsHiddenZone(a) && IsHiddenZone(b);

    private static bool IsHiddenZone(ZoneType z) =>
        z == ZoneType.Library || z == ZoneType.Hand;

    private static JsonElement Serialize<T>(T value)
        => JsonSerializer.SerializeToElement(value, Opts);

    /// <summary>Serialize a dual-shape card payload
    /// (<see cref="CardMovedPayload"/> / <see cref="CardDrawnPayload"/>)
    /// dropping null properties so the masked + revealed variants collapse
    /// to their exact historical key sets (CR 706).</summary>
    private static JsonElement SerializeCard<T>(T value)
        => JsonSerializer.SerializeToElement(value, NullOmittingOpts);

    /// <summary>Serialize a <see cref="StackObjectPayload"/> dropping null
    /// card-identity fields so StackObjectAdded / Resolved keep their lean
    /// {stackId, controllerId, kind, description} shape while SpellCast
    /// additionally carries the populated cardId / cardName.</summary>
    private static JsonElement SerializeStack(StackObjectPayload value)
        => JsonSerializer.SerializeToElement(value, NullOmittingOpts);

    private static JsonElement Empty() => JsonDocument.Parse("{}").RootElement;

    // The next two helpers must stay aligned with
    // StateSnapshotter.SnapshotStackObject — the wire-format `kind` +
    // `description` strings emitted on the stack DTO are the same the
    // event payload promises, so the frontend can treat
    // StackObjectAddedEvent as "append this StackItem" verbatim.
    private static string StackKind(IStackObject obj) => obj switch
    {
        ISpell => "Spell",
        ITriggeredAbility => "TriggeredAbility",
        IActivatedAbility => "ActivatedAbility",
        _ => obj.GetType().Name,
    };

    private static string StackDescription(IStackObject obj, Player? viewer) => obj switch
    {
        ISpell spell => spell.Card?.Name ?? "",
        ITriggeredAbility t => DisplayName(t.Source as ICard, viewer) + " trigger",
        IActivatedAbility => "ability",
        _ => obj.GetType().Name,
    };
}
