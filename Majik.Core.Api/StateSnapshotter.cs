using Majik.Core.Abilities;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
        PhaseStateType? phase,
        Player activePlayer,
        IReadOnlyList<Player> players,
        Majik.Core.Stack.Stack stack,
        Player? viewer = null,
        TurnStateType? turnState = null,
        // PLAN 04 — the per-game seq of the last event folded into this
        // snapshot. Threaded onto GameStateDto.Seq so the portal can drop
        // stale snapshots. Defaults to 0 for callers that don't track seq.
        long seq = 0)
    {
        if (players == null) throw new ArgumentNullException(nameof(players));
        if (activePlayer == null) throw new ArgumentNullException(nameof(activePlayer));
        if (stack == null) throw new ArgumentNullException(nameof(stack));

        return new GameStateDto(
            GameId: gameId,
            TurnNumber: turnNumber,
            Phase: PhaseLabelResolver.Resolve(phase, turnState),
            ActivePlayerId: activePlayer.Id,
            Players: players.Select(p => SnapshotPlayer(p, viewer)).ToList(),
            Stack: stack.GetAll().Select(SnapshotStackObject).ToList(),
            YouPlayerId: viewer?.Id,
            Seq: seq);
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
            Hand: hideHidden ? HiddenZone(p.Zones.Hand) : SnapshotZone(p.Zones.Hand),
            Battlefield: SnapshotZone(p.Zones.Battlefield),
            Graveyard: SnapshotZone(p.Zones.Graveyard),
            Library: HiddenZone(p.Zones.Library),       // always hidden, even to owner
            Exile: SnapshotZone(p.Zones.Exile));
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

    private static ZoneDto SnapshotZone(IZone zone) =>
        new(zone.GetCards().Select(SnapshotCard).ToList());

    /// <summary>
    /// Single-card snapshot shared with prompt envelopes (e.g.
    /// <see cref="Majik.Core.Api.Dtos.PromptDto.Candidates"/> for
    /// library-search picks) so the wire shape is identical to what the
    /// portal already renders for in-zone cards. Internal — call sites
    /// outside this assembly must use the zone-level <see cref="Snapshot"/>
    /// entry point.
    /// </summary>
    internal static CardSnapshotDto SnapshotCard(ICard card)
    {
        var f = BuildPermanentFields(card);
        return new CardSnapshotDto(
            InstanceId: card.InstanceId,
            Name: card.Name,
            ManaCost: card.ManaCost,
            Types: card.CardTypes.Select(t => t.ToString()).ToList(),
            Power: f.Power,
            Toughness: f.Toughness,
            Tapped: f.Tapped,
            SummoningSickness: f.SummoningSickness,
            Abilities: f.Abilities,
            ProducedManaColors: f.ProducedManaColors,
            Counters: f.Counters);
    }

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
    internal static PermanentFields BuildPermanentFields(ICard card)
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

        IReadOnlyDictionary<string, int> counters = EmptyCounters;
        if (card is Permanent perm)
        {
            tapped = perm.IsTapped;
            summoningSickness = perm.HasSummoningSickness;
            counters = SnapshotCounters(perm);
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
        if (all.Count == 0) return EmptyCounters;
        var map = new Dictionary<string, int>(all.Count);
        foreach (var kv in all)
        {
            if (kv.Value > 0) map[kv.Key.Name] = kv.Value;
        }
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
        var sb = new System.Text.StringBuilder(6);
        if (w) sb.Append('W');
        if (u) sb.Append('U');
        if (b) sb.Append('B');
        if (r) sb.Append('R');
        if (g) sb.Append('G');
        if (c) sb.Append('C');
        return sb.ToString();
    }

    private static AbilityDto SnapshotAbility(IAbility ability) => ability switch
    {
        IActivatedAbility a => new AbilityDto("Activated", a.GetType().Name, a.Id),
        ITriggeredAbility => new AbilityDto("Triggered", "triggered ability"),
        IStaticAbility => new AbilityDto("Static", "static ability"),
        _ => new AbilityDto(ability.GetType().Name, ability.ToString() ?? ""),
    };

    private static StackObjectDto SnapshotStackObject(IStackObject obj) => obj switch
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
            Description: (t.Source as ICard)?.Name + " trigger"),
        IActivatedAbility a => new StackObjectDto(
            Id: a.Id,
            Kind: "ActivatedAbility",
            ControllerId: a.Controller.Id,
            // Surface a human-readable description matching the triggered-
            // ability case above: "<source card name>: <first effect text>".
            // Falls back to the source name alone (then to "ability") when
            // the wrapper carries no effects, so a half-built stub still
            // produces something more useful than the generic "ability"
            // label clients saw before.
            Description: BuildActivatedAbilityDescription(a)),
        _ => new StackObjectDto(obj.Id, obj.GetType().Name, null, obj.GetType().Name),
    };

    private static string BuildActivatedAbilityDescription(IActivatedAbility a)
    {
        var sourceName = (a.Source as ICard)?.Name;
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
        return sourceName ?? "ability";
    }
}
