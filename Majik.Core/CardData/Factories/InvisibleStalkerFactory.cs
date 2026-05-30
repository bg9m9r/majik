using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Invisible Stalker (Innistrad, {1}{U}).
///
/// Creature — Human Rogue 1/1. Oracle text (verified against Scryfall):
///   "Hexproof (This creature can't be the target of spells or abilities
///    your opponents control.)
///    This creature can't be blocked."
///
/// The card's base shape (name, Creature, Human/Rogue subtypes, {1}{U},
/// 1/1) is materialised from the embedded JSON definition
/// (<c>invisible-stalker.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed static
/// riders (Hexproof + "can't be blocked") are layered on top here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express keyword markers or
/// combat restrictions, so they live in the factory (same posture as
/// <see cref="StormscaleScionFactory"/> / <see cref="BladeSplicerFactory"/>
/// and the analogue <see cref="BlightedAgentFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 <see cref="Creature"/> — Human Rogue at {1}{U}.
/// - <b>Hexproof (CR 702.11)</b> — wired as a <see cref="KeywordAbility"/>
///   marker. This is the live read path:
///   <see cref="Majik.Core.Targeting.TargetLegality"/> consults the
///   "Hexproof" keyword directly to reject targeting by spells / abilities
///   an opponent controls (CR 702.11b). Same shape as the Hexproof rider on
///   <see cref="SigardaHostOfHeronsFactory"/> /
///   <see cref="GeistOfSaintTraftFactory"/>.
/// - <b>"This creature can't be blocked." (CR 509.1c)</b> — registered on
///   the supplied <see cref="ContinuousEffectsService"/> as a non-expiring
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/> scoped to this
///   creature; <see cref="Majik.Core.Combat.CombatValidator"/> consults the
///   restriction during block declaration. An "Unblockable"
///   <see cref="KeywordAbility"/> marker is also attached so card-text /
///   keyword scans observe the rider on the shape-only path (no live
///   effects service). Identical shape to the analogue
///   <see cref="BlightedAgentFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"Can't be blocked" without a continuous-effects service</b>: the
///   shape-only <see cref="Create(Player)"/> path attaches the Unblockable
///   keyword marker but does NOT install the
///   <see cref="CombatRestrictionEffect"/> — <see cref="Majik.Core.Combat.CombatValidator"/>
///   without an effects service still allows blocks. Production callers
///   thread the live service via the (owner, effects) overload. Same posture
///   as <see cref="BlightedAgentFactory"/>.
/// </summary>
[CardName("Invisible Stalker")]
public static class InvisibleStalkerFactory
{
    public const string CardName = "Invisible Stalker";
    public const string Slug = "invisible-stalker";

    /// <summary>
    /// Construct Invisible Stalker with no continuous-effects service. The
    /// Hexproof + Unblockable keyword markers are attached for card-text
    /// inspection (Hexproof is fully live — TargetLegality reads the marker),
    /// but the live "can't be blocked" combat restriction is NOT registered.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Invisible Stalker with an optional
    /// <see cref="ContinuousEffectsService"/>. When the service is supplied
    /// the "can't be blocked" rider is registered as a non-expiring
    /// <see cref="CombatRestrictionEffect"/> bound to the stalker so
    /// <see cref="Majik.Core.Combat.CombatValidator"/> rejects block
    /// declarations targeting it (CR 509.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. May be null — the
    /// unblockable restriction is then skipped (keyword marker still attached
    /// for inspection).</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Rogue subtypes, {1}{U}, 1/1). The JSON carries no abilities —
        // Hexproof + Unblockable are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Hexproof — CR 702.11. KeywordAbility marker; this is the LIVE read
        // path — TargetLegality consults the "Hexproof" keyword to reject
        // targeting by an opponent's spells / abilities (CR 702.11b).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Hexproof", card, owner));

        // ----------------------------------------------------------------
        // "This creature can't be blocked." — CR 509.1c.
        //
        // Keyword marker covers the card-text / inspection surface; the
        // working combat restriction is registered on the supplied
        // ContinuousEffectsService (no-op on the shape-only path). The
        // restriction does not expire at end of turn — Invisible Stalker is
        // permanently unblockable while on the battlefield. Benign
        // off-battlefield: CombatValidator only consults it during block
        // declaration on live battlefield creatures. Same shape as the
        // analogue BlightedAgentFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Unblockable", card, owner));

        effects?.Register(new CombatRestrictionEffect(
            CombatRestriction.CannotBeBlocked,
            target: card,
            expiresAtEndOfTurn: false));

        return card;
    }
}
