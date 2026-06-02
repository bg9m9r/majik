using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spirit Mantle (New Phyrexia, {1}{W}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-06-02):
///   "Enchant creature
///    Enchanted creature gets +1/+1 and has protection from creatures."
///
/// A Bogles-style protection aura: a fixed +1/+1 pump plus protection from
/// creatures (CR 702.16), which — among other things — makes the enchanted
/// creature unblockable by creatures (CR 509.1b / 702.16e). Combines the
/// fixed +X/+X aura posture of <see cref="EtherealArmorFactory"/> /
/// <see cref="CartoucheOfSolidarityFactory"/> with the dynamic
/// protection-grant wiring of <see cref="SwordOfFireAndIceFactory"/>
/// (a Layer-6 <see cref="GrantAbilityEffect"/> re-projecting a
/// <see cref="ProtectionAbility"/> onto the live enchanted creature).
///
/// ## Implementation
///
/// - Card identity (Enchantment — Aura, {1}{W}, white color indicator) is
///   materialised from the embedded JSON definition (<c>spirit-mantle.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, matching the JSON-driven aura
///   posture of <see cref="EtherealArmorFactory"/>.
/// - <b>Static "+1/+1"</b> — a single <see cref="AttachedBoostEffect"/> at
///   Layer 7c (CR 613). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically so re-attaching transfers
///   the boost without re-registration, and gates on the Mantle being on the
///   battlefield AND attached (its <c>IsActive</c> check). Unlike Ethereal
///   Armor's first-strike rider, the keyword half here is NOT a printed
///   keyword on the computed characteristics — protection is a
///   <see cref="ProtectionAbility"/> marker (see below), so it is granted as
///   a real ability, not a keyword string.
/// - <b>"Protection from creatures"</b> — CR 702.16. With a
///   <see cref="ContinuousEffectsService"/> wired, a Layer-6
///   <see cref="GrantAbilityEffect"/> re-projects
///   <see cref="ProtectionAbility"/>("creatures") onto the live enchanted
///   creature; the selector reads <see cref="Permanent.AttachedTo"/> at sync
///   time so re-attaching transfers the grant and an LTB / Humility-class
///   effect revokes it via the service's grant lifecycle (CR 613.6e). The
///   quality string "creatures" is the bucket
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromCardType"/>
///   reads for <see cref="CardType.Creature"/>. The shape-only path (no
///   service) leaves the marker on the Mantle card itself so factory-shape /
///   dispatch tests still get a deterministic answer — same posture as
///   <see cref="SwordOfFireAndIceFactory"/>.
/// - <b>"Enchant creature"</b> — the standard bare card-type clause. The
///   cast-time predicate is the generic "creature" filter (CR 702.5b /
///   303.4c), built through the shared <see cref="AuraSpellDefinitionBuilder"/>.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits service wiring
/// and produces the correct card shape only (with the protection marker on the
/// card itself) — suitable for factory-shape / dispatch tests. The two-arg
/// overload registers the +1/+1 boost and the live protection grant.
/// </summary>
[CardName("Spirit Mantle")]
public static class SpiritMantleFactory
{
    public const string CardName = "Spirit Mantle";
    public const string Slug = "spirit-mantle";
    public const string PrintedManaCost = "{1}{W}";

    public const int PowerBoost = 1;
    public const int ToughnessBoost = 1;

    /// <summary>CR 702.16 quality bucket for protection from creatures —
    /// the plural <see cref="Majik.Core.Rules.Protection.HasProtectionFromCardType"/>
    /// matches for <see cref="CardType.Creature"/>.</summary>
    public const string ProtectionQuality = "creatures";

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Enchant creature\n" +
        "Enchanted creature gets +1/+1 and has protection from creatures.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Constructs a Spirit Mantle with card identity + the protection marker on
    /// the card itself, but no live continuous effect registered. Suitable for
    /// shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Spirit Mantle. When <paramref name="continuousEffects"/> is
    /// supplied, the +1/+1 boost (Layer 7c) and the protection-from-creatures
    /// grant (Layer 6) are registered against the service; both gate on the
    /// Mantle being on the battlefield AND attached, and the protection grant's
    /// selector reads <see cref="Permanent.AttachedTo"/> so the marker lives on
    /// the enchanted creature (which is what CR 702.16e reads). The shape-only
    /// path (no service) leaves the protection marker on the Mantle card so the
    /// helper still returns a deterministic answer.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Static: "Enchanted creature gets +1/+1." CR 613 Layer 7c.
        // Inert while unattached / off the battlefield.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost));
        }

        // ----------------------------------------------------------------
        // "Enchanted creature ... has protection from creatures." CR 702.16,
        // CR 613.1f. With a service wired, a Layer-6 ability-grant effect
        // re-projects ProtectionAbility("creatures") onto the live enchanted
        // creature; the selector reads card.AttachedTo at sync time so
        // re-attaching transfers the grant and the service's grant lifecycle
        // revokes it on LTB / Humility (CR 613.6e). Shape-only path keeps the
        // marker on the Mantle card itself for deterministic dispatch tests —
        // same posture as Sword of Fire and Ice.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility(ProtectionQuality)));
        }
        else
        {
            card.AddAbility(new ProtectionAbility(ProtectionQuality));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Spirit Mantle —
    /// "Enchant creature" → a single creature target (CR 702.5b / 303.4c). On
    /// resolution the Mantle enters the battlefield already attached to the
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
}
