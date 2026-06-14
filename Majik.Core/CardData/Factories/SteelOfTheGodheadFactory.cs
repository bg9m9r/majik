using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steel of the Godhead (Shadowmoor, {2}{W/U}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-06-14):
///   "Enchant creature
///    As long as enchanted creature is white, it gets +1/+1 and has lifelink.
///    (Damage dealt by the creature also causes its controller to gain that
///    much life.)
///    As long as enchanted creature is blue, it gets +1/+1 and can't be
///    blocked."
///
/// Reuses analogue shapes already in the engine:
/// - <b>Aura cast-time targeting</b> ("Enchant creature") via
///   <see cref="AuraSpellDefinitionBuilder"/> — same as
///   <see cref="SentinelsEyesFactory"/> / <see cref="CripplingBlightFactory"/>.
/// - <b>Colour-conditional static +1/+1 (+ lifelink) boost</b> via
///   <see cref="AttachedColorConditionalBoostEffect"/> — the colour-gated
///   sibling of <see cref="AttachedBoostEffect"/>. The gate reads the
///   enchanted creature's effective colour (CR 105.3 / 613.1e) so a creature
///   that is BOTH white and blue gets +2/+2 (both clauses apply
///   independently, CR 613) plus lifelink and can't-be-blocked.
/// - <b>"Can't be blocked" (blue clause)</b> via a predicate-mode
///   <see cref="CombatRestrictionEffect"/> — same combat-restriction surface
///   as <see cref="BlightedAgentFactory"/>, but the predicate gates on
///   "is the enchanted creature AND is currently blue" so the restriction
///   tracks re-attachment and colour changes (CR 509.1c).
///
/// The base shape (name / Enchantment — Aura / {2}{W/U}) is materialised from
/// the embedded JSON (<c>steel-of-the-godhead.json</c>); the continuous
/// effects are layered here because the JSON AbilityDefinition schema does not
/// yet express colour-conditional attached boosts (same posture as
/// <see cref="SentinelsEyesFactory"/>).
/// </summary>
[CardName("Steel of the Godhead")]
public static class SteelOfTheGodheadFactory
{
    public const string CardName = "Steel of the Godhead";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "steel-of-the-godhead";

    public const string PrintedManaCost = "{2}{W/U}";
    public const int PowerBoost = 1;
    public const int ToughnessBoost = 1;

    /// <summary>White clause grant: Lifelink (CR 702.15).</summary>
    public static readonly IReadOnlyList<string> WhiteGrantedKeywords =
        new[] { "Lifelink" };

    /// <summary>
    /// Constructs a Steel of the Godhead with card identity only (no live
    /// continuous effects). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Steel of the Godhead. When
    /// <paramref name="continuousEffects"/> is supplied the colour-conditional
    /// boosts + the blue "can't be blocked" restriction are registered against
    /// the service (each gated on the aura being on the battlefield AND
    /// attached, plus the enchanted creature having the matching colour).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }

        if (continuousEffects != null)
        {
            // CR 613 — white clause: +1/+1 (Layer 7c) and lifelink (Layer 6)
            // while the enchanted creature is white. The keyword grant is a
            // SEPARATE Layer-6 effect so it surfaces through EffectiveKeywords
            // (which stops at Layer 6); the P/T bump rides Layer 7c.
            continuousEffects.Register(new AttachedColorConditionalBoostEffect(
                source: card,
                requiredColor: ManaColor.White,
                power: PowerBoost,
                toughness: ToughnessBoost));
            continuousEffects.Register(new AttachedColorConditionalBoostEffect(
                source: card,
                requiredColor: ManaColor.White,
                grantedKeywords: WhiteGrantedKeywords,
                layer: Layer.Abilities));

            // CR 613 — blue clause: +1/+1 while the enchanted creature is blue.
            // (Both clauses apply independently — a W+U creature gets +2/+2.)
            continuousEffects.Register(new AttachedColorConditionalBoostEffect(
                source: card,
                requiredColor: ManaColor.Blue,
                power: PowerBoost,
                toughness: ToughnessBoost));

            // CR 509.1c — blue clause: "can't be blocked." Predicate-mode
            // restriction gating on the enchanted creature being blue, so it
            // tracks re-attachment and colour changes. IsActiveGate ties the
            // restriction to the aura being on the battlefield AND attached.
            continuousEffects.Register(new CombatRestrictionEffect(
                CombatRestriction.CannotBeBlocked,
                predicate: c =>
                    ReferenceEquals(card.AttachedTo, c)
                    && (c.ActiveEffects?.EffectiveColors(c)
                        ?? CardColors.GetColors(c)).Contains(ManaColor.Blue),
                isActiveGate: () =>
                    card.Zone == Majik.Core.Zones.ZoneType.Battlefield
                    && card.AttachedTo is { Zone: Majik.Core.Zones.ZoneType.Battlefield },
                expiresAtEndOfTurn: false));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Steel of the
    /// Godhead. "Enchant creature" — any creature on the supplied battlefield
    /// is a legal target (CR 702.5b). CR 303.4f — on resolve the aura enters
    /// the battlefield already attached to the chosen target.
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
