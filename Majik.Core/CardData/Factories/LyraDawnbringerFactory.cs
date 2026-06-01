using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lyra Dawnbringer (Dominaria, {3}{W}{W}).
/// Legendary Creature — Angel 5/5. Oracle text (verified against
/// Scryfall):
///   "Flying
///    First strike (This creature deals combat damage before creatures
///    without first strike.)
///    Lifelink (Damage dealt by this creature also causes you to gain
///    that much life.)
///    Other Angels you control get +1/+1 and have lifelink."
///
/// The card's base shape (name, Creature, Legendary supertype, Angel
/// subtype, {3}{W}{W}, 5/5) is materialised from the embedded JSON
/// definition (<c>lyra-dawnbringer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The four printed behaviours
/// (Flying / First strike / Lifelink keywords + the Angel-lord anthem) are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// yet express keyword markers or lord statics, so they live in the factory
/// (same posture as <see cref="StormscaleScionFactory"/> and the other
/// JSON-backed lords).
///
/// ## Implemented (v1)
/// - <b>Flying (CR 702.9)</b>, <b>First strike (CR 702.7)</b>,
///   <b>Lifelink (CR 702.15)</b> on Lyra herself — each wired as a
///   <see cref="KeywordAbility"/> marker. Flying / First strike are read
///   by the combat block-restriction / first-strike-step pipeline
///   (CombatAbilities); Lifelink by the standard combat-damage life-gain
///   pipeline. Same body shape as <see cref="SerraAngelFactory"/> (a 4/4
///   Flying Angel) plus the First-strike marker of
///   <see cref="RazorfootGriffinFactory"/> and the Lifelink marker of
///   <see cref="TrainedCaracalFactory"/>.
/// - <b>Lord static (CR 613.7c P/T + CR 613.1f/1g granted keyword)</b>:
///   "Other Angels you control get +1/+1 and have lifelink." Wired via
///   <see cref="LordStaticEffect"/> with <c>matchingSubtype: Angel,
///   power: 1, toughness: 1, grantedKeywords: ["Lifelink"], includeSelf:
///   false, opponentsOnly: false, allPlayers: false</c> —
///   controller-scoped ("you control"); <c>includeSelf: false</c> honours
///   the printed "Other" (Lyra's own Lifelink comes from the printed
///   keyword above, not the anthem). Identical shape to
///   <see cref="GoblinChieftainFactory"/>'s keyword-granting anthem and
///   <see cref="StormscaleScionFactory"/>'s Dragon +1/+1. Registered only
///   when a <see cref="ContinuousEffectsService"/> is supplied.
///
/// Multiple Lyras stack the +1/+1 (HashSet keyword semantics make the
/// duplicate Lifelink grant idempotent).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Lyra isn't on the battlefield so the bonus lifts correctly (same
///   posture as <see cref="GoblinChieftainFactory"/> /
///   <see cref="StormscaleScionFactory"/>).
/// </summary>
[CardName("Lyra Dawnbringer")]
public static class LyraDawnbringerFactory
{
    public const string CardName = "Lyra Dawnbringer";
    public const string Slug = "lyra-dawnbringer";

    /// <summary>
    /// Construct Lyra Dawnbringer with the printed Flying / First strike /
    /// Lifelink keywords wired but no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the Angel-lord anthem is not
    /// registered (other Angels you control don't yet receive +1/+1 +
    /// Lifelink because there's no layers service to register the effect
    /// against). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Lyra Dawnbringer. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and Lifelink to other
    /// Angels the controller controls is registered against the layers
    /// service. Lyra's own Flying / First strike / Lifelink keyword markers
    /// are always wired.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 + Lifelink Angel anthem against. May be null — no live
    /// bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Legendary, Angel, {3}{W}{W}, 5/5). The JSON carries no abilities —
        // the keyword markers + anthem are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker; CombatAbilities.HasFlying
        // / CanBlockFlying read it for evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.7 — First strike. KeywordAbility marker; consumed by the
        // first-strike combat-damage step (CombatAbilities.HasFirstStrike).
        card.AddAbility(new KeywordAbility("First Strike", card, owner));

        // CR 702.15 — Lifelink. KeywordAbility marker consumed by the
        // standard combat-damage life-gain pipeline.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f/1g (granted keyword) — "Other
            // Angels you control get +1/+1 and have lifelink." allPlayers:
            // false → controller-scoped ("you control"). includeSelf: false
            // honours the printed "Other" (Lyra's own Lifelink is the
            // printed keyword above, not the anthem).
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Angel,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Lifelink" },
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        return card;
    }
}
