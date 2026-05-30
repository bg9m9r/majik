using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ethereal Armor (Return to Ravnica, {W}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-05-29):
///   "Enchant creature
///    Enchanted creature gets +1/+1 for each enchantment you control and
///    has first strike."
///
/// A white-weenie / Bogles staple: a one-mana aura whose pump scales with
/// the controller's enchantment count and grants first strike. Combines the
/// dynamic-N "for each &lt;type&gt; you control" boost shape of
/// <see cref="CranialPlatingFactory"/> with the aura/attach + granted-keyword
/// wiring of <see cref="DaybreakCoronetFactory"/>.
///
/// ## Implementation
///
/// - Card identity (Enchantment — Aura, {W}, white color indicator) is
///   materialised from the embedded JSON definition (<c>ethereal-armor.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, matching the JSON-driven aura
///   posture of <see cref="UtopiaSprawlFactory"/>.
/// - <b>Static "+N/+N for each enchantment you control" + first strike</b> —
///   a single dynamic-N <see cref="AttachedBoostEffect"/> (CR 613 Layer 7c for
///   P/T, Layer 6 for the granted keyword). The closure samples the
///   controller's live enchantment count at each layer pass via
///   <see cref="CountEnchantments"/>. The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically so re-attaching transfers
///   the boost without re-registration, and gates on the Armor being on the
///   battlefield AND attached (its <c>IsActive</c> check).
/// - <b>Self-counts</b>: the printed "enchantment you control" makes no
///   "other" carve-out, so the Armor counts itself once it is on the
///   battlefield (same posture as Cranial Plating counting itself among
///   artifacts).
/// - <b>Enchant creature</b> — the standard bare card-type clause. The
///   cast-time predicate is the generic "creature" filter (CR 702.5b /
///   303.4c), built through the shared <see cref="AuraSpellDefinitionBuilder"/>.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service wiring
/// and produces the correct card shape only — suitable for factory-shape /
/// dispatch tests. The two-arg overload registers the dynamic boost.
/// </summary>
[CardName("Ethereal Armor")]
public static class EtherealArmorFactory
{
    public const string CardName = "Ethereal Armor";
    public const string Slug = "ethereal-armor";
    public const string Cost = "{W}";

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Enchant creature\n" +
        "Enchanted creature gets +1/+1 for each enchantment you control and " +
        "has first strike.";

    /// <summary>Granted keyword on the enchanted creature: First Strike
    /// (CR 702.7).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "First Strike" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Constructs an Ethereal Armor with card identity only (no live
    /// continuous effect). Suitable for shape / dispatcher tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs an Ethereal Armor. When <paramref name="continuousEffects"/>
    /// is supplied, the dynamic +N/+N boost (where N = the controller's live
    /// enchantment count) plus the First Strike grant is registered against the
    /// service; gated on the aura being on the battlefield AND attached.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);

        if (continuousEffects != null)
        {
            // CR 613 — a single dynamic-N AttachedBoostEffect carries both the
            // Layer 7c +N/+N pump (N = controller's enchantment count, sampled
            // at each layer pass) and the Layer 6 First Strike grant.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                powerFn: () => CountEnchantments(card),
                toughnessFn: () => CountEnchantments(card),
                grantedKeywords: GrantedKeywords));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Ethereal Armor —
    /// "Enchant creature" → a single creature target (CR 702.5b / 303.4c). On
    /// resolution the Armor enters the battlefield already attached to the
    /// chosen creature (CR 303.4f).
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

    /// <summary>
    /// Live count of enchantment permanents on the Armor's CURRENT
    /// controller's battlefield (CR 613 Layer 7c input). Reads the controller
    /// dynamically (not at factory-construction time) so a control-change
    /// effect re-targets the count correctly. Defaults to 0 when the Armor has
    /// no live controller (off-battlefield / orphaned) so the boost gates
    /// cleanly via <see cref="AttachedBoostEffect.IsActive"/>. The Armor counts
    /// itself — the printed "enchantment you control" has no "other" carve-out.
    /// </summary>
    public static int CountEnchantments(Permanent armor)
    {
        var ctrl = armor.Controller ?? armor.Owner;
        if (ctrl == null) return 0;
        return ctrl.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Enchantment));
    }
}
