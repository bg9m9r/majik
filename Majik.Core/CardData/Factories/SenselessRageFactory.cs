using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Senseless Rage (Shadows over Innistrad, {1}{R}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature
///    Enchanted creature gets +2/+2.
///    Madness {1}{R}"
///
/// ## Implementation
///
/// - Aura subtype + {1}{R} mana cost, red.
/// - Cast-time targeting via <see cref="AuraSpellDefinitionBuilder"/>:
///   "Enchant creature" → any creature on the battlefield is a legal
///   target (CR 702.5b). BotIntent.Buff signals the buff intent.
/// - Static "enchanted creature gets +2/+2" while Senseless Rage is on the
///   battlefield AND attached, via a single
///   <see cref="AttachedBoostEffect"/>(+2, +2) registered at Layer 7c
///   (CR 613 Layer 7c). The effect's <c>IsActive</c> check gates on both the
///   aura being on the battlefield AND having a non-null
///   <see cref="Permanent.AttachedTo"/>, so the boost evaporates the moment
///   the aura leaves play or is unattached.
///
///   This is the same shape as <see cref="DeadWeightFactory"/> /
///   <see cref="MadcapSkillsFactory"/>, but a +2/+2 buff with no granted
///   keyword.
///
/// ## Madness — intrinsic, NOT wired here (CR 702.35)
///
/// Madness {1}{R} works for every catalogued card via
/// <c>Majik.Core/Keywords/MadnessCatalog.cs</c> (name → cost) consulted by
/// the central discard funnel (<c>Fx.DiscardCard</c>). Senseless Rage is
/// catalogued at {1}{R}, so the "Madness {1}{R}" line is honoured
/// intrinsically on any real discard — no per-card replacement wiring is
/// needed (or wanted) on this factory.
///
/// ## Prod wiring (the seam this factory closes)
///
/// The +2/+2 boost only registers when a live
/// <see cref="ContinuousEffectsService"/> is supplied. The <b>production</b>
/// card-build path (<c>DeckCardBuilder</c> → <c>NamedCardFactory.Create(name,
/// owner, effects)</c> → <c>CreateGeneratedWithEffects</c>) dispatches to the
/// two-parameter <see cref="Create(Player, ContinuousEffectsService?)"/>
/// overload — the source generator only recognises a
/// <c>Create(Player, ContinuousEffectsService)</c> shape as the effects-aware
/// overload. Exposing exactly that overload is what threads the live per-game
/// effects service into the Aura's ETB-attach resolution, so the static buff
/// registers in real matches (not just under a test-supplied service).
/// </summary>
[CardName("Senseless Rage")]
public static class SenselessRageFactory
{
    public const string CardName = "Senseless Rage";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>CR 613 Layer 7c — printed power/toughness bonus: +2/+2.</summary>
    public const int PowerBoost = 2;
    public const int ToughnessBoost = 2;

    /// <summary>
    /// Constructs Senseless Rage with card identity only (no continuous
    /// effect registered). Suitable for shape/dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> card-build path routes to.
    /// When <paramref name="continuousEffects"/> is supplied, the +2/+2 buff
    /// is registered against the service (Layer 7c per CR 613); gated on the
    /// aura being on the battlefield AND attached (the effect's own
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
            // CR 613 Layer 7c — single AttachedBoostEffect carrying the
            // +2/+2 P/T modification. Reads Permanent.AttachedTo dynamically,
            // so the boost transfers cleanly if the aura is re-attached and is
            // inert while unattached or off the battlefield.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Senseless Rage.
    /// "Enchant creature" — any creature on the supplied battlefield is a
    /// legal target (CR 702.5b). BotIntent.Buff signals that this is a buff
    /// attachment.
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
            intent: BotIntent.Buff);
    }
}
