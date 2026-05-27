using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dead Weight (Innistrad, {B}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature."
///   "Enchanted creature gets -2/-2."
///
/// ## Implementation
///
/// - Aura subtype + {B} mana cost.
/// - Cast-time targeting via <see cref="AuraSpellDefinitionBuilder"/>:
///   "Enchant creature" → any creature on the battlefield is a legal
///   target (CR 702.5b). BotIntent.Removal signals the debuff intent.
/// - Static "enchanted creature gets -2/-2" while Dead Weight is on the
///   battlefield and attached, via a single
///   <see cref="AttachedBoostEffect"/>(-2, -2) registered at Layer 7c
///   (CR 613.3c). The effect's <c>IsActive</c> check gates on both the
///   aura being on the battlefield AND having a non-null
///   <see cref="Permanent.AttachedTo"/>, so the debuff evaporates the
///   moment the aura leaves play or is unattached.
/// - No keywords are granted (Dead Weight only modifies P/T).
/// </summary>
[CardName("Dead Weight")]
public static class DeadWeightFactory
{
    public const string CardName = "Dead Weight";
    public const string PrintedManaCost = "{B}";
    public const int PowerModifier = -2;
    public const int ToughnessModifier = -2;

    /// <summary>
    /// Constructs a Dead Weight with card identity only (no continuous
    /// effect registered). Suitable for shape/dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Dead Weight. When
    /// <paramref name="continuousEffects"/> is supplied, the -2/-2
    /// debuff is registered against the service (Layer 7c per CR 613.3c).
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
            // CR 613.3c — Layer 7c P/T modification.
            // AttachedBoostEffect(-2, -2) reduces the enchanted creature's
            // power and toughness by 2 each while Dead Weight is on the
            // battlefield and attached (IsActive check inside the effect).
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerModifier,
                toughness: ToughnessModifier));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Dead Weight.
    /// "Enchant creature" — any creature on the supplied battlefield is a
    /// legal target (CR 702.5b). BotIntent.Removal signals that this is a
    /// debuff attachment.
    /// CR 303.4f — on resolve the aura enters the battlefield already
    /// attached to the chosen target.
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
            intent: BotIntent.Removal);
    }
}
