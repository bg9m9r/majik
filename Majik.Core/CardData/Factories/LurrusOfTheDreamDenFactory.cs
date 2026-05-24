using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lurrus of the Dream-Den (Ikoria, {W}{B}).
///
/// Legendary Creature — Cat Nightmare 3/2. Oracle text:
///   "Lifelink
///    Companion — Each permanent card in your starting deck has mana
///    value 2 or less.
///    During each of your turns, you may cast one permanent spell with
///    mana value 2 or less from your graveyard."
///
/// ## Implemented (v1)
/// - 3/2 Legendary Creature — Cat Nightmare with Lifelink keyword.
/// - Static ability surfaced on the card (<see cref="StaticAbility"/>)
///   with a description containing "cast one permanent spell with mana
///   value 2 or less from your graveyard".
/// - <see cref="LurrusGraveyardCastGate"/> implements the runtime
///   predicate ("permanent card", "mana value ≤ 2", "controller's
///   turn", "no other Lurrus-grant cast already performed this turn").
///   Callers build a <see cref="GraveyardCastAlternativeCost"/> via
///   <see cref="BuildAlternativeCost"/>, passing the gate, and feed it
///   into <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>. The
///   alt cost handles the zone-restriction / "card returns to default
///   destination" (battlefield, no post-resolve exile) plumbing for
///   free — same shape as the existing cast-from-non-hand alt costs.
/// - Bus-aware overload subscribes to <see cref="TurnStartedEvent"/>
///   and resets the "cast performed this turn" flag whenever a new
///   turn begins. Without a bus the test harness is expected to call
///   <see cref="LurrusGraveyardCastGate.ResetForTurn"/> manually.
///
/// ## Companion (deck-construction half)
/// The companion deck-construction rule (CR 702.139 — "Each permanent
/// card in your starting deck has mana value 2 or less") is exposed via
/// <see cref="CompanionRestriction"/>, an
/// <see cref="ICompanionRestriction"/> that
/// <see cref="Majik.Core.Rules.CompanionValidator"/> consumes at
/// deck-registration time. The runtime "cast from outside the game"
/// pipeline is still deferred — the engine has no sideboard zone yet
/// (see <see cref="Majik.Core.Zones.ZoneType"/>), so the once-per-game
/// companion-tax cast is layered on once that surface lands. Until
/// then, the runtime grant ("during each of your turns, you may cast
/// one permanent spell with mana value 2 or less from your graveyard")
/// is the only in-game effect wired.
///
/// ## Approach
/// Mirrors Snapcaster Mage's runtime-cost-flag pattern. Instead of
/// teaching <see cref="Majik.Core.Game.SpellCastFlow"/> a new code
/// path, Lurrus exposes itself via an <see cref="IGraveyardCastGate"/>
/// that callers compose with a <see cref="GraveyardCastAlternativeCost"/>.
/// The existing alternative-cost machinery already handles the
/// zone-shift on cast, alternative-cost-replaces-printed-cost mana
/// payment (CR 118.9), and default post-resolution destination
/// (battlefield for permanents).
/// </summary>
[CardName("Lurrus of the Dream-Den")]
public static class LurrusOfTheDreamDenFactory
{
    /// <summary>
    /// Mana-value ceiling for "permanent spell with mana value 2 or less"
    /// — surfaced as a constant so tests and callers don't repeat the
    /// magic number.
    /// </summary>
    public const int MaxGraveyardCastManaValue = 2;

    /// <summary>
    /// CR 702.139 — Lurrus's companion deck-construction predicate:
    /// "Each permanent card in your starting deck has mana value 2 or
    /// less." Surfaced as a static singleton so deck-registration call
    /// sites can validate without instantiating Lurrus.
    /// </summary>
    public static ICompanionRestriction CompanionRestriction { get; } =
        new LurrusCompanionRestriction();

    /// <summary>
    /// Card-instance → gate registry. Lurrus's static ability is
    /// instance-scoped: each Lurrus owns its own once-per-turn ledger.
    /// Exposed for callers (test harness, future bot probe) via
    /// <see cref="GetGate"/>.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Card, LurrusGraveyardCastGate>
        _gates = new();

    /// <summary>
    /// Retrieve the <see cref="LurrusGraveyardCastGate"/> attached to a
    /// Lurrus instance produced by this factory. Returns null when the
    /// card was not built by this factory.
    /// </summary>
    public static LurrusGraveyardCastGate? GetGate(Card lurrus)
    {
        if (lurrus == null) throw new ArgumentNullException(nameof(lurrus));
        return _gates.TryGetValue(lurrus, out var gate) ? gate : null;
    }

    /// <summary>
    /// Construct Lurrus with no event-bus wiring. The graveyard-cast
    /// gate is still produced and stamped on the returned card; the
    /// per-turn reset must be driven manually by the caller (test
    /// harness) via <see cref="LurrusGraveyardCastGate.ResetForTurn"/>.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Lurrus with an optional event bus. When the bus is
    /// supplied, a <see cref="TurnStartedEvent"/> handler is subscribed
    /// that calls <see cref="LurrusGraveyardCastGate.ResetForTurn"/>
    /// whenever a turn begins, so the once-per-controller-turn slot
    /// refreshes automatically (CR 500.1 / 514 turn-boundary).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Lurrus of the Dream-Den",
            manaCost: "{W}{B}",
            power: 3,
            toughness: 2,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Nightmare });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.15 — Lifelink. Damage dealt by this creature also causes
        // its controller to gain that much life.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // Static ability marker — the runtime gate is exposed via the
        // returned gate accessor. The static ability description is
        // surfaced on the card so shape tests can confirm the printed
        // text is wired without running a full game.
        var gate = new LurrusGraveyardCastGate(card);
        _gates.AddOrUpdate(card, gate);

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description:
                "During each of your turns, you may cast one permanent spell "
                + "with mana value 2 or less from your graveyard.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));

        // Turn-boundary reset. CR 500.1 — beginning of each turn restarts
        // the per-turn "you may cast one … from your graveyard" budget.
        // No bus → no auto-reset (factory was built shape-only; callers
        // manage turn boundaries manually).
        if (eventBus != null)
        {
            Action<TurnStartedEvent>? handler = null;
            handler = (e) =>
            {
                gate.ResetForTurn(e.Player);
            };
            eventBus.Subscribe(handler);
        }

        return card;
    }

    /// <summary>
    /// Convenience builder. Constructs a
    /// <see cref="GraveyardCastAlternativeCost"/> bound to the given
    /// graveyard card's printed mana cost, wired to the supplied
    /// Lurrus-grant gate. Throws when the card is not a permanent or
    /// has mana value &gt; 2 — both are illegal-at-announce per the
    /// Lurrus oracle text.
    /// </summary>
    public static GraveyardCastAlternativeCost BuildAlternativeCost(
        ICard card,
        LurrusGraveyardCastGate gate)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (gate == null) throw new ArgumentNullException(nameof(gate));

        if (!LurrusGraveyardCastGate.IsPermanentSpell(card))
        {
            throw new InvalidOperationException(
                $"Lurrus alt cost: {card.Name} is not a permanent card.");
        }
        var manaCostValue = ManaCostOf(card);
        if (manaCostValue.TotalValue > MaxGraveyardCastManaValue)
        {
            throw new InvalidOperationException(
                $"Lurrus alt cost: {card.Name} mana value "
                + $"({manaCostValue.TotalValue}) exceeds 2.");
        }

        return new GraveyardCastAlternativeCost(
            description: $"Lurrus of the Dream-Den — cast {card.Name} from graveyard",
            cost: manaCostValue,
            gate: gate);
    }

    /// <summary>
    /// Read the mana-cost value object from an <see cref="ICard"/>. Falls
    /// back to <see cref="ManaCost.Parse"/> when the concrete subclass
    /// isn't <see cref="Card"/> (third-party / future ICard implementors).
    /// </summary>
    internal static ManaCost ManaCostOf(ICard card)
    {
        if (card is Card concrete) return concrete.ManaCostValue;
        return ManaCost.Parse(card.ManaCost);
    }
}

/// <summary>
/// Runtime gate for Lurrus's "cast one permanent spell with mana value
/// 2 or less from your graveyard each of your turns" clause. Tracks
/// per-player "has performed a Lurrus grave-cast this turn" so the
/// once-per-turn restriction can be enforced by the
/// <see cref="GraveyardCastAlternativeCost"/> that wraps the gate.
/// </summary>
public sealed class LurrusGraveyardCastGate : IGraveyardCastGate
{
    private readonly Card _lurrus;
    private readonly HashSet<Player> _castThisTurn = new();
    private Player? _currentTurnPlayer;

    public LurrusGraveyardCastGate(Card lurrus)
    {
        _lurrus = lurrus ?? throw new ArgumentNullException(nameof(lurrus));
    }

    /// <summary>
    /// The player whose turn is currently active, as observed by the
    /// last <see cref="ResetForTurn"/> call. Null until the first turn
    /// boundary is seen. Used by <see cref="CanCast"/> to enforce the
    /// "during each of your turns" timing predicate.
    /// </summary>
    public Player? ActivePlayer => _currentTurnPlayer;

    /// <summary>
    /// Reset the per-turn budget. Called from the
    /// <see cref="TurnStartedEvent"/> handler installed by the
    /// bus-aware factory overload — clears any "cast performed" flags
    /// that belong to the previous turn and notes the new active player.
    /// </summary>
    public void ResetForTurn(Player turnPlayer)
    {
        if (turnPlayer == null) throw new ArgumentNullException(nameof(turnPlayer));
        _currentTurnPlayer = turnPlayer;
        _castThisTurn.Clear();
    }

    /// <summary>
    /// True if the given caster has already used a Lurrus grave-cast
    /// this turn. Surfaces for assertions in tests.
    /// </summary>
    public bool HasCastThisTurn(Player caster) =>
        caster != null && _castThisTurn.Contains(caster);

    /// <summary>
    /// CR 110.4a / CR 110.5 — "permanent card" = a card with one or
    /// more of the permanent types (artifact, creature, enchantment,
    /// land, planeswalker, battle). Lurrus excludes instants and
    /// sorceries explicitly.
    /// </summary>
    public static bool IsPermanentSpell(ICard card)
    {
        if (card == null) return false;
        if (card.HasType(CardType.Instant)) return false;
        if (card.HasType(CardType.Sorcery)) return false;
        return card.HasType(CardType.Artifact)
            || card.HasType(CardType.Creature)
            || card.HasType(CardType.Enchantment)
            || card.HasType(CardType.Land)
            || card.HasType(CardType.Planeswalker);
    }

    /// <inheritdoc/>
    public bool CanCast(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;

        // Lurrus must be on the battlefield to grant the cast.
        if (_lurrus.Zone != ZoneType.Battlefield) return false;

        // Only the controller of Lurrus benefits — the static ability
        // says "you", which means Lurrus's controller per CR 109.5.
        if (!ReferenceEquals(_lurrus.Controller, caster)) return false;

        // "During each of your turns" — only legal on the caster's own
        // turn. When no turn boundary has been observed yet, refuse.
        if (_currentTurnPlayer == null) return false;
        if (!ReferenceEquals(_currentTurnPlayer, caster)) return false;

        // Permanent card only.
        if (!IsPermanentSpell(card)) return false;

        // Mana value 2 or less.
        if (LurrusOfTheDreamDenFactory.ManaCostOf(card).TotalValue
            > LurrusOfTheDreamDenFactory.MaxGraveyardCastManaValue)
            return false;

        // Once per turn.
        if (_castThisTurn.Contains(caster)) return false;

        return true;
    }

    /// <inheritdoc/>
    public void NotePerformed(ICard card, Player caster)
    {
        if (caster == null) return;
        _castThisTurn.Add(caster);
    }
}

/// <summary>
/// CR 702.139 — Lurrus's deck-construction predicate: "Each permanent
/// card in your starting deck has mana value 2 or less." Non-permanent
/// cards (instants, sorceries) are unconstrained per the printed wording
/// ("each permanent card"); among permanents (artifact, creature,
/// enchantment, land, planeswalker, battle), every one must satisfy
/// <see cref="ValueObjects.ManaCost.TotalValue"/> ≤
/// <see cref="LurrusOfTheDreamDenFactory.MaxGraveyardCastManaValue"/>.
/// </summary>
internal sealed class LurrusCompanionRestriction : ICompanionRestriction
{
    public string Description =>
        "Each permanent card in your starting deck has mana value 2 or less.";

    public bool IsSatisfiedBy(IEnumerable<ICard> startingDeck)
    {
        ArgumentNullException.ThrowIfNull(startingDeck);
        foreach (var card in startingDeck)
        {
            if (card == null) continue;
            if (!LurrusGraveyardCastGate.IsPermanentSpell(card)) continue;
            var mv = LurrusOfTheDreamDenFactory.ManaCostOf(card).TotalValue;
            if (mv > LurrusOfTheDreamDenFactory.MaxGraveyardCastManaValue)
                return false;
        }
        return true;
    }
}
