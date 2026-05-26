using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Narset, Parter of Veils (War of the Spark,
/// {1}{U}{U}).
///
/// Legendary Planeswalker — Narset, starting loyalty 5.
/// Oracle text:
///   "Each opponent can't draw more than one card each turn.
///    -2: Look at the top four cards of your library. You may reveal a
///        noncreature, nonland card from among them and put it into your
///        hand. Put the rest on the bottom of your library in a random
///        order."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 5, Narset subtype, mana cost
///   {1}{U}{U}.
/// - <b>Printed static</b> (CR 117.1a): "Each opponent can't draw more
///   than one card each turn." Wired via
///   <see cref="NarsetDrawRestrictionReplacement"/> registered on each
///   opponent's <see cref="ReplacementBus"/> while Narset is on the
///   battlefield. The replacement tracks how many times that opponent
///   has drawn this turn — the first <see cref="DrawCardIntent"/> per
///   turn is let through unchanged, subsequent draw intents are cancelled
///   (CR 614 — replacement returns null). Reset is driven by
///   <see cref="TurnStartedEvent"/> on the supplied <see cref="IEventBus"/>.
/// - <b>-2: top-four peek + grab noncreature/nonland</b>: looks at top 4
///   of controller's library, picks the first noncreature/nonland (auto-
///   pick deterministic — same v1 shape as Karn / Liliana), routes it to
///   the controller's hand, and shuffles the remainder before placing on
///   the bottom in shuffled order (CR 701.19 — "random order"). Uses
///   <see cref="GameRandomRegistry"/> for the shuffle.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven reveal choice</b>: -2 picks the first eligible card
///   rather than prompting; "may reveal" is auto-accepted whenever a
///   candidate exists. Mirrors every other Phase-1 PW factory.
/// - <b>Draw-watcher coverage</b>: the static gates on
///   <see cref="DrawCardIntent"/> — any draw path that bypasses
///   <see cref="ReplacementBus"/> also bypasses Narset (same gap as
///   Dredge / Sheoldred). Production draw paths route through
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/>, which DOES route
///   through the bus when one is attached.
/// </summary>
[CardName("Narset, Parter of Veils")]
public static class NarsetParterOfVeilsFactory
{
    public const string CardName = "Narset, Parter of Veils";
    public const string PrintedManaCost = "{1}{U}{U}";
    public const int StartingLoyalty = 5;
    public const int LookCount = 4;
    public const int MaxDrawsPerTurnForOpponents = 1;

    /// <summary>
    /// Construct Narset, Parter of Veils with no resolvers wired. Shape /
    /// dispatcher posture — the printed static and -2 body silently
    /// skip; loyalty change still applies (CR 606.3).
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null);

    /// <summary>
    /// Construct Narset, Parter of Veils with the printed-static
    /// lifecycle wired against <paramref name="eventBus"/> and per-
    /// opponent <see cref="ReplacementBus"/> registration via
    /// <paramref name="opponentResolver"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">Returns the set of players treated
    /// as opponents at restriction-sync time. Each opponent must have a
    /// non-null <see cref="Player.Replacements"/> bus for the restriction
    /// to take effect; opponents without a bus are silently skipped.
    /// Called when Narset enters the battlefield. May be null —
    /// restriction simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking of the
    /// printed static and for per-turn draw-counter reset. May be null —
    /// Attach will still sync once but per-turn reset relies on the bus.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var narset = new Planeswalker(
            name: CardName,
            manaCost: PrintedManaCost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Narset });

        narset.SetOwner(owner);
        narset.SetController(owner);

        // -- Printed static (CR 117.1a) — "Each opponent can't draw more
        //    than one card each turn." -----------------------------------
        if (opponentResolver != null)
        {
            var lifecycle = new NarsetDrawRestrictionEffect(
                source: narset,
                eventBus: eventBus,
                opponentResolver: opponentResolver);
            lifecycle.Attach();
        }

        // -- -2: peek top 4, grab a noncreature/nonland, rest to bottom
        //        in random order. -----------------------------------------
        narset.AddAbility(new LoyaltyAbility(narset, -2, () =>
        {
            // Snapshot the top N (or fewer if the library is smaller).
            var top = owner.Zones.Library.GetCards().Take(LookCount).ToList();
            if (top.Count == 0) return;

            // First noncreature/nonland — controller may reveal + put
            // into hand (v1 auto-accept).
            ICard? picked = null;
            foreach (var c in top)
            {
                if (IsEligibleReveal(c))
                {
                    picked = c;
                    break;
                }
            }

            if (picked != null)
            {
                owner.Zones.Library.RemoveCard(picked);
                owner.Zones.Hand.AddCard(picked);
                picked.SetZone(ZoneType.Hand);
                top.Remove(picked);
            }

            // Remainder — shuffle into a random order, then move each to
            // the bottom of the library in that order.
            if (top.Count > 0)
            {
                var rng = GameRandomRegistry.Get(owner);
                rng.Shuffle(top);
                foreach (var c in top)
                {
                    owner.Zones.Library.RemoveCard(c);
                    owner.Zones.Library.AddCard(c); // AddCard appends to bottom
                    c.SetZone(ZoneType.Library);
                }
            }
        }));

        return narset;
    }

    private static bool IsEligibleReveal(ICard c)
    {
        // "Noncreature, nonland" — exclude both card types regardless of
        // any other type (e.g. an artifact-creature is excluded by the
        // creature half).
        if (c.HasType(CardType.Creature)) return false;
        if (c.HasType(CardType.Land)) return false;
        return true;
    }
}

/// <summary>
/// Lifecycle binder for Narset, Parter of Veils' printed static —
/// "Each opponent can't draw more than one card each turn."
///
/// While Narset is on the battlefield, registers a
/// <see cref="NarsetDrawRestrictionReplacement"/> on each opponent's
/// <see cref="ReplacementBus"/>. The replacement tracks draws-this-turn
/// and cancels every <see cref="DrawCardIntent"/> beyond the first per
/// turn per opponent.
///
/// Per-turn reset is driven by <see cref="TurnStartedEvent"/> on the
/// supplied event bus. LTB unregisters every opponent registration.
/// </summary>
public sealed class NarsetDrawRestrictionEffect
{
    private readonly ICard _source;
    private readonly IEventBus? _eventBus;
    private readonly Func<IReadOnlyList<Player>> _opponentResolver;
    private readonly Dictionary<Guid, (Player Player, NarsetDrawRestrictionReplacement Replacement)> _registered = new();
    private bool _attached;
    private bool _currentlyActive;

    public NarsetDrawRestrictionEffect(
        ICard source,
        IEventBus? eventBus,
        Func<IReadOnlyList<Player>> opponentResolver)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eventBus = eventBus;
        _opponentResolver = opponentResolver ?? throw new ArgumentNullException(nameof(opponentResolver));
    }

    /// <summary>Register the restriction on every opponent's bus if Narset
    /// is on the battlefield. Subscribes to zone-change + turn-start events
    /// for lifecycle tracking. Idempotent.</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;

        if (_eventBus != null)
        {
            _eventBus.Subscribe<CardMovedEvent>(OnCardMoved);
            _eventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }
        SyncRegistration();
    }

    private void OnCardMoved(CardMovedEvent e)
    {
        if (!ReferenceEquals(e.Card, _source)) return;
        SyncRegistration();
    }

    private void OnTurnStarted(TurnStartedEvent _)
    {
        // Reset every registered opponent's draws-this-turn counter.
        foreach (var (_, entry) in _registered)
        {
            entry.Replacement.ResetDrawCount();
        }
    }

    private void SyncRegistration()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;

        if (shouldBeActive && !_currentlyActive)
        {
            var opponents = _opponentResolver();
            if (opponents != null)
            {
                foreach (var opp in opponents)
                {
                    if (opp is null) continue;
                    if (opp.Replacements is null) continue;
                    if (_registered.ContainsKey(opp.Id)) continue;

                    var replacement = new NarsetDrawRestrictionReplacement(opp);
                    opp.Replacements.Register(replacement);
                    _registered[opp.Id] = (opp, replacement);
                }
            }
            _currentlyActive = true;
        }
        else if (!shouldBeActive && _currentlyActive)
        {
            foreach (var (_, entry) in _registered)
            {
                entry.Player.Replacements?.Unregister(entry.Replacement);
            }
            _registered.Clear();
            _currentlyActive = false;
        }
    }

    /// <summary>True while the restriction is registered against any
    /// opponent's bus.</summary>
    public bool IsRestrictionActive => _currentlyActive;
}

/// <summary>
/// Replacement effect for "Each opponent can't draw more than one card
/// each turn." Self-counts draws via this opponent's bus: the first
/// <see cref="DrawCardIntent"/> per turn passes through, subsequent
/// ones return null (cancel). <see cref="ResetDrawCount"/> is called
/// at turn-start by the owning lifecycle effect.
/// </summary>
public sealed class NarsetDrawRestrictionReplacement : IReplacementEffect<DrawCardIntent>
{
    private readonly Player _affected;
    private int _drawsThisTurn;

    public NarsetDrawRestrictionReplacement(Player affected)
    {
        _affected = affected ?? throw new ArgumentNullException(nameof(affected));
    }

    public bool OneShot => false;
    public object? Tag => null;

    public bool Applies(DrawCardIntent intent, IReadOnlyList<object> history)
    {
        if (intent is null) return false;
        if (!ReferenceEquals(intent.Player, _affected)) return false;
        return true;
    }

    public DrawCardIntent? Replace(DrawCardIntent intent, IReadOnlyList<object> history)
    {
        // First draw this turn passes through unchanged; the bus's
        // history dedup ensures we run at most once per intent (Tag is
        // null → use effect identity), but we'd see multiple intents
        // across a single "draw N" loop. Use _drawsThisTurn.
        if (_drawsThisTurn < NarsetParterOfVeilsFactory.MaxDrawsPerTurnForOpponents)
        {
            _drawsThisTurn++;
            return intent;
        }
        // Beyond the cap — cancel (CR 614 — replacement returns null).
        _drawsThisTurn++;
        return null;
    }

    /// <summary>Reset the per-turn counter. Called by the owning
    /// lifecycle effect on every <see cref="TurnStartedEvent"/>.</summary>
    public void ResetDrawCount() => _drawsThisTurn = 0;

    /// <summary>Inspect the per-turn counter (for tests).</summary>
    public int DrawsThisTurn => _drawsThisTurn;
}
