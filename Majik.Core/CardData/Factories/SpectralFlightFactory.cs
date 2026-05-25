using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spectral Flight (Magic 2014, {1}{U}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature"
///   "Enchanted creature gets +2/+2 and has flying."
///
/// ## Implementation
///
/// - Aura subtype + <c>{1}{U}</c> cost; ETB-attach plumbing via the
///   standard <see cref="AuraSpellDefinitionBuilder"/> path
///   (<see cref="BuildSpellDefinition"/>).
/// - <b>Static "+2/+2 + Flying" boost</b> — single
///   <see cref="AttachedBoostEffect"/> carrying both the P/T modification
///   (+2/+2, CR 613 Layer 7c) and the Flying keyword grant (CR 702.9 /
///   Layer 6). Reads the source's <see cref="Permanent.AttachedTo"/>
///   dynamically so the boost transfers cleanly if the aura is ever
///   re-attached. Same shape as <see cref="DaybreakCoronetFactory"/>.
///
/// ## Lifecycle
///
/// Single-arg <see cref="Create(Player)"/> omits the
/// <see cref="ContinuousEffectsService"/> wiring — shape-only path for
/// factory-dispatch / identity tests; no live boost or Flying grant
/// projection. Use <see cref="Create(Player, ContinuousEffectsService?)"/>
/// for runtime wiring.
/// </summary>
[CardName("Spectral Flight")]
public static class SpectralFlightFactory
{
    public const string CardName = "Spectral Flight";
    public const string PrintedManaCost = "{1}{U}";
    public const int PowerBoost = 2;
    public const int ToughnessBoost = 2;

    /// <summary>Granted keywords on the enchanted creature: Flying
    /// (CR 702.9).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "Flying" };

    /// <summary>Printed oracle text — source of truth for the single-noun
    /// "Enchant creature" clause routed through
    /// <see cref="AuraEnchantClauseParser"/>.</summary>
    public const string OracleText =
        "Enchant creature\n" +
        "Enchanted creature gets +2/+2 and has flying.";

    /// <summary>
    /// Constructs a Spectral Flight with card identity only (no live
    /// continuous effect). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Spectral Flight. When
    /// <paramref name="continuousEffects"/> is supplied, the +2/+2 +
    /// Flying boost is registered against the service in a single
    /// <see cref="AttachedBoostEffect"/>; gated on the aura being on
    /// the battlefield AND attached (effect's <c>IsActive</c> check).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613 — single AttachedBoostEffect carries both the
            // Layer 7c +2/+2 bump and the Layer 6 Flying grant.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Spectral
    /// Flight. The printed "Enchant creature" clause routes through
    /// <see cref="AuraSpellDefinitionBuilder.ForAuraFromOracle"/>, which
    /// parses the noun and filters the battlefield to creatures.
    /// CR 303.4f — on resolve, the aura enters the battlefield already
    /// attached to the chosen creature.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAuraFromOracle(
            aura, OracleText, battlefield);
    }
}
