using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// Which top-of-library cards a "play from the top of your library" grant
/// (CR 601.3e / CR 305.6) lets its controller play.
/// </summary>
public enum TopPlayFilter
{
    /// <summary>Lands only — Courser of Kruphix, Oracle of Mul Daya, Augur of
    /// Autumn's land clause (CR 305.6).</summary>
    Lands,

    /// <summary>Creature cards only — Augur of Autumn's Coven clause,
    /// Vivien (Champion of the Wilds)-style creature top-play.</summary>
    Creatures,

    /// <summary>Artifact cards only — Mystic Forge's "you may cast artifact
    /// spells … from the top of your library" clause. A CAST filter (the card
    /// goes onto the stack via <see cref="Majik.Core.Game.SpellCastFlow"/>),
    /// distinct from the play-as-a-land filters above.</summary>
    Artifacts,

    /// <summary>Colorless cards only — Mystic Forge's "… and colorless spells"
    /// clause (CR 105.2c — a card with no colors). A CAST filter.</summary>
    Colorless,

    /// <summary>Any card — Bolas's Citadel / Vizier-of-the-Menagerie-adjacent
    /// "play the top card" effects. Covers both the land-play and the
    /// nonland-cast capabilities.</summary>
    Any,
}

/// <summary>
/// CR 601.3e / CR 305.6 / CR 715.4 — process-level, per-game registry for the
/// "you may play [filter] from the top of your library" continuous permission
/// (plus the informational "play with the top card revealed" rider).
///
/// <para>
/// A battlefield static (Courser of Kruphix, Augur of Autumn, Oracle of Mul
/// Daya, …) registers a grant keyed by its source token while it is on the
/// battlefield and removes it on leave (CR 603.6e — a static functions only
/// while its source is on the battlefield). Each grant carries the controller
/// it benefits and a <see cref="TopPlayFilter"/> describing which top-of-library
/// cards become legal play sources.
/// </para>
///
/// <para>
/// CR 601.3e: an effect may allow a player to play a card "as though it were in
/// their hand" — the card is still played from its current zone (the library),
/// it still consumes the normal land drop (CR 305.2) and respects any
/// additional-land static, and only the top card is ever eligible. The engine's
/// land-play path (<see cref="Majik.Core.Game.PriorityLoop"/> →
/// <see cref="Majik.Core.Services.ZoneService.MoveCardToAsync"/>) already plays
/// a land from whatever zone the card occupies, so this registry is the
/// permission/visibility surface the agent + validators consult to know the top
/// library card is a legal play source — and the bus-aware lifecycle that
/// registers/revokes it as the source enters/leaves.
/// </para>
///
/// <para>
/// Reveal-top (CR 715.4) is tracked by the presence of any grant whose source
/// also reveals the top card — Courser / Oracle / Augur all play with the top
/// card revealed. <see cref="IsTopRevealed"/> exposes that for the bot / UI
/// surface (the top card is public information).
/// </para>
///
/// Grants stack across multiple sources (a player with both Courser and Augur
/// has two land grants — idempotent for the same filter). A card is playable
/// from the top iff it is currently the top of the controller's library AND at
/// least one registered grant for that controller matches its type.
///
/// Tests that mutate the registry should call <see cref="Clear"/> in a
/// fixture / dispose path to avoid leakage across cases.
/// </summary>
public static class LibraryTopPlayPermissions
{
    /// <summary>Per-game store: the token-keyed grant list and its lock.</summary>
    public sealed class Store
    {
        // Each entry: (token, controller, filter, revealsTop, extraPredicate,
        // topCastAltCostFactory).
        // extraPredicate (nullable) ANDs an extra per-card restriction on top of
        // the type filter — e.g. Conspicuous Snoop's "Goblin card" subtype gate
        // on its Creatures grant. Null means "no extra restriction".
        // topCastAltCostFactory (nullable) — when set, a spell cast under THIS
        // grant must be cast using the produced alternative cost INSTEAD of its
        // printed mana cost (CR 118.9). Bolas's Citadel: "pay life equal to its
        // mana value rather than pay its mana cost." Null = cast with the
        // printed cost (Mystic Forge / Augur Coven / Conspicuous Snoop).
        internal readonly List<(object Token, Player Controller, TopPlayFilter Filter, bool RevealsTop, Func<ICard, bool>? ExtraPredicate, Func<IAlternativeCost>? TopCastAltCostFactory)> Grants = new();
        internal readonly object Gate = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>Install a fresh per-game store. See <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>
    /// Register a "may play [<paramref name="filter"/>] from the top of your
    /// library" grant on <paramref name="controller"/>, keyed by
    /// <paramref name="token"/> (the source permanent). Idempotent for the same
    /// (token, controller, filter) — re-registering does not add a duplicate.
    /// When <paramref name="revealsTop"/> is true the source also plays with the
    /// top card revealed (CR 715.4).
    /// <para>
    /// <paramref name="extraPredicate"/> (optional) ANDs an additional per-card
    /// restriction on top of the type filter, used by the cast-side matcher
    /// (<see cref="MayCastTopCard"/>): Conspicuous Snoop's "you may cast Goblin
    /// spells from the top of your library" is a <see cref="TopPlayFilter.Creatures"/>
    /// grant whose predicate also demands the card be a Goblin. Null means "no
    /// extra restriction".
    /// </para>
    /// </summary>
    public static void AddGrant(
        object token, Player controller, TopPlayFilter filter, bool revealsTop = true,
        Func<ICard, bool>? extraPredicate = null,
        Func<IAlternativeCost>? topCastAltCostFactory = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(controller);
        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (ReferenceEquals(g.Token, token)
                    && ReferenceEquals(g.Controller, controller)
                    && g.Filter == filter)
                {
                    return;
                }
            }
            store.Grants.Add((token, controller, filter, revealsTop, extraPredicate, topCastAltCostFactory));
        }
    }

    /// <summary>
    /// Remove every grant registered under <paramref name="token"/>. Used when
    /// the source permanent leaves the battlefield. Idempotent.
    /// </summary>
    public static void RemoveGrant(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var store = Current;
        lock (store.Gate)
        {
            store.Grants.RemoveAll(g => ReferenceEquals(g.Token, token));
        }
    }

    /// <summary>
    /// True if <paramref name="controller"/> currently has at least one grant
    /// whose filter would let them play a card of the given type-set from the
    /// top of their library — independent of whether such a card is actually on
    /// top right now. Use <see cref="MayPlayTopCard"/> for the live, on-top
    /// check.
    /// </summary>
    public static bool HasGrant(Player controller, TopPlayFilter filter)
    {
        if (controller == null) return false;
        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (ReferenceEquals(g.Controller, controller) && Covers(g.Filter, filter))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// True if <paramref name="card"/> is currently the top card of
    /// <paramref name="controller"/>'s library AND a registered grant for that
    /// controller covers its card type — i.e. the controller may play it from
    /// the top this turn (CR 601.3e). Lands additionally still cost the land
    /// drop, enforced by <see cref="Majik.Core.Game.LandDropTracker"/> on the
    /// play path; this method only answers the zone-source-legality half.
    /// </summary>
    public static bool MayPlayTopCard(Player controller, ICard card)
    {
        if (controller == null || card == null) return false;
        if (!ReferenceEquals(TopOfLibrary(controller), card)) return false;

        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (!ReferenceEquals(g.Controller, controller)) continue;
                if (Matches(g.Filter, card)
                    && (g.ExtraPredicate == null || g.ExtraPredicate(card)))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// The top card of <paramref name="controller"/>'s library if they currently
    /// have an active "play lands from the top" grant AND that top card is a
    /// land — i.e. the land they may play from the top this turn. Returns null
    /// otherwise. Convenience for the agent's land-drop enumeration.
    /// </summary>
    public static ICard? PlayableLandFromTop(Player controller)
    {
        if (controller == null) return null;
        var top = TopOfLibrary(controller);
        if (top == null) return null;
        return MayPlayTopCard(controller, top) && top.HasType(CardType.Land) ? top : null;
    }

    /// <summary>
    /// CR 601.3e — true if <paramref name="card"/> is currently the top card of
    /// <paramref name="controller"/>'s library AND a registered grant lets that
    /// controller <b>cast</b> it from the top (Mystic Forge: artifact / colorless
    /// spells; Bolas's Citadel: any nonland spell). This is the CAST analogue of
    /// <see cref="MayPlayTopCard"/>, which only answers the land-PLAY half.
    ///
    /// <para>
    /// Lands are never "cast" (CR 601.1 — a land is <i>played</i>, not cast), so
    /// a land on top is never castable here even under an <see
    /// cref="TopPlayFilter.Any"/> grant — use <see cref="MayPlayTopCard"/> /
    /// <see cref="PlayableLandFromTop"/> for the land-play half. The spell-cast
    /// path itself (<see cref="Majik.Core.Game.SpellCastFlow"/>) already moves a
    /// card from whatever zone it occupies onto the stack and stamps the
    /// "cast from library" sentinel; this method is the zone-source-legality
    /// surface the agent / validators consult to know the top card is a legal
    /// cast source.
    /// </para>
    /// </summary>
    public static bool MayCastTopCard(Player controller, ICard card)
    {
        if (controller == null || card == null) return false;
        // CR 601.1 — lands are played, not cast.
        if (card.HasType(CardType.Land)) return false;
        if (!ReferenceEquals(TopOfLibrary(controller), card)) return false;

        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (!ReferenceEquals(g.Controller, controller)) continue;
                if (MatchesCast(g.Filter, card)
                    && (g.ExtraPredicate == null || g.ExtraPredicate(card)))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// CR 118.9 — if <paramref name="card"/> is castable from the top of
    /// <paramref name="controller"/>'s library under a grant that REQUIRES an
    /// alternative cost (Bolas's Citadel: "pay life equal to its mana value
    /// rather than pay its mana cost"), produce a fresh instance of that
    /// alternative cost. Returns null when the card is castable with its printed
    /// cost (Mystic Forge / Augur Coven / Conspicuous Snoop carry no alt-cost
    /// factory) or when no covering grant matches.
    ///
    /// <para>
    /// Used by the cast enumeration (<see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>)
    /// + <see cref="Majik.Core.Game.TurnDriver"/> to attach the mandatory
    /// pay-life alt cost when routing a Bolas's Citadel top-cast through
    /// <see cref="Majik.Core.Game.SpellCastFlow"/>. When several grants cover the
    /// card, the FIRST grant carrying an alt-cost factory wins (a Bolas grant +
    /// a Mystic-Forge grant on the same card: Bolas's pay-life requirement is
    /// the binding one — CR 118.9 alt costs are exclusive).
    /// </para>
    /// </summary>
    public static IAlternativeCost? MandatoryTopCastAltCostFor(Player controller, ICard card)
    {
        if (controller == null || card == null) return null;
        if (card.HasType(CardType.Land)) return null;
        if (!ReferenceEquals(TopOfLibrary(controller), card)) return null;

        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (!ReferenceEquals(g.Controller, controller)) continue;
                if (g.TopCastAltCostFactory == null) continue;
                if (MatchesCast(g.Filter, card)
                    && (g.ExtraPredicate == null || g.ExtraPredicate(card)))
                {
                    return g.TopCastAltCostFactory();
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The top card of <paramref name="controller"/>'s library if they currently
    /// have an active cast-from-top grant whose filter covers it (and it is a
    /// nonland spell) — i.e. the spell they may cast from the top. Returns null
    /// otherwise. Convenience for the agent's cast enumeration (parallels
    /// <see cref="PlayableLandFromTop"/> on the play side).
    /// </summary>
    public static ICard? CastableSpellFromTop(Player controller)
    {
        if (controller == null) return null;
        var top = TopOfLibrary(controller);
        if (top == null) return null;
        return MayCastTopCard(controller, top) ? top : null;
    }

    /// <summary>
    /// True if any active grant for <paramref name="controller"/> reveals the
    /// top card of their library (CR 715.4). When true the top card is public
    /// information (bot / UI may surface it).
    /// </summary>
    public static bool IsTopRevealed(Player controller)
    {
        if (controller == null) return false;
        var store = Current;
        lock (store.Gate)
        {
            foreach (var g in store.Grants)
            {
                if (ReferenceEquals(g.Controller, controller) && g.RevealsTop) return true;
            }
            return false;
        }
    }

    /// <summary>Reset the active store. Test-only.</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Gate) store.Grants.Clear();
    }

    private static ICard? TopOfLibrary(Player controller) =>
        controller.Zones.Library.GetCards().FirstOrDefault();

    // A grant with `granted` filter covers a `wanted` capability when the
    // granted filter is at least as permissive: Any covers everything; a
    // specific filter covers only its own kind.
    private static bool Covers(TopPlayFilter granted, TopPlayFilter wanted) =>
        granted == TopPlayFilter.Any || granted == wanted;

    private static bool Matches(TopPlayFilter filter, ICard card) => filter switch
    {
        TopPlayFilter.Lands => card.HasType(CardType.Land),
        TopPlayFilter.Creatures => card.HasType(CardType.Creature),
        TopPlayFilter.Any => true,
        // Artifacts / Colorless are cast-only filters — they never make a card
        // PLAYABLE-as-a-land (MayPlayTopCard); they only authorize casting
        // (MayCastTopCard). A play-side query against them never matches.
        _ => false,
    };

    // CR 601.3e cast-side matcher (MayCastTopCard). Distinct from Matches: an
    // Any grant casts any NONLAND spell (the land-vs-cast split is enforced by
    // MayCastTopCard's CR 601.1 land guard before this is reached). The
    // Artifacts / Colorless filters key off card type / colour (CR 105.2c —
    // a card with no colours is colourless).
    private static bool MatchesCast(TopPlayFilter filter, ICard card) => filter switch
    {
        TopPlayFilter.Artifacts => card.HasType(CardType.Artifact),
        TopPlayFilter.Colorless => Cards.CardColors.GetColors(card).Count == 0,
        // Creature cards ARE cast (CR 601.1, unlike lands) — the Coven clause
        // (Augur of Autumn) + Conspicuous Snoop's Goblin-creature grant cast a
        // creature from the top. The extra predicate (set on the grant) narrows
        // it further (e.g. "Goblin card" for Snoop).
        TopPlayFilter.Creatures => card.HasType(CardType.Creature),
        TopPlayFilter.Any => true,
        // The land/creature PLAY filters do not, on their own, authorize a CAST
        // from the top — Courser's "play lands" clause never let you cast a
        // creature. (A creature top-play grant is a PLAY permission for the
        // Coven clause, not a cast permission.)
        _ => false,
    };
}
