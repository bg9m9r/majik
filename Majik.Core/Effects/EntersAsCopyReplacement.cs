using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 706.9 / 706.10 — "You may have this [permanent] enter as a copy of
/// [a/any creature | artifact-or-creature | land | creature-or-planeswalker
/// you control]." Watches the owning card's ETB <see cref="ZoneMoveIntent"/>
/// and, on apply, installs a copy of the chosen source onto the entering
/// permanent.
///
/// ## Two copy machineries
/// <list type="bullet">
///   <item><b>Creature-only legacy path</b> (the original 3-arg constructor):
///   registers a <see cref="CopyEffect"/> (mirrors printed P/T + keywords).
///   Used by Clone / Phantasmal Image / Glasspool Mimic — kept byte-for-byte
///   so those tests stay green.</item>
///   <item><b>Generalized characteristics path</b> (the options constructor):
///   registers a <see cref="CopyCharacteristicsEffect"/> (CR 707.2 — full
///   copiable type line / subtypes / supertypes / colour / P/T), so the copy
///   source may be a NON-creature artifact, a land, or a planeswalker. This is
///   the path Phyrexian Metamorph / Vesuva / Spark Double use.</item>
/// </list>
///
/// ## Riders (generalized path, CR 706.2 / 706.9b / 613.1d)
/// <list type="bullet">
///   <item><b>Extra type-add</b> (<see cref="Options.AddTypeOnCopy"/>) —
///   Layer-4 <see cref="AddCardTypeEffect"/> "it's an Artifact in addition"
///   (Phyrexian Metamorph, CR 706.9c).</item>
///   <item><b>Strip legendary</b> (<see cref="Options.StripLegendary"/>) —
///   Layer-4 <see cref="RemoveSupertypeEffect"/> "it's not legendary if that
///   permanent is legendary" (Vesuva / Spark Double, CR 706.2).</item>
///   <item><b>Conditional entry counter</b>
///   (<see cref="Options.PlusOneCounterIfCopiedCreature"/> /
///   <see cref="Options.LoyaltyCounterIfCopiedPlaneswalker"/>) — CR 706.9b:
///   "enters with an additional +1/+1 counter if it's a creature / loyalty
///   counter if it's a planeswalker" (Spark Double). The +1/+1 counter rides
///   through <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> so it lands
///   after the permanent is placed.</item>
///   <item><b>Enters tapped</b> (<see cref="Options.EntersTapped"/>) — Vesuva
///   "enter tapped as a copy" rides through
///   <see cref="ZoneMoveIntent.EntersTapped"/>.</item>
/// </list>
///
/// ## v1 lossy
/// - The "you may" choice is auto-yes when any candidate exists; no agent
///   prompt yet. Tests model "decline" by leaving the pool empty.
/// - Deterministic first-candidate pick (no agent picker through the
///   replacement bus yet).
/// - GraveyardAny: controller's graveyard only (Body Double's "any graveyard"
///   is lossy).
/// </summary>
public sealed class EntersAsCopyReplacement : IReplacementEffect<ZoneMoveIntent>
{
    public enum CopyPool { AnyBattlefield, BattlefieldYouControl, GraveyardAny }

    /// <summary>
    /// CR 706.9 — which permanents the copy source may be drawn from (in
    /// addition to the <see cref="CopyPool"/> zone restriction).
    /// </summary>
    public enum SourceFilter
    {
        /// <summary>Creature sources only (Clone family).</summary>
        Creature,
        /// <summary>Any artifact OR creature (Phyrexian Metamorph).</summary>
        ArtifactOrCreature,
        /// <summary>Any land (Vesuva).</summary>
        Land,
        /// <summary>A creature OR planeswalker (Spark Double).</summary>
        CreatureOrPlaneswalker,
    }

    /// <summary>
    /// Riders applied on the generalized characteristics-copy path (CR 706.2 /
    /// 706.9b / 706.9c). Defaults reproduce a plain copy with no riders.
    /// </summary>
    public sealed record Options(
        SourceFilter Filter,
        CardType? AddTypeOnCopy = null,
        bool StripLegendary = false,
        bool PlusOneCounterIfCopiedCreature = false,
        bool LoyaltyCounterIfCopiedPlaneswalker = false,
        bool EntersTapped = false);

    private readonly ICard _card;
    private readonly CopyPool _pool;
    private readonly ContinuousEffectsService _effects;
    private readonly Options? _options;

    /// <summary>
    /// Legacy creature-only constructor — registers a <see cref="CopyEffect"/>
    /// against the entering creature. Preserved for Clone / Phantasmal Image /
    /// Glasspool Mimic.
    /// </summary>
    public EntersAsCopyReplacement(
        ICard card,
        CopyPool pool,
        ContinuousEffectsService effects)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _pool = pool;
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _options = null;
    }

    /// <summary>
    /// Generalized constructor — registers a
    /// <see cref="CopyCharacteristicsEffect"/> against the entering permanent
    /// and applies the riders in <paramref name="options"/>. Supports
    /// non-creature copy sources (artifact, land, planeswalker).
    /// </summary>
    public EntersAsCopyReplacement(
        ICard card,
        CopyPool pool,
        ContinuousEffectsService effects,
        Options options)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _pool = pool;
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        var controller = intent.Controller ?? _card.Owner;

        // ---- Legacy creature-only path (CopyEffect) -----------------------
        if (_options == null)
        {
            if (_card is not Creature copier) return intent;
            var creatureSource = PickCreatureSource(controller);
            if (creatureSource != null)
            {
                _effects.Register(new CopyEffect(copier, creatureSource));
            }
            return intent;
        }

        // ---- Generalized characteristics path -----------------------------
        if (_card is not Permanent target) return intent;

        var source = PickPermanentSource(controller);
        if (source == null) return intent;  // no candidate → enters as printed.

        // CR 707.2 — full copiable characteristics copy (lasts while on the
        // battlefield, Clone-style). Source may be an artifact / land /
        // planeswalker, not just a creature. RegisterCopy ALSO mirrors the
        // source's printed non-keyword activated / triggered abilities onto the
        // entering permanent (re-instantiated bound to it via the default
        // rebind), so a clone of an ability permanent (Phyrexian Metamorph /
        // Spark Double / Vesuva onto an ability source) gets the source's
        // abilities, not just its keyword markers. Granted triggered abilities
        // auto-bind to the TriggerManager when they land on the copy's
        // Abilities list; the grant revokes when the copy leaves play (CR 613.6e).
        CopyCharacteristicsEffect.RegisterCopy(
            _effects,
            target,
            source,
            abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind,
            expiresAtEndOfTurn: false);

        // CR 706.9c / 613.1d — "it's an [type] in addition to its other types".
        if (_options.AddTypeOnCopy is { } addType)
        {
            _effects.Register(new AddCardTypeEffect(target, addType));
        }

        // CR 706.2 — "it's not legendary if that permanent is legendary".
        if (_options.StripLegendary)
        {
            _effects.Register(new RemoveSupertypeEffect(target, CardSupertype.Legendary));
        }

        // Plumb the layer service into the card so P/T / type / subtype reads
        // consult the copied characteristics (CR 613).
        target.ActiveEffects = _effects;

        // CR 706.9b — conditional extra entry counter, keyed on what we copied.
        var extraPlusOne = intent.PlusOneCountersOnEnter;
        if (_options.PlusOneCounterIfCopiedCreature && source.HasType(CardType.Creature))
        {
            extraPlusOne += 1;
        }
        if (_options.LoyaltyCounterIfCopiedPlaneswalker && source.HasType(CardType.Planeswalker))
        {
            // The engine tracks loyalty on Planeswalker.Loyalty rather than the
            // generic Counters collection; surfacing the extra loyalty counter
            // through the copy-characteristics row is the manland-P/T-style gap
            // documented on CopyCharacteristicsEffect. Add it directly when the
            // entering instance is a Planeswalker (the common Spark Double case
            // copies a creature, handled above).
            if (target is Planeswalker pw) pw.AddLoyalty(1);
        }

        return intent with
        {
            PlusOneCountersOnEnter = extraPlusOne,
            EntersTapped = intent.EntersTapped || _options.EntersTapped,
        };
    }

    private Creature? PickCreatureSource(Player? controller)
    {
        if (controller == null) return null;

        IEnumerable<Creature> candidates = _pool switch
        {
            CopyPool.GraveyardAny => controller.Zones.Graveyard.GetCards().OfType<Creature>(),
            _ => controller.Zones.Battlefield.GetCards().OfType<Creature>(),
        };
        return candidates.FirstOrDefault(c => !ReferenceEquals(c, _card));
    }

    private Permanent? PickPermanentSource(Player? controller)
    {
        if (controller == null || _options == null) return null;

        IEnumerable<Permanent> zone = _pool switch
        {
            CopyPool.GraveyardAny => controller.Zones.Graveyard.GetCards().OfType<Permanent>(),
            _ => controller.Zones.Battlefield.GetCards().OfType<Permanent>(),
        };

        return zone
            .Where(p => !ReferenceEquals(p, _card))
            .FirstOrDefault(p => MatchesFilter(p, _options.Filter));
    }

    private static bool MatchesFilter(Permanent p, SourceFilter filter) => filter switch
    {
        SourceFilter.Creature => p.HasType(CardType.Creature),
        SourceFilter.ArtifactOrCreature =>
            p.HasType(CardType.Artifact) || p.HasType(CardType.Creature),
        SourceFilter.Land => p.HasType(CardType.Land),
        SourceFilter.CreatureOrPlaneswalker =>
            p.HasType(CardType.Creature) || p.HasType(CardType.Planeswalker),
        _ => false,
    };
}
