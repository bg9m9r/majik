using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Madcap Skills (Shadowmoor, {1}{R}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature
///    Enchanted creature gets +3/+0 and has menace."
///
/// ## Shape source
/// Card identity (name, {1}{R}, Enchantment — Aura, red) is loaded from
/// <c>Majik.Core/CardData/Cards/madcap-skills.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (same posture as
/// <see cref="CombatResearchFactory"/>). The continuous +3/+0-and-menace
/// boost is attached in code below — same shape as
/// <see cref="DaybreakCoronetFactory"/>'s single
/// <see cref="AttachedBoostEffect"/>.
///
/// ## Implementation
/// - Aura subtype + {1}{R} cost; ETB-attach plumbing via the standard
///   <see cref="AuraSpellDefinitionBuilder"/> path
///   (<see cref="BuildSpellDefinition"/>; "Enchant creature" — CR 702.5b
///   defines any creature as a legal target).
/// - Static "+3/+0 and has menace" via a single
///   <see cref="AttachedBoostEffect"/> carrying both the P/T modification
///   (+3/+0 — CR 613 Layer 7c) and the granted Menace keyword (CR 702.111,
///   Layer 6). The effect reads <see cref="Permanent.AttachedTo"/>
///   dynamically so the boost transfers cleanly if the aura is re-attached
///   and is inert while the aura is unattached or off the battlefield.
/// </summary>
[CardName("Madcap Skills")]
public static class MadcapSkillsFactory
{
    public const string CardName = "Madcap Skills";

    /// <summary>CR 613 Layer 7c — printed power/toughness bonus: +3/+0.</summary>
    public const int PowerBoost = 3;
    public const int ToughnessBoost = 0;

    /// <summary>Granted keyword on the enchanted creature: Menace
    /// (CR 702.111). Title-case marker, matching the engine-wide Menace
    /// keyword string consumed by the combat system.</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "Menace" };

    public const string OracleText =
        "Enchant creature\n" +
        "Enchanted creature gets +3/+0 and has menace.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("madcap-skills");

    /// <summary>
    /// Constructs Madcap Skills with card identity only (no live continuous
    /// effect). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Madcap Skills. When <paramref name="continuousEffects"/> is
    /// supplied, the +3/+0-and-menace boost is registered against the
    /// service; gated on the aura being on the battlefield AND attached
    /// (effect's <c>IsActive</c> check).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613 — single AttachedBoostEffect carries both the Layer 7c
            // +3/+0 and the Layer 6 Menace keyword grant.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Madcap Skills.
    /// The printed "Enchant creature" line (CR 702.5b) makes any creature a
    /// legal target. Filters the supplied battlefield to creatures.
    /// CR 303.4f — on resolve, the aura enters the battlefield already
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
            predicate: p => p != null && p.HasType(CardType.Creature));
    }
}
