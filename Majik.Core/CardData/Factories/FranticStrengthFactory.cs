using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Frantic Strength (Bloomburrow, {2}{G}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-06-24):
///   "Flash
///    Enchant creature
///    Enchanted creature gets +2/+2 and has trample."
///
/// A green combat trick in Aura clothing: cast at instant speed (Flash) to
/// pump a creature +2/+2 and grant trample — pushing through blockers or
/// surviving combat. It is essentially <see cref="RancorFactory"/> without the
/// graveyard-recursion dies trigger, with a symmetric +2/+2 boost (rather than
/// Rancor's +2/+0) and the Flash keyword (CR 702.8).
///
/// ## Implementation
///
/// - <b>Card identity</b> (Enchantment — Aura, {2}{G}, green) is materialised
///   from the embedded JSON definition (<c>frantic-strength.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, matching the JSON-driven aura
///   posture of <see cref="RancorFactory"/> / <see cref="EtherealArmorFactory"/>.
/// - <b>Flash (CR 702.8)</b> — carried as a <see cref="Majik.Core.Abilities.KeywordAbility"/>
///   marker via the JSON <c>keywords</c> array (same wiring as
///   <see cref="CogworkWrestlerFactory"/>); the keyword lets the Aura be cast at
///   instant speed by the timing rules.
/// - <b>Static "+2/+2 and has trample"</b> — a single
///   <see cref="AttachedBoostEffect"/> carrying both the Layer 7c +2/+2 pump
///   and the Layer 6 Trample grant (CR 613). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically and gates on the Aura being
///   on the battlefield AND attached (its <c>IsActive</c> check). The granted
///   "Trample" keyword is the marker consumed by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>.
/// - <b>Enchant creature</b> — the standard bare card-type clause
///   (CR 702.5b / 303.4c), built through the shared
///   <see cref="AuraSpellDefinitionBuilder"/>.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload produces the correct
/// card shape only (no live continuous effect) — suitable for factory-shape /
/// dispatch tests. The two-arg overload registers the static boost.
/// </summary>
[CardName("Frantic Strength")]
public static class FranticStrengthFactory
{
    public const string CardName = "Frantic Strength";
    public const string Slug = "frantic-strength";
    public const string Cost = "{2}{G}";
    public const int PowerBoost = 2;
    public const int ToughnessBoost = 2;

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Flash\n" +
        "Enchant creature\n" +
        "Enchanted creature gets +2/+2 and has trample.";

    /// <summary>Granted keyword on the enchanted creature: Trample
    /// (CR 702.19).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "Trample" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Constructs a Frantic Strength with card identity (incl. the Flash
    /// keyword marker from the JSON) only — no live continuous effect.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Frantic Strength. When <paramref name="continuousEffects"/>
    /// is supplied, the +2/+2 boost plus the Trample grant is registered against
    /// the service; gated on the aura being on the battlefield AND attached
    /// (effect's <c>IsActive</c> check).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);

        if (continuousEffects != null)
        {
            // CR 613 — single AttachedBoostEffect carries both the Layer 7c
            // +2/+2 pump and the Layer 6 Trample grant.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Frantic Strength —
    /// "Enchant creature" → a single creature target (CR 702.5b / 303.4c). On
    /// resolution the Aura enters the battlefield already attached to the chosen
    /// creature (CR 303.4f).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: p => p.HasType(CardType.Creature));
    }
}
