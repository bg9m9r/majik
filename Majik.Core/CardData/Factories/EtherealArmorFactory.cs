using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ethereal Armor (Return to Ravnica, {W}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature"
///   "Enchanted creature gets +1/+1 for each enchantment you control and
///    has first strike."
///
/// ## Implementation
///
/// - Aura subtype + <c>{W}</c> cost; ETB-attach plumbing via the standard
///   <see cref="AuraSpellDefinitionBuilder"/> path
///   (<see cref="BuildSpellDefinition"/>).
/// - <b>Static "+N/+N where N = enchantments you control" + First
///   Strike</b> — single dynamic-N
///   <see cref="AttachedBoostEffect"/> (CR 613 Layer 7c for the P/T bump
///   + Layer 6 for the granted keyword list). The closure samples
///   <see cref="CountEnchantments"/> on the armor's current controller at
///   each layer pass, mirroring <see cref="CranialPlatingFactory"/>'s
///   live-controller pattern so a controller-change effect
///   (Threads of Disloyalty / Mind Control) re-targets the count.
///   Ethereal Armor itself is an enchantment on the battlefield, so it
///   counts toward its own boost when active — printed text says
///   "enchantment you control" with no "other" carve-out (CR 700.6),
///   same posture as Cranial Plating's self-count.
/// - <b>First Strike grant</b> — bundled into the same
///   <see cref="AttachedBoostEffect"/>'s <c>grantedKeywords</c> list
///   (CR 702.7). The effect's <c>IsActive</c> gates on the aura being
///   on the battlefield AND attached, so the keyword falls off cleanly
///   when the armor leaves play or gets unattached.
///
/// ## Lifecycle
///
/// Single-arg <see cref="Create(Player)"/> omits the
/// <see cref="ContinuousEffectsService"/> wiring — shape-only path for
/// factory-dispatch / identity tests; no live boost or first-strike
/// projection. Use <see cref="Create(Player, ContinuousEffectsService?)"/>
/// for runtime wiring.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Phased-out / face-down enchantment nuances</b> — the count
///   closure scans the controller's battlefield top-level for any
///   permanent with <c>CardType.Enchantment</c>. Phased-out enchantments
///   (CR 702.26) and face-down morph enchantments would currently
///   miscount; same gap as Cranial Plating's metalcraft predicate.
/// </summary>
[CardName("Ethereal Armor")]
public static class EtherealArmorFactory
{
    public const string CardName = "Ethereal Armor";
    public const string PrintedManaCost = "{W}";

    /// <summary>Granted keywords on the enchanted creature: First Strike
    /// (CR 702.7).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "First Strike" };

    /// <summary>Printed oracle text — source of truth for the single-noun
    /// "Enchant creature" clause routed through
    /// <see cref="AuraEnchantClauseParser"/>.</summary>
    public const string OracleText =
        "Enchant creature\n" +
        "Enchanted creature gets +1/+1 for each enchantment you control " +
        "and has first strike.";

    /// <summary>
    /// Constructs an Ethereal Armor with card identity only (no live
    /// continuous effect). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs an Ethereal Armor. When
    /// <paramref name="continuousEffects"/> is supplied, the dynamic-N
    /// +N/+N boost + First Strike grant are registered against the
    /// service in a single <see cref="AttachedBoostEffect"/>; gated on
    /// the armor being on the battlefield AND attached (effect's
    /// <c>IsActive</c> check).
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
            // CR 613 Layer 7c — dynamic-N P/T bump sampled at each layer
            // pass; granted keywords (First Strike) applied alongside.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                powerFn: () => CountEnchantments(card),
                toughnessFn: () => CountEnchantments(card),
                grantedKeywords: GrantedKeywords));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Ethereal
    /// Armor. The printed "Enchant creature" clause routes through
    /// <see cref="AuraSpellDefinitionBuilder.ForAuraFromOracle"/>, which
    /// parses the noun and filters the battlefield to creatures.
    /// CR 303.4f — on resolve, the armor enters the battlefield already
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

    /// <summary>
    /// Live count of enchantment permanents on the armor's CURRENT
    /// controller's battlefield. Reads the controller dynamically (not
    /// at factory-construction time) so a controller-change effect
    /// re-targets the count correctly. Defaults to 0 when the armor has
    /// no live controller (off-battlefield / orphaned) so the boost
    /// gates cleanly via <see cref="AttachedBoostEffect.IsActive"/>.
    /// </summary>
    public static int CountEnchantments(Permanent armor)
    {
        var ctrl = armor.Controller ?? armor.Owner;
        if (ctrl == null) return 0;
        return ctrl.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Enchantment));
    }
}
