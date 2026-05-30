using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sentinel's Eyes (Theros Beyond Death, {W}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-05-29):
///   "Enchant creature
///    Enchanted creature gets +1/+1 and has vigilance.
///    Escape—{W}, Exile two other cards from your graveyard. (You may cast
///    this card from your graveyard for its escape cost.)"
///
/// Sentinel's Eyes pairs two analogue shapes already in the engine:
/// - <b>Static +N/+N + keyword-grant Aura body</b> — same shape as
///   <see cref="CripplingBlightFactory"/> (generic "Enchant creature"
///   targeting) and <see cref="DaybreakCoronetFactory"/> (the
///   <see cref="AttachedBoostEffect"/> grantedKeywords path).
/// - <b>Escape (CR 702.138)</b> — same <see cref="EscapeAlternativeCost"/>
///   wiring as <see cref="ClingToDustFactory"/>.
///
/// The base card shape (name / Enchantment — Aura / {W} cost) is
/// materialised from the embedded JSON definition (<c>sentinels-eyes.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the continuous boost is layered
/// on here because the JSON <c>AbilityDefinition</c> schema expresses neither
/// the attached-boost static nor Escape yet (same posture as
/// <see cref="ArdentPleaFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Aura shape</b> at printed cost {W} (mana value 1).
/// - <b>Cast-time targeting</b> via <see cref="AuraSpellDefinitionBuilder"/>:
///   "Enchant creature" → any creature on the battlefield is a legal target
///   (CR 702.5b). BotIntent.Buff signals the buff intent.
/// - <b>Static "+1/+1 and has vigilance"</b> via a single
///   <see cref="AttachedBoostEffect"/> carrying both the Layer 7c P/T bump
///   (CR 613.3c) and the Layer 6 Vigilance keyword grant (CR 702.21). The
///   effect reads <see cref="Permanent.AttachedTo"/> dynamically so the boost
///   transfers cleanly if the aura is ever re-attached. Registered against
///   the supplied <see cref="ContinuousEffectsService"/> when provided.
/// - <b>Escape (CR 702.138)</b> via <see cref="EscapeAlternativeCost"/>:
///   printed escape cost {W}, exile two OTHER cards from your graveyard.
///   <see cref="BuildAlternativeCost"/> returns the bound alt-cost; Escape
///   only changes how the aura is cast (from the graveyard), not its
///   on-resolution attach/boost behaviour.
/// </summary>
[CardName("Sentinel's Eyes")]
public static class SentinelsEyesFactory
{
    public const string CardName = "Sentinel's Eyes";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "sentinels-eyes";

    public const string PrintedManaCost = "{W}";
    public const int PowerBoost = 1;
    public const int ToughnessBoost = 1;

    /// <summary>Granted keyword on the enchanted creature: Vigilance
    /// (CR 702.21).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "Vigilance" };

    /// <summary>CR 702.138 — printed Escape mana cost: {W}.</summary>
    public const string EscapeManaCost = "{W}";

    /// <summary>CR 702.138a — Escape rider: exile two OTHER cards from your
    /// graveyard.</summary>
    public const int EscapeExileCount = 2;

    /// <summary>
    /// CR 702.138 — Sentinel's Eyes' printed Escape alt-cost ({W}, exile two
    /// OTHER graveyard cards). Mana cost replaces the printed {W}; the
    /// aura's attach/boost behaviour on resolution is unchanged.
    /// </summary>
    public static EscapeAlternativeCost BuildAlternativeCost() =>
        new(ValueObjects.ManaCost.Parse(EscapeManaCost), EscapeExileCount);

    /// <summary>
    /// Constructs a Sentinel's Eyes with card identity only (no live
    /// continuous effect). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Sentinel's Eyes. When <paramref name="continuousEffects"/>
    /// is supplied, the +1/+1 + Vigilance boost is registered against the
    /// service; gated on the aura being on the battlefield AND attached
    /// (effect's <c>IsActive</c> check).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Enchantment — Aura / {W}) from the embedded JSON.
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
            // CR 613 — single AttachedBoostEffect carries both the Layer 7c
            // +1/+1 P/T bump and the Layer 6 Vigilance keyword grant.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Sentinel's Eyes.
    /// "Enchant creature" — any creature on the supplied battlefield is a
    /// legal target (CR 702.5b). BotIntent.Buff signals the buff attachment.
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
