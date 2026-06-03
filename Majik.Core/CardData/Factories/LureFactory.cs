using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lure (Alpha et al., {1}{G}{G}).
///
/// Enchantment — Aura. Oracle text (Scryfall, verified 2026-06-02):
///   "Enchant creature"
///   "All creatures able to block enchanted creature do so."
///
/// The canonical "force the whole board to gang-block one creature" aura —
/// stick it on a trampler / deathtoucher and the opponent must throw every
/// untapped able creature in front of it (CR 509.1c / 509.1g).
///
/// ## Implementation — the aura/equipment keyword-grant rail
///
/// - <b>Card identity</b> (Enchantment — Aura, {1}{G}{G}) is built directly in
///   C# (no JSON def), mirroring <see cref="PacifismFactory"/>.
/// - <b>"All creatures able to block enchanted creature do so"</b> — granted
///   to the enchanted host as the <c>"MustBeBlockedByAllAble"</c> marker
///   keyword via a single <see cref="AttachedBoostEffect"/> carrying only a
///   granted keyword (0/0 boost). The effect reads the aura's
///   <see cref="Permanent.AttachedTo"/> dynamically and gates on the aura being
///   on the battlefield AND attached (its <c>IsActive</c> check), so the grant
///   registers on attach and is revoked on detach / host-leaves (CR 613.1f /
///   613.6e). The granted marker is the SAME keyword that Breaker of Armies
///   carries as a printed <see cref="Majik.Core.Abilities.KeywordAbility"/>, so
///   <see cref="Majik.Core.Combat.CombatAbilities.MustBeBlockedByAllAble"/>
///   reads it through <c>Compute(...).Keywords</c> and the must-block overload
///   of <c>CombatValidator.IsValidBlockDeclaration</c> enforces it (CR 509.1c).
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service wiring
/// and produces the correct card shape only (factory-shape / dispatch tests).
/// Use the two-arg overload to register the keyword grant against a live
/// <see cref="ContinuousEffectsService"/>.
///
/// CR rule references: 303.4 (Auras), 702.5b (Enchant creature target),
/// 509.1c (block requirement — "able to block ~ do so"), 509.1g (maximising
/// satisfied requirements), 613.1f / 613.6e (Layer-6 keyword grant + revoke).
/// </summary>
[CardName("Lure")]
public static class LureFactory
{
    public const string CardName = "Lure";
    public const string PrintedManaCost = "{1}{G}{G}";

    /// <summary>The marker keyword granted to the enchanted creature — the
    /// same one Breaker of Armies carries printed (CR 509.1c).</summary>
    public const string GrantedKeyword = "MustBeBlockedByAllAble";

    private static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { GrantedKeyword };

    /// <summary>
    /// Constructs a Lure with card identity only (no live keyword grant).
    /// Suitable for factory-shape / dispatch tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Lure. When <paramref name="continuousEffects"/> is
    /// supplied, the "all creatures able to block enchanted creature do so"
    /// keyword grant (CR 509.1c, Layer-6 marker via
    /// <see cref="AttachedBoostEffect"/>) is registered against the service;
    /// it gates on the aura being on the battlefield AND attached. When null,
    /// the grant is skipped.
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
            // CR 509.1c / 613.1f — grant the MustBeBlockedByAllAble marker to
            // the enchanted creature. No P/T change; keyword-grant only.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: 0,
                toughness: 0,
                grantedKeywords: GrantedKeywords));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Lure —
    /// "Enchant creature" → any creature on the supplied battlefield is a legal
    /// target (CR 702.5b). On resolution the aura enters the battlefield
    /// already attached to the chosen creature (CR 303.4f). BotIntent.Buff
    /// signals the gang-block setup.
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
            predicate: static p => p.HasType(CardType.Creature),
            intent: BotIntent.Buff);
    }
}
