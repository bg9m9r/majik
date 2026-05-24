using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dauthi Voidwalker (Modern Horizons 2, {1}{B}).
///
/// Creature — Dauthi Rogue 3/2. Oracle text:
///   "Shadow (This creature can block or be blocked by only creatures
///    with shadow.)
///    If a card would be put into an opponent's graveyard from anywhere,
///    exile it with a void counter on it instead.
///    {2}, {T}, Remove a void counter from a card exiled with Dauthi
///    Voidwalker: You may play that card this turn without paying its
///    mana cost."
///
/// ## Implemented (v1)
/// - 3/2 Creature — Dauthi Rogue at {1}{B}.
/// - Shadow keyword (<see cref="KeywordAbility"/> marker; full combat
///   semantics handled by <see cref="Majik.Core.Combat.CombatAbilities"/>
///   when present, otherwise consumers read the marker directly).
/// - <b>Replacement effect — opponent-graveyard → exile-with-void-counter</b>
///   is wired against <see cref="ReplacementBus"/> when the bus-aware
///   overload is used. The replacement watches every
///   <see cref="ZoneMoveIntent"/> whose destination is
///   <see cref="ZoneType.Graveyard"/> and whose card's owner is an
///   opponent of Voidwalker's controller; it rewrites the destination to
///   <see cref="ZoneType.Exile"/> and stamps a void counter on the card
///   via the factory's per-Voidwalker registry. Per the oracle ("from
///   anywhere"), it applies regardless of the source zone — battlefield,
///   hand, library, stack, command, exile-to-grave bounces all funnel
///   through the same <see cref="Services.ZoneService"/> path.
/// - <b>Activated ability — {2}, {T}, Remove a void counter from a card
///   exiled with Dauthi Voidwalker</b>. v1 auto-picks the first card
///   currently carrying a void counter under this Voidwalker. The effect
///   removes one void counter from the chosen exiled card; callers (or
///   the supplied <c>onCounterRemoved</c> sink, when wired) then cast it
///   for free via the <see cref="CastFromExileAlternativeCost"/>
///   produced by <see cref="BuildAlternativeCost"/>. CR 605 — not a mana
///   ability, so the ability goes on the stack.
///
/// ## Cast-for-free plumbing
/// The activated ability does NOT itself put the exiled card on the
/// stack — it only removes the counter, mirroring how Lurrus's static
/// ability and Snapcaster's ETB grant funnel into the existing
/// alternative-cost machinery. Callers cast the chosen exile-resident
/// card via <see cref="Game.SpellCastFlow.CastAsync"/> with a
/// <see cref="CastFromExileAlternativeCost"/> of <see cref="ManaCost.Zero"/>
/// (the "without paying its mana cost" clause — CR 118.9). The
/// alt-cost's <see cref="CastFromExileAlternativeCost.CanCastFor"/>
/// already checks zone + ownership invariants.
///
/// ## Deferred (v1 gaps)
/// - <b>"This turn" timing</b>: the cast-for-free permission only lasts
///   "this turn" per the oracle (CR 117 / CR 118.9). v1 does not enforce
///   a per-turn expiry on the cast permission itself — the void counter
///   simply records that the card is in Voidwalker's exile pile;
///   removing it is the ability's payoff but does not stamp a
///   timer. Wiring an EOT cleanup mirrors the Snapcaster bus-handler
///   pattern (<see cref="Events.IEventBus"/> + <see cref="StateMachine.PhaseStateType"/>.Cleanup)
///   and is deferred until a cast-permission flag is added.
/// - <b>Target selection on the activated ability</b>: oracle wording
///   ("Remove a void counter from a card exiled with Dauthi Voidwalker")
///   is a cost, not a target, so technically no real targeting prompt is
///   needed — but a player choice IS required at activation. v1
///   auto-picks the first such card deterministically. Wiring an agent
///   prompt mirrors the rest of the v1 factories (deferred).
/// - <b>Voidwalker leaves the battlefield</b>: the replacement effect is
///   not auto-unregistered when Voidwalker leaves the battlefield. The
///   bus-aware overload exposes the produced
///   <see cref="VoidwalkerExileReplacement"/> via the returned tuple so
///   the caller can <see cref="ReplacementBus.Unregister{TIntent}"/> it
///   on the Voidwalker's leave-battlefield event.
/// </summary>
[CardName("Dauthi Voidwalker")]
public static class DauthiVoidwalkerFactory
{
    public const string CardName = "Dauthi Voidwalker";

    /// <summary>
    /// Per-Voidwalker state: which exile-resident cards currently carry a
    /// void counter stamped by this Voidwalker. Keyed off the Voidwalker
    /// card instance via a <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
    /// so multiple Voidwalkers in the same game keep separate piles.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Card, VoidwalkerState>
        _state = new();

    /// <summary>
    /// Retrieve the <see cref="VoidwalkerState"/> attached to a Voidwalker
    /// instance produced by this factory. Returns null when the card was
    /// not built by this factory.
    /// </summary>
    public static VoidwalkerState? GetState(Card voidwalker)
    {
        if (voidwalker == null) throw new ArgumentNullException(nameof(voidwalker));
        return _state.TryGetValue(voidwalker, out var s) ? s : null;
    }

    /// <summary>
    /// Construct Dauthi Voidwalker with no replacement-bus wiring. The
    /// Shadow keyword + activated ability are wired against the
    /// per-instance state, but the opponent-graveyard replacement does
    /// not fire because nothing is consulting the bus. Suitable for shape
    /// tests / dispatcher use.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, replacements: null).Card;

    /// <summary>
    /// Construct Dauthi Voidwalker with an optional <see cref="ReplacementBus"/>.
    /// When the bus is supplied, the opponent-graveyard replacement is
    /// registered on it and surfaced on the returned tuple for the caller
    /// to <see cref="ReplacementBus.Unregister{TIntent}"/> when Voidwalker
    /// leaves the battlefield (v1 — automatic leave-cleanup deferred).
    /// </summary>
    public static (Creature Card, VoidwalkerExileReplacement? Replacement) Create(
        Player owner,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{1}{B}",
            power: 3,
            toughness: 2,
            subtypes: new[] { CardSubtype.Dauthi, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.27 — Shadow. Combat ability: can block / be blocked only
        // by creatures with Shadow.
        card.AddAbility(new KeywordAbility("Shadow", card, owner));

        var state = new VoidwalkerState(card);
        _state.AddOrUpdate(card, state);

        // ----------------------------------------------------------------
        // Replacement effect — opponent-graveyard → exile w/ void counter.
        // CR 614 — funnel through ZoneMoveIntent on the ReplacementBus.
        // ----------------------------------------------------------------
        VoidwalkerExileReplacement? replacement = null;
        if (replacements != null)
        {
            replacement = new VoidwalkerExileReplacement(card, state);
            replacements.Register<ZoneMoveIntent>(replacement);
        }

        // ----------------------------------------------------------------
        // {2}, {T}, Remove a void counter from a card exiled with Dauthi
        // Voidwalker: You may play that card this turn without paying its
        // mana cost.
        //
        // CR 605 — not a mana ability. Effect removes one void counter
        // from the auto-picked exiled card; callers feed the same card
        // into SpellCastFlow with the CastFromExileAlternativeCost
        // returned by BuildAlternativeCost.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            "Dauthi Voidwalker: remove a void counter from an exiled card "
            + "(caller casts it for free via CastFromExileAlternativeCost)",
            () =>
            {
                var target = state.FirstVoidCounteredCard();
                if (target == null) return; // No void-counter exile pile — no-op.
                state.RemoveVoidCounter(target);
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(card),
                new RemoveVoidCounterCost(state),
            },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activated);

        return (card, replacement);
    }

    /// <summary>
    /// Convenience builder for the cast-from-exile alt cost used to "play
    /// that card this turn without paying its mana cost" (CR 118.9). The
    /// cost is <see cref="ManaCost.Zero"/> — the spell is cast for free.
    /// </summary>
    public static CastFromExileAlternativeCost BuildAlternativeCost(ICard exiledCard)
    {
        if (exiledCard == null) throw new ArgumentNullException(nameof(exiledCard));
        return new CastFromExileAlternativeCost(
            description: $"Dauthi Voidwalker — play {exiledCard.Name} from exile without paying its mana cost",
            cost: ManaCost.Zero);
    }
}

/// <summary>
/// Per-Voidwalker runtime state. Tracks which exile-resident cards
/// currently carry a void counter stamped by this Voidwalker, so the
/// activated ability can locate and decrement them.
/// </summary>
public sealed class VoidwalkerState
{
    private readonly Card _voidwalker;
    private readonly HashSet<ICard> _exiledWithVoidCounter = new(ReferenceEqualityComparer.Instance);

    public VoidwalkerState(Card voidwalker)
    {
        _voidwalker = voidwalker ?? throw new ArgumentNullException(nameof(voidwalker));
    }

    /// <summary>The Voidwalker instance this state belongs to.</summary>
    public Card Voidwalker => _voidwalker;

    /// <summary>Snapshot of cards currently exiled with a void counter
    /// under this Voidwalker. Stable iteration order matters for the v1
    /// "auto-pick first" semantics of the activated ability.</summary>
    public IEnumerable<ICard> VoidCounteredCards => _exiledWithVoidCounter;

    /// <summary>Number of cards in this Voidwalker's void-counter pile.</summary>
    public int VoidCounterCount => _exiledWithVoidCounter.Count;

    /// <summary>True if <paramref name="card"/> currently has a void
    /// counter stamped by this Voidwalker.</summary>
    public bool HasVoidCounter(ICard card) =>
        card != null && _exiledWithVoidCounter.Contains(card);

    /// <summary>Mark <paramref name="card"/> as exiled with a void counter
    /// under this Voidwalker. Idempotent.</summary>
    public void AddVoidCounter(ICard card)
    {
        if (card == null) return;
        _exiledWithVoidCounter.Add(card);
    }

    /// <summary>Remove the void counter from <paramref name="card"/>.
    /// Returns true if a counter was actually removed.</summary>
    public bool RemoveVoidCounter(ICard card)
    {
        if (card == null) return false;
        return _exiledWithVoidCounter.Remove(card);
    }

    /// <summary>v1 auto-pick — return the first card carrying a void
    /// counter under this Voidwalker, or null if the pile is empty.</summary>
    public ICard? FirstVoidCounteredCard() =>
        _exiledWithVoidCounter.FirstOrDefault();
}

/// <summary>
/// Replacement effect: when an opponent's card would be put into a
/// graveyard from anywhere, exile it instead and stamp a void counter
/// on it under the owning Voidwalker. CR 614 — registered on the
/// <see cref="ReplacementBus"/> and consulted by <see cref="Services.ZoneService"/>
/// on every move.
/// </summary>
public sealed class VoidwalkerExileReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Card _voidwalker;
    private readonly VoidwalkerState _state;

    public VoidwalkerExileReplacement(Card voidwalker, VoidwalkerState state)
    {
        _voidwalker = voidwalker ?? throw new ArgumentNullException(nameof(voidwalker));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (intent.ToZone != ZoneType.Graveyard) return false;
        // Voidwalker only fires its replacement while on the battlefield —
        // an exiled / hand-bound Voidwalker has no active static ability
        // (CR 113.6 / 614.12). When Voidwalker leaves the battlefield,
        // callers Unregister this effect; the zone check below is a belt-
        // and-braces guard.
        if (_voidwalker.Zone != ZoneType.Battlefield) return false;

        var cardOwner = intent.Card.Owner;
        if (cardOwner == null) return false;
        var controller = _voidwalker.Controller;
        if (controller == null) return false;

        // "Opponent's card" — anyone who is not Voidwalker's controller.
        return !ReferenceEquals(cardOwner, controller);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        // Stamp the void counter on the exile-bound card under this
        // Voidwalker, and rewrite the destination to Exile.
        _state.AddVoidCounter(intent.Card);
        return intent with { ToZone = ZoneType.Exile };
    }
}

/// <summary>
/// "Remove a void counter from a card exiled with Dauthi Voidwalker" —
/// activation cost on Voidwalker's {2}, {T}, Remove a void counter
/// ability. Implements <see cref="ICost"/> so it composes alongside the
/// mana + tap costs on the same <see cref="ActivatedAbility"/>.
///
/// Note: the actual counter removal is performed by the ability's
/// effect (so callers can read the chosen target from state for the
/// follow-up cast). <see cref="CanPay"/> only checks pile non-emptiness;
/// <see cref="Pay"/> is a no-op. This mirrors Walking Ballista's
/// <see cref="RemovePlusOnePlusOneCounterCost"/> shape — except the
/// removal target needs to be visible to the effect, so we defer the
/// actual mutation to <see cref="ActivatedAbility.Effects"/>.
/// </summary>
public sealed class RemoveVoidCounterCost : ICost
{
    private readonly VoidwalkerState _state;

    public RemoveVoidCounterCost(VoidwalkerState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public string Description =>
        $"Remove a void counter from a card exiled with {_state.Voidwalker.Name}";

    public bool CanPay(Player player) => _state.VoidCounterCount > 0;

    public void Pay(Player player)
    {
        // No-op — the ability's effect performs the actual counter
        // removal so the same target stays accessible to follow-up
        // bookkeeping (the cast-for-free step). CanPay is the gate.
        if (!CanPay(player))
        {
            throw new InvalidOperationException(
                $"Cannot pay void-counter cost: no card carries a void counter "
                + $"under {_state.Voidwalker.Name}.");
        }
    }
}
