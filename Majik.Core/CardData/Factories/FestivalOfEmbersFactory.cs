using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Festival of Embers (Modern Horizons 3 — {4}{R} red
/// Enchantment).
///
/// Oracle text (verified against Scryfall):
///   "During your turn, you may cast instant and sorcery spells from your
///    graveyard by paying 1 life in addition to their other costs.
///    If a card or token would be put into your graveyard from anywhere,
///    exile it instead.
///    {1}{R}: Sacrifice this enchantment."
///
/// Festival of Embers is the persistent-enchantment analogue of
/// <see cref="YawgmothsWillFactory"/> (cast spells from your graveyard +
/// graveyard→exile replacement), narrowed to <b>instant and sorcery</b>
/// spells, with a <b>+1 life rider</b> on each grave-cast and a self-sacrifice
/// activated ability. The graveyard→exile half is the
/// <see cref="DryadMilitantFactory"/> / Rest-in-Peace pattern (a CR 614
/// replacement gated on the enchantment being on the battlefield — NOT
/// EOT-expirable), scoped to the controller's own cards (and tokens).
///
/// The base card shape (name / Enchantment / {4}{R} / red) is materialised
/// from the embedded JSON definition (<c>festival-of-embers.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the replacement + sac ability are
/// layered on in C# (same posture as <see cref="SealOfFireFactory"/> /
/// <see cref="DryadMilitantFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Enchantment shape</b> at {4}{R}, red (mana value 5).
/// - <b>Static grant — "During your turn, you may cast instant and sorcery
///   spells from your graveyard by paying 1 life in addition to their other
///   costs" (CR 118.9 / CR 118.8)</b>: surfaced as a
///   <see cref="FestivalOfEmbersGraveyardCastGate"/> (an
///   <see cref="IGraveyardCastGate"/>) that gates on (a) the enchantment being
///   on the battlefield, (b) the caster being its controller, (c) the
///   controller's own turn, and (d) the grave card being an instant or sorcery.
///   Callers build a <see cref="GraveyardCastAlternativeCost"/> via
///   <see cref="BuildAlternativeCost"/>, which carries the card's <i>printed</i>
///   mana cost ("in addition to their other costs" — Festival does NOT waive
///   the mana cost) plus a <c>lifeCost: 1</c> rider, and feed it into the
///   spell-cast flow — exactly the Lurrus / Yawgmoth's-Will plumbing. There is
///   no once-per-turn cap (unlike Lurrus), so the gate tracks no per-turn
///   ledger.
/// - <b>Static replacement — "If a card or token would be put into your
///   graveyard from anywhere, exile it instead" (CR 614)</b>: a
///   <see cref="FestivalOfEmbersGraveToExileReplacement"/> registered on the
///   supplied <see cref="ReplacementBus"/>, rewriting every
///   <see cref="ZoneMoveIntent"/> headed to <see cref="ZoneType.Graveyard"/>
///   whose moving card is owned by Festival's controller to
///   <see cref="ZoneType.Exile"/>. Gated on the enchantment being on the
///   battlefield (CR 614.6) so a destroyed / bounced Festival stops rewriting
///   immediately — NOT EOT-expirable (unlike Yawgmoth's Will, which is a
///   one-turn sorcery effect). "a card or token" — the rewrite keys on the
///   moving card's owner only, so token copies the controller owns are covered
///   too.
/// - <b>Activated — "{1}{R}: Sacrifice this enchantment" (CR 602)</b>: an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>("{1}{R}")
///   cost whose effect sacrifices Festival itself (CR 701.16a). When a bus is
///   supplied the sacrifice publishes a <see cref="PermanentSacrificedEvent"/>
///   crediting the controller (aristocrat seam).
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-ordering prompt</b> (CR 616.1): overlapping graveyard
///   replacements (Rest in Peace / Leyline of the Void / another Festival)
///   apply in registration order — affected-player choice deferred, same gap as
///   every other graveyard-replacement factory.
/// </summary>
[CardName("Festival of Embers")]
public static class FestivalOfEmbersFactory
{
    public const string CardName = "Festival of Embers";
    public const string Slug = "festival-of-embers";

    /// <summary>CR 118.8 — the life paid in addition to a grave-cast's other
    /// costs.</summary>
    public const int GraveCastLifeCost = 1;

    /// <summary>The self-sacrifice activated ability's mana cost.</summary>
    public const string SacrificeManaCost = "{1}{R}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Card-instance → gate registry. Festival's grant is
    /// instance-scoped (each Festival's gate reads its own zone +
    /// controller).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Card, FestivalOfEmbersGraveyardCastGate>
        _gates = new();

    /// <summary>Retrieve the <see cref="FestivalOfEmbersGraveyardCastGate"/>
    /// attached to a Festival instance produced by this factory. Null when the
    /// card was not built here.</summary>
    public static FestivalOfEmbersGraveyardCastGate? GetGate(Card festival)
    {
        ArgumentNullException.ThrowIfNull(festival);
        return _gates.TryGetValue(festival, out var gate) ? gate : null;
    }

    /// <summary>
    /// Construct Festival of Embers with card identity + the gate + sac ability
    /// only (no replacement-bus wiring). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to — suitable for shape /
    /// dispatcher tests. The graveyard→exile replacement is skipped on this
    /// path (same v1 posture as <see cref="DryadMilitantFactory"/> /
    /// <see cref="RestInPeaceFactory"/>: the bus-aware overload registers it).
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, replacements: null, eventBus: null);

    /// <summary>
    /// Canonical builder.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Bus on which the graveyard→exile replacement
    /// is registered. Null → the replacement half is skipped (card identity +
    /// gate + sac ability only).</param>
    /// <param name="eventBus">When non-null, the self-sacrifice cost / effect
    /// publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a).</param>
    public static Enchantment Create(Player owner, ReplacementBus? replacements, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {4}{R}, red). The JSON carries no abilities — behaviour is layered on.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Static grant — "During your turn, you may cast instant and sorcery
        // spells from your graveyard by paying 1 life in addition to their
        // other costs." (CR 118.9 / 118.8)
        //
        // Surfaced as an IGraveyardCastGate; callers compose it with a
        // GraveyardCastAlternativeCost via BuildAlternativeCost. The gate gates
        // on Festival being on the battlefield, the caster being its
        // controller, the controller's own turn, and the grave card being an
        // instant or sorcery.
        // ----------------------------------------------------------------
        var gate = new FestivalOfEmbersGraveyardCastGate(card);
        _gates.AddOrUpdate(card, gate);

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description:
                "During your turn, you may cast instant and sorcery spells from "
                + "your graveyard by paying 1 life in addition to their other costs.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));

        // ----------------------------------------------------------------
        // Static replacement — "If a card or token would be put into your
        // graveyard from anywhere, exile it instead." (CR 614)
        //
        // Gated on Festival being on the battlefield, so blink / bounce /
        // destroy stop the rewrite immediately. NOT EOT-expirable.
        // ----------------------------------------------------------------
        replacements?.Register<ZoneMoveIntent>(
            new FestivalOfEmbersGraveToExileReplacement(card));

        // ----------------------------------------------------------------
        // Activated — "{1}{R}: Sacrifice this enchantment." (CR 602)
        //
        // Mana cost {1}{R}; the effect sacrifices Festival itself. When a bus
        // is supplied the sacrifice publishes PermanentSacrificedEvent
        // (CR 701.16a). The sacrifice is the EFFECT here (not a cost), so it is
        // performed in the resolution closure.
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice this enchantment",
            () => SacrificeSelf(card, owner, eventBus));

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(SacrificeManaCost),
            },
            effects: new IEffect[] { sacEffect }));

        return card;
    }

    /// <summary>
    /// Convenience builder. Constructs a
    /// <see cref="GraveyardCastAlternativeCost"/> bound to the given graveyard
    /// card's <i>printed</i> mana cost ("in addition to their other costs" —
    /// Festival doesn't waive the mana cost) plus a 1-life rider (CR 118.8),
    /// wired to the supplied Festival gate. Throws when the card is not an
    /// instant or sorcery — Festival only grants those.
    /// </summary>
    public static GraveyardCastAlternativeCost BuildAlternativeCost(
        ICard card,
        FestivalOfEmbersGraveyardCastGate gate)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(gate);

        if (!IsInstantOrSorcery(card))
        {
            throw new InvalidOperationException(
                $"Festival of Embers alt cost: {card.Name} is not an instant or sorcery card.");
        }

        var manaCost = card is Card concrete ? concrete.ManaCostValue : ManaCost.Parse(card.ManaCost);

        return new GraveyardCastAlternativeCost(
            description: $"Festival of Embers — cast {card.Name} from graveyard (pay 1 life)",
            cost: manaCost,
            gate: gate,
            lifeCost: GraveCastLifeCost);
    }

    /// <summary>
    /// CR 304 / CR 305 — a card is an instant or sorcery card iff its card
    /// types include <see cref="CardType.Instant"/> or
    /// <see cref="CardType.Sorcery"/>.
    /// </summary>
    internal static bool IsInstantOrSorcery(ICard card)
    {
        if (card == null) return false;
        return card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery);
    }

    /// <summary>
    /// Move Festival from the battlefield to its owner's graveyard (CR 701.16a).
    /// When a bus is supplied, route through <see cref="Fx.Sacrifice(ICard, Player, IEventBus)"/>
    /// so a <see cref="PermanentSacrificedEvent"/> is published. Idempotent —
    /// no-op if Festival is already off the battlefield.
    /// </summary>
    private static void SacrificeSelf(Enchantment festival, Player owner, IEventBus? eventBus)
    {
        if (festival.Zone != ZoneType.Battlefield) return;

        if (eventBus != null)
        {
            Fx.Sacrifice(festival, festival.Controller ?? owner, eventBus);
            return;
        }

        owner.Zones.Battlefield.RemoveCard(festival);
        owner.Zones.Graveyard.AddCard(festival);
        festival.SetZone(ZoneType.Graveyard);
    }
}

/// <summary>
/// Runtime gate for Festival of Embers' "During your turn, you may cast instant
/// and sorcery spells from your graveyard" clause. Consulted by
/// <see cref="GraveyardCastAlternativeCost.CanCastFor"/>. Unlike
/// <see cref="LurrusGraveyardCastGate"/>, Festival has no once-per-turn cap, so
/// no per-turn ledger is tracked — the "during your turn" timing predicate
/// reads the active player supplied at cast time.
/// </summary>
public sealed class FestivalOfEmbersGraveyardCastGate : IGraveyardCastGate
{
    private readonly Card _festival;
    private Player? _activePlayer;

    public FestivalOfEmbersGraveyardCastGate(Card festival)
    {
        _festival = festival ?? throw new ArgumentNullException(nameof(festival));
    }

    /// <summary>The player whose turn is currently active, as last set by
    /// <see cref="SetActivePlayer"/>. Null until a turn boundary is observed.
    /// Used by <see cref="CanCast"/> to enforce "during your turn".</summary>
    public Player? ActivePlayer => _activePlayer;

    /// <summary>
    /// Note whose turn it currently is — drives the "during your turn" timing
    /// gate (CR 117.1a). The bus-aware caller wires this off
    /// <see cref="TurnStartedEvent"/>; the test harness calls it directly.
    /// </summary>
    public void SetActivePlayer(Player turnPlayer)
    {
        _activePlayer = turnPlayer ?? throw new ArgumentNullException(nameof(turnPlayer));
    }

    /// <inheritdoc/>
    public bool CanCast(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;

        // Festival must be on the battlefield to grant the cast (CR 113.6).
        if (_festival.Zone != ZoneType.Battlefield) return false;

        // "you" = Festival's controller (CR 109.5).
        if (!ReferenceEquals(_festival.Controller, caster)) return false;

        // "During your turn" — only legal on the caster's own turn.
        if (_activePlayer == null) return false;
        if (!ReferenceEquals(_activePlayer, caster)) return false;

        // Instant or sorcery only.
        return FestivalOfEmbersFactory.IsInstantOrSorcery(card);
    }

    /// <inheritdoc/>
    public void NotePerformed(ICard card, Player caster)
    {
        // No once-per-turn cap — nothing to record.
    }
}

/// <summary>
/// CR 614 replacement effect: while Festival of Embers is on the battlefield,
/// every <see cref="ZoneMoveIntent"/> headed to <see cref="ZoneType.Graveyard"/>
/// whose moving card is owned by Festival's controller ("your graveyard") is
/// rewritten to <see cref="ZoneType.Exile"/>. "a card or token … from anywhere"
/// — no source-zone gate, and token-ness is irrelevant (the rewrite keys on the
/// owner only). NOT EOT-expirable — the static stays live as long as Festival is
/// on the battlefield (CR 614.6).
/// </summary>
public sealed class FestivalOfEmbersGraveToExileReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Card _source;

    public FestivalOfEmbersGraveToExileReplacement(Card source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.ToZone != ZoneType.Graveyard) return false;

        // "your graveyard" — only the controller's own cards (CR 109.5).
        var controller = _source.Controller;
        if (controller == null) return false;
        return ReferenceEquals(intent.Card.Owner, controller);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}
