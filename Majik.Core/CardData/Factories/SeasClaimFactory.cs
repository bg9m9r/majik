using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sea's Claim (Onslaught).
///
/// Enchantment — Aura — {U}
/// Oracle text:
///   "Enchant land
///    Enchanted land is an Island."
///
/// ## Implementation
///
/// Same shape as <see cref="SpreadingSeasFactory"/> minus the ETB draw
/// trigger: a single <see cref="AttachedAuraRetypeStaticEffect"/> wired
/// against the aura's <see cref="Permanent.AttachedTo"/> slot, retyping
/// the enchanted land to {Island}. PR #155's
/// <see cref="EffectiveManaAbilities"/> derives {T}: Add {U} on the
/// retyped land automatically (CR 305.6).
///
/// ## Deferred (v1 gaps)
/// - <b>Cast-time targeting</b>: see Spreading Seas — the spell-cast +
///   declare-target → attach flow is not wired engine-wide. Tests
///   manually <see cref="Permanent.AttachTo"/> after putting both
///   permanents onto the battlefield.
/// </summary>
[CardName("Sea's Claim")]
public static class SeasClaimFactory
{
    public const string CardName = "Sea's Claim";
    public const string Cost = "{U}";

    /// <summary>
    /// Printed oracle text. <see cref="AuraEnchantClauseParser"/> derives
    /// the cast-time target predicate from the "Enchant land" line.
    /// </summary>
    public const string OracleText =
        "Enchant land\n" +
        "Enchanted land is an Island.";

    private static readonly IReadOnlySet<CardSubtype> IslandOnly =
        new HashSet<CardSubtype> { CardSubtype.Island };

    /// <summary>
    /// Creates a Sea's Claim with correct card identity only (no live
    /// Layer 4 effect). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Sea's Claim. When <paramref name="effects"/>
    /// is supplied, an <see cref="AttachedAuraRetypeStaticEffect"/> is
    /// attached so the Layer 4 effect registers/unregisters as the aura
    /// enters/leaves the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            Cost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new AttachedAuraRetypeStaticEffect(
                card,
                effects,
                eventBus,
                newLandSubtypes: IslandOnly);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Sea's Claim
    /// — "Enchant land" → single <see cref="Land"/> target. The effect
    /// attaches the aura to the chosen land on resolution so the
    /// downstream layer effect can scope its retype before the
    /// battlefield zone-move fires.
    ///
    /// CR 303.4f — Auras enter the battlefield attached to their target.
    /// </summary>
    /// <param name="aura">The Sea's Claim permanent being cast.</param>
    /// <param name="battlefield">Current battlefield permanents — the
    /// candidate pool is filtered to those that are Lands.</param>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        // CR 702.5b — derive the target predicate from the printed
        // "Enchant land" oracle clause rather than hand-wiring it.
        return AuraSpellDefinitionBuilder.ForAuraFromOracle(
            aura,
            OracleText,
            battlefield);
    }
}
