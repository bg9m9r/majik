using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Valgavoth, Terror Eater (Duskmourn: House of Horror,
/// {6}{B}{B}{B}).
///
/// Legendary Creature — Elder Demon 9/9. Oracle text (Scryfall, verified):
///   "Flying, lifelink
///    Ward—Sacrifice three nonland permanents.
///    If a card you didn't control would be put into an opponent's graveyard
///    from anywhere, exile it instead.
///    During your turn, you may play cards exiled with Valgavoth. If you cast a
///    spell this way, pay life equal to its mana value rather than pay its mana
///    cost."
///
/// Direct-construction factory in the <see cref="SireOfSevenDeathsFactory"/> /
/// <see cref="DauthiVoidwalkerFactory"/> mould: the
/// <see cref="Definitions.CardDefinition"/> JSON schema cannot express
/// <see cref="KeywordAbility"/> markers, a bus-registered replacement effect,
/// or the bespoke play-from-exile alternative cost — so Valgavoth is built in
/// C# and attaches them directly.
///
/// ## Implemented (v1)
/// - 9/9 Legendary Creature — Elder Demon at {6}{B}{B}{B}.
/// - <b>Flying</b> (CR 702.9) + <b>Lifelink</b> (CR 702.15) shipped as
///   <see cref="KeywordAbility"/> markers consumed by CombatValidator /
///   CombatAbilities.
/// - <b>Ward—Sacrifice three nonland permanents (CR 702.21c).</b> Shipped as a
///   <see cref="KeywordAbility"/>("Ward") marker plus a real battlefield-attached
///   ward trigger (<see cref="WardTriggerWiring.Attach"/>) whose payment is a
///   <see cref="SacrificeNNonlandPermanentsCost"/>(3). An opponent's targeting
///   spell/ability is countered unless its controller sacrifices three nonland
///   permanents (CR 702.21f). Same posture as Sire of Seven Deaths (Ward—Pay 7
///   life).
/// - <b>Replacement — opponent-grave-bound → exile (CR 614).</b> Registered on
///   the <see cref="ReplacementBus"/> (when supplied), mirroring Dauthi
///   Voidwalker's <see cref="VoidwalkerExileReplacement"/>. The replacement
///   watches every <see cref="ZoneMoveIntent"/> whose destination is
///   <see cref="ZoneType.Graveyard"/> and whose card's owner is an opponent of
///   Valgavoth's controller (CR 404.2 — a card goes to its owner's graveyard, so
///   "an opponent's graveyard" ≡ owner-is-opponent; together with the live
///   controller check this covers the "a card you didn't control" clause). It
///   rewrites the destination to <see cref="ZoneType.Exile"/> and records the
///   card in the per-Valgavoth exile registry. Per the oracle ("from anywhere")
///   it applies regardless of the source zone.
/// - <b>Play-from-exile — pay life equal to mana value (CR 118.9).</b> The
///   cast-for-life alternative cost is exposed via
///   <see cref="BuildPlayFromExileCost"/>, reusing the shared
///   <see cref="PayLifeEqualToManaValueAlternativeCost"/> (no mana paid; life =
///   the spell's mana value). Callers cast an exile-resident card via
///   <see cref="Game.SpellCastFlow"/> with this alt cost during Valgavoth's
///   controller's turn.
///
/// ## Deferred (v1 gaps)
/// - <b>"During your turn" timing gate</b>: the oracle restricts playing the
///   exiled cards to Valgavoth's controller's turn (CR 117). v1 records which
///   cards are exiled-with-Valgavoth; the turn gate is enforced by the caller's
///   cast path (the same posture as other play-from-exile grants). Wiring an
///   automatic permission window mirrors the Bolas's Citadel cast-from-top grant
///   and is deferred.
/// - <b>Replacement auto-unregister on leave-battlefield</b>: as with Dauthi
///   Voidwalker, the produced <see cref="ValgavothExileReplacement"/> is
///   surfaced on the returned tuple so the caller can
///   <see cref="ReplacementBus.Unregister{TIntent}"/> it when Valgavoth leaves
///   the battlefield. The replacement also belt-and-braces gates on Valgavoth
///   being on the battlefield (CR 113.6).
/// </summary>
[CardName("Valgavoth, Terror Eater")]
public static class ValgavothTerrorEaterFactory
{
    public const string CardName = "Valgavoth, Terror Eater";
    public const string PrintedManaCost = "{6}{B}{B}{B}";
    public const int Power = 9;
    public const int Toughness = 9;

    /// <summary>Number of nonland permanents an opponent sacrifices to pay
    /// Valgavoth's Ward (CR 702.21c).</summary>
    public const int WardSacrificeCount = 3;

    /// <summary>
    /// Per-Valgavoth state: which exile-resident cards were exiled with this
    /// Valgavoth (playable during its controller's turn). Keyed off the
    /// Valgavoth instance via a
    /// <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
    /// so multiple Valgavoths keep separate piles.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Card, ValgavothState>
        _state = new();

    /// <summary>
    /// Retrieve the <see cref="ValgavothState"/> attached to a Valgavoth
    /// instance produced by this factory. Returns null when the card was not
    /// built by this factory.
    /// </summary>
    public static ValgavothState? GetState(Card valgavoth)
    {
        ArgumentNullException.ThrowIfNull(valgavoth);
        return _state.TryGetValue(valgavoth, out var s) ? s : null;
    }

    /// <summary>
    /// CR 702.21 — Valgavoth's printed "Ward—Sacrifice three nonland
    /// permanents" effect, bound to the supplied <paramref name="card"/>. The
    /// ward cost is a non-mana <see cref="SacrificeNNonlandPermanentsCost"/>(3)
    /// (CR 702.21c); the mana portion is
    /// <see cref="Majik.Core.ValueObjects.ManaCost.Zero"/>.
    /// <see cref="WardEffect.Resolve"/> counters an opponent's targeting
    /// spell/ability unless they sacrifice three nonland permanents.
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new SacrificeNNonlandPermanentsCost(WardSacrificeCount));

    /// <summary>
    /// CR 118.9 — the "pay life equal to its mana value rather than pay its mana
    /// cost" alternative cost used to play a card exiled with Valgavoth. No mana
    /// is paid; the life paid is the spell's mana value
    /// (<see cref="PayLifeEqualToManaValueAlternativeCost"/>).
    /// </summary>
    public static PayLifeEqualToManaValueAlternativeCost BuildPlayFromExileCost() =>
        new();

    /// <summary>
    /// Construct Valgavoth with no replacement-bus wiring. The keyword markers,
    /// the Ward trigger, and the play-from-exile alt cost are wired, but the
    /// opponent-graveyard replacement does not fire because nothing consults the
    /// bus. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, replacements: null).Card;

    /// <summary>
    /// Construct Valgavoth with an optional <see cref="ReplacementBus"/>. When
    /// supplied, the opponent-graveyard → exile replacement is registered on it
    /// and surfaced on the returned tuple for the caller to
    /// <see cref="ReplacementBus.Unregister{TIntent}"/> when Valgavoth leaves the
    /// battlefield (v1 — automatic leave-cleanup deferred).
    /// </summary>
    public static (Creature Card, ValgavothExileReplacement? Replacement) Create(
        Player owner,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Elder, CardSubtype.Demon });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. CR 702.15 — Lifelink. Marker keywords consumed by
        // CombatValidator / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // CR 702.21c — Ward—Sacrifice three nonland permanents. Marker keyword
        // plus the real battlefield-attached ward trigger; an opponent's
        // targeting spell/ability is countered unless they sacrifice three
        // nonland permanents (same wiring as Sire of Seven Deaths' pay-life ward).
        card.AddAbility(new KeywordAbility("Ward", card, owner));
        WardTriggerWiring.Attach(BuildWardEffect(card), owner);

        var state = new ValgavothState(card);
        _state.AddOrUpdate(card, state);

        // CR 614 — "If a card you didn't control would be put into an opponent's
        // graveyard from anywhere, exile it instead." Funnel through
        // ZoneMoveIntent on the ReplacementBus (mirrors Dauthi Voidwalker).
        ValgavothExileReplacement? replacement = null;
        if (replacements != null)
        {
            replacement = new ValgavothExileReplacement(card, state);
            replacements.Register<ZoneMoveIntent>(replacement);
        }

        return (card, replacement);
    }
}

/// <summary>
/// Per-Valgavoth runtime state. Tracks which exile-resident cards were exiled
/// with this Valgavoth — the pool its controller "may play during their turn"
/// (CR 117).
/// </summary>
public sealed class ValgavothState
{
    private readonly Card _valgavoth;
    private readonly HashSet<ICard> _exiled = new(ReferenceEqualityComparer.Instance);

    public ValgavothState(Card valgavoth)
    {
        _valgavoth = valgavoth ?? throw new ArgumentNullException(nameof(valgavoth));
    }

    /// <summary>The Valgavoth instance this state belongs to.</summary>
    public Card Valgavoth => _valgavoth;

    /// <summary>Cards currently exiled with this Valgavoth (playable during its
    /// controller's turn).</summary>
    public IEnumerable<ICard> ExiledCards => _exiled;

    /// <summary>Number of cards exiled with this Valgavoth.</summary>
    public int ExiledCount => _exiled.Count;

    /// <summary>True if <paramref name="card"/> was exiled with this
    /// Valgavoth.</summary>
    public bool HasExiled(ICard card) => card != null && _exiled.Contains(card);

    /// <summary>Record <paramref name="card"/> as exiled with this Valgavoth.
    /// Idempotent.</summary>
    public void AddExiled(ICard card)
    {
        if (card == null) return;
        _exiled.Add(card);
    }

    /// <summary>Drop <paramref name="card"/> from the exile pile (e.g. once it
    /// has been played). Returns true if it was present.</summary>
    public bool RemoveExiled(ICard card) => card != null && _exiled.Remove(card);
}

/// <summary>
/// Replacement effect: "If a card you didn't control would be put into an
/// opponent's graveyard from anywhere, exile it instead." CR 614 — registered
/// on the <see cref="ReplacementBus"/> and consulted by
/// <see cref="Services.ZoneService"/> on every move. Mirrors Dauthi
/// Voidwalker's <see cref="VoidwalkerExileReplacement"/> but without the void
/// counter — Valgavoth simply tracks the exiled card for its play-from-exile
/// grant.
/// </summary>
public sealed class ValgavothExileReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Card _valgavoth;
    private readonly ValgavothState _state;

    public ValgavothExileReplacement(Card valgavoth, ValgavothState state)
    {
        _valgavoth = valgavoth ?? throw new ArgumentNullException(nameof(valgavoth));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (intent.ToZone != ZoneType.Graveyard) return false;

        // CR 113.6 — the static replacement is active only while Valgavoth is on
        // the battlefield. Callers Unregister on leave; this is a belt-and-braces
        // guard.
        if (_valgavoth.Zone != ZoneType.Battlefield) return false;

        var controller = _valgavoth.Controller;
        if (controller == null) return false;

        // CR 404.2 — a card is put into its OWNER's graveyard. "An opponent's
        // graveyard" therefore ≡ the card's owner is an opponent of Valgavoth's
        // controller.
        var cardOwner = intent.Card.Owner;
        if (cardOwner == null || ReferenceEquals(cardOwner, controller)) return false;

        // "A card you didn't control" — exclude anything Valgavoth's controller
        // controls (e.g. a stolen permanent of the opponent's heading to the
        // opponent's graveyard while still under Valgavoth's control). The
        // intent's Controller (move-time) falls back to the card's current
        // controller.
        var cardController = intent.Controller ?? intent.Card.Controller;
        if (cardController != null && ReferenceEquals(cardController, controller)) return false;

        return true;
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        // Record the card as exiled-with-Valgavoth, and rewrite the destination
        // to Exile.
        _state.AddExiled(intent.Card);
        return intent with { ToZone = ZoneType.Exile };
    }
}
