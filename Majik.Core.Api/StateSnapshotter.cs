using Majik.Core.Abilities;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Api;

/// <summary>
/// Pure transform from live engine state to <see cref="GameStateDto"/>.
/// Holds no state — safe to call on every "get state" request.
/// </summary>
public static class StateSnapshotter
{
    public static GameStateDto Snapshot(
        Guid gameId,
        int turnNumber,
        StepStateType? phase,
        Player activePlayer,
        IReadOnlyList<Player> players,
        Majik.Core.Stack.Stack stack,
        Player? viewer = null,
        PhaseStateType? turnState = null,
        // PLAN 04 — the per-game seq of the last event folded into this
        // snapshot. Threaded onto GameStateDto.Seq so the portal can drop
        // stale snapshots. Defaults to 0 for callers that don't track seq.
        long seq = 0,
        // CR 117.1 — who holds priority (null = nobody).
        Player? priorityPlayer = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(activePlayer);
        ArgumentNullException.ThrowIfNull(stack);

        var entryIds = EntryIdsByObject(stack);

        return new GameStateDto(
            GameId: gameId,
            TurnNumber: turnNumber,
            Phase: PhaseLabelResolver.Resolve(phase, turnState),
            ActivePlayerId: activePlayer.Id,
            Players: players.Select(p => SnapshotPlayer(p, viewer)).ToList(),
            // CR 603.3 — one entry per stack object, keyed by its entry id (a
            // repeat firing of one triggered ability gets its own id).
            Stack: stack.GetAll().Zip(stack.GetEntryIds(),
                (o, id) => SnapshotStackObject(o, viewer, entryIds) with { Id = id })
                // CR 601.2a — a spell being cast is on the stack (top) while
                // its caster chooses targets / modes / X / costs, before the
                // engine pushes the Spell object.
                .Concat(stack.BeingCast.Select(c => SnapshotBeingCast(c, viewer, entryIds)))
                .ToList(),
            YouPlayerId: viewer?.Id,
            Seq: seq,
            PriorityPlayerId: priorityPlayer?.Id);
    }

    private static PlayerDto SnapshotPlayer(Player p, Player? viewer)
    {
        // CR 706 — opponent hand + library are hidden information.
        // Viewer == p (or null = spectator-all-revealed) sees everything.
        var hideHidden = viewer != null && !ReferenceEquals(p, viewer);
        return new PlayerDto(
            Id: p.Id,
            Name: p.Name,
            Life: p.LifeTotal,
            HasLost: p.HasLost,
            Mana: SnapshotMana(p.ManaPool),
            Hand: hideHidden ? HiddenZone(p.Zones.Hand) : SnapshotZone(p.Zones.Hand, viewer),
            Battlefield: SnapshotZone(p.Zones.Battlefield, viewer),
            Graveyard: SnapshotZone(p.Zones.Graveyard, viewer),
            Library: HiddenZone(p.Zones.Library),       // always hidden, even to owner
            Exile: SnapshotZone(p.Zones.Exile, viewer));
    }

    /// <summary>Zone whose contents are hidden — DTO carries only the count.</summary>
    private static ZoneDto HiddenZone(IZone zone)
    {
        var n = zone.GetCards().Count();
        var placeholders = Enumerable.Range(0, n).Select(_ => new CardSnapshotDto(
            InstanceId: Guid.Empty,
            Name: "(hidden)",
            ManaCost: "",
            Types: System.Array.Empty<string>(),
            Power: null,
            Toughness: null,
            Tapped: false,
            SummoningSickness: false,
            Abilities: System.Array.Empty<AbilityDto>())).ToList();
        return new ZoneDto(placeholders);
    }

    private static ManaPoolDto SnapshotMana(ManaPool pool) => new(
        Generic: pool.Generic,
        White: pool.White,
        Blue: pool.Blue,
        Black: pool.Black,
        Red: pool.Red,
        Green: pool.Green,
        Colorless: 0);

    private static ZoneDto SnapshotZone(IZone zone, Player? viewer) =>
        new(zone.GetCards().Select(c => SnapshotCard(c, viewer)).ToList());

    // ── CR 708 face-down permanents ──────────────────────────────────────

    /// <summary>CR 708.2 — the name a face-down permanent is shown with to a
    /// player who may not look at it (it has no name). Shared with the event
    /// payloads (EventPayloadBuilder.FaceDownName).</summary>
    internal const string FaceDownName = "Face-down permanent";

    /// <summary>CR 406.3 — the name a card exiled face down is shown with to a
    /// player the exiling effect does not let look at it.</summary>
    internal const string FaceDownExileName = "Face-down card";

    /// <summary>CR 406.3 — a card in exile face down (Bomat Courier, Vivien,
    /// Champion of the Wilds, Necropotence).</summary>
    internal static bool IsExiledFaceDown(ICard? card) => card is Card { IsExiledFaceDown: true };

    // CR 708.2 / 708.9 — only a permanent is face down: one that left the
    // battlefield was revealed, and its card is face up in its new zone.
    internal static bool IsFaceDown(ICard? card) => card is Permanent { IsFaceDown: true, Zone: ZoneType.Battlefield };

    /// <summary>CR 708.6 — the controller of a face-down permanent may look at
    /// it; a null viewer is the all-revealed (spectator / debug) view.</summary>
    internal static bool MayLookAt(ICard card, Player? viewer) =>
        viewer is null || (card is Permanent p && ReferenceEquals(p.Controller ?? p.Owner, viewer));

    /// <summary>The card a face-down permanent is: a manifested / cloaked
    /// permanent is a wrapper around the card it represents (CR 708.2c).</summary>
    internal static ICard RepresentedCard(ICard card) => card is ManifestedCreature m ? m.UnderlyingCard : card;

    /// <summary>The name to show <paramref name="viewer"/> for <paramref name="card"/>:
    /// its name, or for a face-down permanent the represented card's name to
    /// whoever may look at it and <see cref="FaceDownName"/> to everyone else.</summary>
    internal static string DisplayName(ICard? card, Player? viewer)
    {
        if (card is null) return "";
        if (!IsFaceDown(card)) return card.Name;
        return MayLookAt(card, viewer) ? RepresentedCard(card).Name : FaceDownName;
    }

    /// <summary>Snapshot of <paramref name="card"/> as <paramref name="viewer"/>
    /// may see it (CR 708 face-down masking; null = all revealed).</summary>
    internal static CardSnapshotDto SnapshotCard(ICard card, Player? viewer) =>
        SnapshotCard(card, includeImprints: true, viewer);

    /// <summary>
    /// Single-card snapshot shared with prompt envelopes (e.g.
    /// <see cref="Majik.Core.Api.Dtos.PromptDto.Candidates"/> for
    /// library-search picks) so the wire shape is identical to what the
    /// portal already renders for in-zone cards. Internal — call sites
    /// outside this assembly must use the zone-level <see cref="Snapshot"/>
    /// entry point.
    /// </summary>
    internal static CardSnapshotDto SnapshotCard(ICard card) =>
        SnapshotCard(card, includeImprints: true, viewer: null);

    /// <param name="includeImprints">When true, populate
    /// <see cref="CardSnapshotDto.ImprintedCards"/> from
    /// <see cref="Permanent.ImprintedCards"/> (CR 702.49) — the cards exiled
    /// "with" this permanent, so a client renders them UNDER it. False on the
    /// nested pass so the snapshot stays SHALLOW (an imprinted card never
    /// carries its own imprints).</param>
    private static CardSnapshotDto SnapshotCard(ICard card, bool includeImprints, Player? viewer)
    {
        // CR 406.3 — same shape as a face-down permanent (CR 708.2): the object
        // has no characteristics; IsFaceDown; the real card (name + FaceDownCard)
        // only for a player the exiling effect lets look at it.
        if (card is Card { IsExiledFaceDown: true } exiledFaceDown)
        {
            var looks = exiledFaceDown.MayLookAtFaceDownExile(viewer);
            return new CardSnapshotDto(
                InstanceId: card.InstanceId,
                Name: looks ? card.Name : FaceDownExileName,
                ManaCost: "",
                Types: System.Array.Empty<string>(),
                Power: null,
                Toughness: null,
                Tapped: false,
                SummoningSickness: false,
                Abilities: System.Array.Empty<AbilityDto>(),
                IsFaceDown: true,
                FaceDownCard: looks ? SnapshotFaceUp(card, includeImprints: false, viewer) : null);
        }
        return SnapshotFaceUp(card, includeImprints, viewer);
    }

    private static CardSnapshotDto SnapshotFaceUp(ICard card, bool includeImprints, Player? viewer)
    {
        var f = BuildPermanentFields(card);

        // CR 702.49 — imprinted cards are exiled face up: visible to all.
        IReadOnlyList<CardSnapshotDto> imprinted = System.Array.Empty<CardSnapshotDto>();
        if (includeImprints && card is Permanent perm && perm.ImprintedCards.Count > 0)
        {
            imprinted = perm.ImprintedCards
                .Select(c => SnapshotCard(c, includeImprints: false, viewer))
                .ToList();
        }

        if (card is Permanent { IsFaceDown: true, Zone: ZoneType.Battlefield } faceDown)
            return SnapshotFaceDown(faceDown, f, imprinted, viewer);

        return new CardSnapshotDto(
            InstanceId: card.InstanceId,
            Name: card.Name,
            ManaCost: WireMana.Cost(card.ManaCost),
            Types: EffectiveTypes(card).Select(t => t.ToString()).ToList(),
            Power: f.Power,
            Toughness: f.Toughness,
            Tapped: f.Tapped,
            SummoningSickness: f.SummoningSickness,
            Abilities: f.Abilities,
            ProducedManaColors: f.ProducedManaColors,
            Counters: f.Counters,
            ImprintedCards: imprinted);
    }

    /// <summary>
    /// CR 708.2 — what a face-down permanent is: a 2/2 creature with no name,
    /// text, subtypes, colour or mana cost. Every viewer gets the permanent's
    /// own state (id, its computed P/T, tapped, summoning sickness, counters,
    /// attachments) and <see cref="CardSnapshotDto.IsFaceDown"/>. Whoever may
    /// look at it (CR 708.6: its controller; the spectator view) also gets the
    /// represented card's name, the permanent's real abilities (so it can be
    /// turned face up) and the card itself in <see cref="CardSnapshotDto.FaceDownCard"/>.
    /// Anyone else sees only the abilities its face-down state grants
    /// (e.g. cloak's ward {2}, CR 702.168a), never "turn face up" (which would
    /// tell them it is a creature card).
    /// </summary>
    private static CardSnapshotDto SnapshotFaceDown(
        Permanent perm, PermanentFields f, IReadOnlyList<CardSnapshotDto> imprinted, Player? viewer)
    {
        var looks = MayLookAt(perm, viewer);
        var represented = RepresentedCard(perm);
        return new CardSnapshotDto(
            InstanceId: perm.InstanceId,
            Name: looks ? represented.Name : FaceDownName,
            ManaCost: "",
            Types: new[] { nameof(CardType.Creature) },
            Power: f.Power ?? 2,
            Toughness: f.Toughness ?? 2,
            Tapped: f.Tapped,
            SummoningSickness: f.SummoningSickness,
            Abilities: looks
                ? f.Abilities
                : perm.EffectiveAbilities.Where(a => a is not FaceDownActivatedAbility).Select(SnapshotAbility).ToList(),
            ProducedManaColors: "",
            Counters: f.Counters,
            ImprintedCards: imprinted,
            IsFaceDown: true,
            FaceDownCard: looks && !ReferenceEquals(represented, perm)
                ? SnapshotCard(represented, includeImprints: false, viewer: null)
                : null);
    }

    /// <summary>
    /// CR 613.1c / 613.7b — the card's EFFECTIVE (layer-computed) card types,
    /// not its printed list. An animated land / manland (Earthbend'd Forest,
    /// Creeping Tar Pit, Karn-animated artifact, …) becomes a creature via a
    /// Layer-4 type-changing continuous effect, but its printed
    /// <see cref="ICard.CardTypes"/> still says only "Land"/"Artifact". The
    /// portal buckets a card to the creature row iff its <c>types</c> contains
    /// "Creature", so the snapshot must serialize the computed types or the
    /// animated land stays stuck in the land row and can never be shown as an
    /// attacker.
    ///
    /// This mirrors exactly what <see cref="Permanent.IsEffectivelyCreature"/>
    /// consults: for a <see cref="Permanent"/> with a non-null
    /// <see cref="Permanent.ActiveEffects"/> it returns
    /// <c>ActiveEffects.Compute(perm).Types</c>; otherwise (non-permanent, or a
    /// permanent whose layer service is not wired up — e.g. a cloned game) it
    /// falls back to the printed <see cref="ICard.CardTypes"/>. Type-changing
    /// effects only apply on the battlefield (the only zone where
    /// continuous effects function), so this battlefield-scoped computed path is
    /// both correct and minimal — non-battlefield cards have a null
    /// <c>ActiveEffects</c> and take the printed-types fallback unchanged.
    /// </summary>
    private static IEnumerable<CardType> EffectiveTypes(ICard card) =>
        card is Permanent { ActiveEffects: { } fx } perm
            ? fx.Compute(perm).Types
            : card.CardTypes;

    /// <summary>
    /// PLAN 04 — the live permanent/creature fields a snapshot and an
    /// enriched event payload must agree on. Shared by
    /// <see cref="SnapshotCard"/> and
    /// <c>EventPayloadBuilder.BuildCardMoved</c> (REVEALED branch only) so the
    /// reducer can patch an ETB in place instead of forcing a full GET /state.
    /// P/T are null for non-creatures; tapped / summoning-sickness false for
    /// non-permanents; <see cref="PermanentFields.Counters"/> is an empty map
    /// when the card carries no counters.
    /// </summary>
    /// <param name="zone">The zone the card is shown in (a move event passes
    /// its destination); defaults to the card's current zone. Counters are
    /// emitted only for the battlefield.</param>
    internal static PermanentFields BuildPermanentFields(ICard card, ZoneType? zone = null)
    {
        int? power = null;
        int? toughness = null;
        bool tapped = false;
        bool summoningSickness = false;

        if (card is Creature c)
        {
            power = c.Power;
            toughness = c.Toughness;
        }
        // CR 613.1c / 613.7b — an animated land / manland is NOT a Creature
        // runtime instance (it stays a Land/Artifact), so the branch above
        // misses its P/T. When such a permanent is effectively a creature via a
        // Layer-4 type-changing continuous effect, Compute(perm) yields a
        // CreatureCharacteristics carrying the animated body's P/T (the same
        // CreatureCharacteristics the combat / SBA paths read). Surface it so
        // the snapshot reports e.g. an Earthbend'd land as a 1/1, not a P/T-less
        // land. Real Creatures keep the c.Power/c.Toughness path above (which
        // already consults the layer system).
        else if (card is Permanent { ActiveEffects: { } fx } animated
                 && fx.Compute(animated) is CreatureCharacteristics cc)
        {
            power = cc.Power;
            toughness = cc.Toughness;
        }

        IReadOnlyDictionary<string, int> counters = EmptyCounters;
        if (card is Permanent perm)
        {
            tapped = perm.IsTapped;
            summoningSickness = perm.HasSummoningSickness;
            // CR 122.2 / 400.7 — counters are on permanents. A card that left
            // the battlefield is a new object with none; the engine keeps the
            // old values on it only for last-known information. No other zone
            // carries engine-placed counters in a live game (suspend's time
            // counters live in SuspendedCardRegistry, which no live game wires).
            if ((zone ?? perm.Zone) == ZoneType.Battlefield) counters = SnapshotCounters(perm);
        }

        return new PermanentFields(
            Power: power,
            Toughness: toughness,
            Tapped: tapped,
            SummoningSickness: summoningSickness,
            Abilities: card.Abilities.Select(SnapshotAbility).ToList(),
            ProducedManaColors: ComputeProducedManaColors(card),
            Counters: counters);
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyCounters =
        new Dictionary<string, int>();

    /// <summary>Project <see cref="Permanent.Counters"/> into a
    /// name→count map keyed by <see cref="Majik.Core.Counters.CounterType.Name"/>.
    /// Zero / negative buckets are dropped (CounterCollection already prunes
    /// to zero on removal, but defended against here too).</summary>
    private static IReadOnlyDictionary<string, int> SnapshotCounters(Permanent perm)
    {
        var all = perm.Counters.All;

        // CR 306.5b — a planeswalker's loyalty lives in a dedicated field
        // (Planeswalker.Loyalty / Permanent.GetEffectiveLoyalty), NOT in the
        // generic Counters collection. The portal renders a "Loyalty" pip from
        // this counters map, so project the effective loyalty into it under the
        // "Loyalty" key. Positive loyalty only (a 0-loyalty walker is about to
        // die to the SBA; a negative value is impossible). This is purely a
        // client surface — the authoritative loyalty stays on the field.
        var effectiveLoyalty = perm.IsEffectivePlaneswalker()
            ? perm.GetEffectiveLoyalty()
            : null;

        if (all.Count == 0 && effectiveLoyalty is not > 0) return EmptyCounters;

        var map = new Dictionary<string, int>(all.Count + 1);
        foreach (var kv in all)
        {
            if (kv.Value > 0) map[kv.Key.Name] = kv.Value;
        }
        if (effectiveLoyalty is int loyalty && loyalty > 0) map["Loyalty"] = loyalty;
        return map;
    }

    /// <summary>Shared permanent-field bundle — see
    /// <see cref="BuildPermanentFields"/>.</summary>
    internal readonly record struct PermanentFields(
        int? Power,
        int? Toughness,
        bool Tapped,
        bool SummoningSickness,
        IReadOnlyList<AbilityDto> Abilities,
        string ProducedManaColors,
        IReadOnlyDictionary<string, int> Counters);

    /// <summary>
    /// CR 605 — derive the WUBRG/C colour string from the card's actual
    /// <see cref="IManaAbility"/> instances so the client can render a
    /// "tap for mana" affordance without round-tripping oracle text.
    /// Order is fixed WUBRG then C. Hybrid / generic / X / Snow are
    /// excluded from v1; only the five colours plus pure {C} are emitted.
    /// </summary>
    private static string ComputeProducedManaColors(ICard card)
    {
        var w = false; var u = false; var b = false; var r = false; var g = false; var c = false;
        foreach (var ma in card.Abilities.OfType<IManaAbility>())
        {
            var mc = ma.ManaGenerated;
            if (mc == null) continue;
            if (mc.White > 0) w = true;
            if (mc.Blue > 0) u = true;
            if (mc.Black > 0) b = true;
            if (mc.Red > 0) r = true;
            if (mc.Green > 0) g = true;
            // {C} is parsed into Generic with no colour pips set.
            if (mc.Generic > 0 && mc.White == 0 && mc.Blue == 0
                && mc.Black == 0 && mc.Red == 0 && mc.Green == 0)
            {
                c = true;
            }
        }
        return string.Concat(
            w ? "W" : "",
            u ? "U" : "",
            b ? "B" : "",
            r ? "R" : "",
            g ? "G" : "",
            c ? "C" : "");
    }

    private static AbilityDto SnapshotAbility(IAbility ability) => ability switch
    {
        IActivatedAbility a => new AbilityDto("Activated", a.GetType().Name, a.Id),
        // CR 606 — loyalty abilities are their own shape (not IActivatedAbility).
        // Surface them with a distinct "Loyalty" kind, the signed cost as the
        // Description ("+1" / "-2" / "-5"), and the ability's stable Id so the
        // portal can list each one, render the loyalty-cost sign/amount, gate on
        // affordability, and echo the Id back in ActivateLoyaltyAbilityCommand.
        LoyaltyAbility la => new AbilityDto("Loyalty", la.Description, la.Id),
        ITriggeredAbility => new AbilityDto("Triggered", "triggered ability"),
        IStaticAbility => new AbilityDto("Static", "static ability"),
        _ => new AbilityDto(ability.GetType().Name, ability.ToString() ?? ""),
    };

    /// <summary>CR 603.3 — every stack entry id of each object on the stack,
    /// bottom to top (an ability that triggered twice has two).</summary>
    private static IReadOnlyDictionary<IStackObject, List<Guid>> EntryIdsByObject(Majik.Core.Stack.Stack stack)
    {
        var map = new Dictionary<IStackObject, List<Guid>>(ReferenceEqualityComparer.Instance);
        foreach (var (obj, id) in stack.GetAll().Zip(stack.GetEntryIds()))
        {
            if (!map.TryGetValue(obj, out var ids)) map[obj] = ids = new List<Guid>(1);
            ids.Add(id);
        }
        return map;
    }

    private static StackObjectDto SnapshotStackObject(IStackObject obj, Player? viewer,
        IReadOnlyDictionary<IStackObject, List<Guid>>? entryIds) =>
        SnapshotStackObjectCore(obj, viewer) with
        {
            Targets = SnapshotTargets(obj, viewer, entryIds),
            SourceCardId = SourceCardId(obj switch
            {
                ISpell s => s.Card,
                ITriggeredAbility t => t.Source as ICard,
                IActivatedAbility a => a.Source as ICard,
                _ => null,
            }, viewer),
            RulesText = StackRulesText(obj, viewer),
            AbilityId = obj switch
            {
                ITriggeredAbility t => t.Id,
                ActivatedAbility a => a.ActivatedFromId ?? a.Id,
                IActivatedAbility a => a.Id,
                _ => null,
            },
        };

    /// <summary>C20 — the ability's own text (see StackObjectDto.RulesText),
    /// null for a spell and when the source is hidden from the viewer.</summary>
    internal static string? StackRulesText(IStackObject obj, Player? viewer)
    {
        var source = obj switch
        {
            ITriggeredAbility t => t.Source,
            IActivatedAbility a => a.Source,
            _ => null,
        };
        if (obj is ISpell) return null;
        if (source is ICard c && (IsHiddenFrom(c, viewer) || HidesFaceDownSource(c, viewer))) return null;
        return obj switch
        {
            ITriggeredAbility t => EventPayloadBuilder.TriggerText(t),
            IActivatedAbility a => ActivatedText(a),
            _ => null,
        };
    }

    private static string ActivatedText(IActivatedAbility a)
    {
        var costs = string.Join(", ", a.Costs
            .Select(c => c is Majik.Core.Costs.ManaCostCost m ? WireMana.Amount(m.Cost) : c.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d)));
        var effects = string.Join(" ", ((a as ActivatedAbility)?.Effects ?? Array.Empty<Majik.Core.Abilities.IEffect>())
            .Select(e => e.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d)));
        return costs.Length > 0 ? $"{costs}: {effects}" : effects;
    }

    /// <summary>The source card's InstanceId (kept across zone moves), or null
    /// when there is none or it is in a zone hidden from <paramref name="viewer"/>
    /// (CR 706): the id would let that player track the hidden card.</summary>
    private static Guid? SourceCardId(ICard? card, Player? viewer) =>
        card is null || IsHiddenFrom(card, viewer) ? null : card.InstanceId;

    // Same shape as a pushed spell (SnapshotStackObjectCore), flagged.
    private static StackObjectDto SnapshotBeingCast(Majik.Core.Stack.CastInProgress cast, Player? viewer,
        IReadOnlyDictionary<IStackObject, List<Guid>>? entryIds) =>
        new(Id: cast.Id,
            Kind: "Spell",
            ControllerId: cast.Caster.Id,
            Description: cast.Card.Name,
            Targets: TargetRefs(cast.Targets.SelectMany(s => s), viewer, entryIds),
            IsBeingCast: true,
            SourceCardId: SourceCardId(cast.Card, viewer));

    // CR 601.2c / 603.3d — the object's chosen targets in announcement order.
    private static IReadOnlyList<TargetRefDto> SnapshotTargets(IStackObject obj, Player? viewer,
        IReadOnlyDictionary<IStackObject, List<Guid>>? entryIds)
    {
        IEnumerable<object> raw = obj switch
        {
            Majik.Core.Spells.Spell { AnnouncedTargets: { } slots } => slots.SelectMany(s => s),
            Majik.Core.Spells.Spell s => s.ChosenTargets,
            TriggeredAbility t => t.ChosenTargets.SelectMany(s => s),
            ActivatedAbility a => a.ChosenTargets.SelectMany(s => s),
            _ => Array.Empty<object>(),
        };
        return TargetRefs(raw, viewer, entryIds);
    }

    /// <param name="entryIds">The stack's entry ids per object. A stack-object
    /// target is referenced by its entry id(s) (StackObjectDto.Id), one ref per
    /// firing when it is on the stack more than once: the engine's target is the
    /// shared object, and a counter removes every firing of it
    /// (OracleSpellBinder.RemoveFromStack). Null, or an object not on the stack:
    /// its own id (the entry id of a first push).</param>
    internal static IReadOnlyList<TargetRefDto> TargetRefs(IEnumerable<object> raw, Player? viewer,
        IReadOnlyDictionary<IStackObject, List<Guid>>? entryIds = null)
    {
        var refs = new List<TargetRefDto>();
        foreach (var target in raw)
        {
            switch (target)
            {
                case Player p:
                    refs.Add(new TargetRefDto(p.Id, "Player"));
                    break;
                // CR 115.1d — a spell, or an activated or triggered ability on
                // the stack (Stifle's target). Same "Spell" kind: a stack-object ref.
                case IStackObject so:
                    if (entryIds != null && entryIds.TryGetValue(so, out var ids))
                        foreach (var id in ids) refs.Add(new TargetRefDto(id, "Spell"));
                    else
                        refs.Add(new TargetRefDto(so.Id, "Spell"));
                    break;
                case ICard c:
                    refs.Add(new TargetRefDto(IsHiddenFrom(c, viewer) ? Guid.Empty : c.InstanceId, "Card"));
                    break;
            }
        }
        return refs;
    }

    // CR 706 — mirrors SnapshotPlayer: a library is hidden from everyone, a
    // hand from everyone but its owner; a null viewer is the all-revealed view.
    internal static bool IsHiddenFrom(ICard card, Player? viewer)
    {
        if (viewer is null) return false;
        return card.Zone switch
        {
            ZoneType.Library => true,
            ZoneType.Hand => !ReferenceEquals(card.Owner, viewer),
            // CR 406.3 — exiled face down, and this viewer may not look at it.
            ZoneType.Exile => card is Card c && !c.MayLookAtFaceDownExile(viewer),
            _ => false,
        };
    }

    private static StackObjectDto SnapshotStackObjectCore(IStackObject obj, Player? viewer) => obj switch
    {
        ISpell spell => new StackObjectDto(
            Id: spell.Id,
            Kind: "Spell",
            ControllerId: spell.Controller.Id,
            Description: spell.Card.Name),
        ITriggeredAbility t => new StackObjectDto(
            Id: t.Id,
            Kind: "TriggeredAbility",
            ControllerId: t.Controller.Id,
            Description: DisplayName(t.Source as ICard, viewer) + " trigger"),
        IActivatedAbility a => new StackObjectDto(
            Id: a.Id,
            Kind: "ActivatedAbility",
            ControllerId: a.Controller.Id,
            // Surface a human-readable description matching the triggered-
            // ability case above: "<source card name>: <first effect text>".
            // Falls back to the source name alone (then to "ability") when
            // the wrapper carries no effects, so a half-built stub still
            // produces something more useful than the generic "ability"
            // label clients saw before. CR 708.2 — an ability of a face-down
            // permanent is not described to a player who may not look at it
            // (its text could name the card, e.g. "Turn face up: restore X").
            Description: HidesFaceDownSource(a.Source, viewer)
                ? FaceDownName + " ability"
                : BuildActivatedAbilityDescription(a, viewer)),
        _ => new StackObjectDto(obj.Id, obj.GetType().Name, null, obj.GetType().Name),
    };

    /// <summary>True when <paramref name="source"/> is a face-down permanent
    /// that <paramref name="viewer"/> may not look at (CR 708.2 / 708.6).</summary>
    internal static bool HidesFaceDownSource(object? source, Player? viewer) =>
        source is ICard c && IsFaceDown(c) && !MayLookAt(c, viewer);

    internal static string BuildActivatedAbilityDescription(IActivatedAbility a, Player? viewer)
    {
        var sourceName = a.Source is ICard src ? DisplayName(src, viewer) : null;
        // IActivatedAbility does not expose Effects on the interface; the
        // concrete ActivatedAbility does. Cast and read the first effect's
        // description when present.
        var firstEffect = (a as ActivatedAbility)?.Effects?.FirstOrDefault()?.Description;
        if (!string.IsNullOrWhiteSpace(firstEffect))
        {
            // Effect descriptions like FetchLandCycleFactory's "Windswept
            // Heath: search library for Forest or Plains, put onto
            // battlefield" already lead with the card name; avoid stuttering
            // by using the effect text directly when it starts with the
            // source's name, otherwise prepend "Name: ".
            if (!string.IsNullOrWhiteSpace(sourceName) &&
                !firstEffect.StartsWith(sourceName!, StringComparison.OrdinalIgnoreCase))
            {
                return $"{sourceName}: {firstEffect}";
            }
            return firstEffect!;
        }
        return sourceName is { Length: > 0 } ? $"{sourceName} ability" : "ability";
    }
}
