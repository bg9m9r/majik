using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sheltered by Ghosts (Duskmourn: House of Horror,
/// {1}{W}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-06-02):
///   "Enchant creature you control
///    When this Aura enters, exile target nonland permanent an opponent
///     controls until this Aura leaves the battlefield.
///    Enchanted creature gets +1/+0 and has lifelink and ward {2}."
///
/// A white aura that doubles as removal: it buffs your own creature and
/// simultaneously O-Rings an opposing problem permanent for as long as it
/// sticks around. Structurally it fuses three established shapes:
///
/// - the JSON-driven Aura identity + static
///   <see cref="AttachedBoostEffect"/> boost/keyword-grant posture of
///   <see cref="RancorFactory"/> / <see cref="DaybreakCoronetFactory"/>;
/// - the parameterised Ward {N} grant via <see cref="GrantAbilityEffect"/>
///   + <see cref="KeywordAbility"/>("Ward", arg) of
///   <see cref="LavaspurBootsFactory"/>;
/// - the "exile target nonland permanent an opponent controls until this
///   leaves" ETB / LTB exile-closure pair of
///   <see cref="BanishingLightFactory"/> (reused verbatim via
///   <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/> so the
///   exact O-Ring semantics — CR 608.2b legality re-check, CR 110.2 return
///   under owner's control — are shared, not re-implemented).
///
/// ## Implementation
///
/// - <b>Card identity</b> (Enchantment — Aura, {1}{W}, white color indicator)
///   is materialised from the embedded JSON definition
///   (<c>sheltered-by-ghosts.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>, matching the JSON-driven aura
///   posture of <see cref="RancorFactory"/>.
/// - <b>ETB exile + LTB return</b> (CR 701.21 / 603.6c) — wired through the
///   shared <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/>
///   closure: on ETB resolution, exile the targeted opponent nonland
///   permanent (re-checking CR 608.2b legality — still on the battlefield,
///   still nonland, still an opponent's); on LTB (this aura leaving the
///   battlefield by any route), return the exiled card to the battlefield
///   under its owner's control (CR 110.2). Two
///   <see cref="TriggeredAbility"/> instances are attached.
/// - <b>Static "+1/+0 and has lifelink"</b> — a single
///   <see cref="AttachedBoostEffect"/> carrying the Layer 7c +1/+0 pump and
///   the Layer 6 Lifelink grant (CR 613). Reads
///   <see cref="Permanent.AttachedTo"/> dynamically and gates on the aura
///   being on the battlefield AND attached (its <c>IsActive</c> check).
/// - <b>"and ward {2}"</b> (CR 702.21) — a Layer-6
///   <see cref="GrantAbilityEffect"/> projecting a parameterised
///   <see cref="KeywordAbility"/>("Ward", arg: 2) onto the live enchanted
///   creature, mirroring <see cref="LavaspurBootsFactory"/>. Ward is a marker
///   keyword across the engine; the parameterised grant projects the {2}
///   marker the spell-resolution consultation will read once that engine-wide
///   ward wiring lands (same deferred posture as Lavaspur Boots / Kappa
///   Cannoneer).
/// - <b>Enchant creature you control</b> — the cast-time predicate filters the
///   battlefield to creatures the aura's controller controls (CR 702.5b /
///   303.4c), built through the shared
///   <see cref="AuraSpellDefinitionBuilder"/>.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (both exile triggers are
/// attached for observability; the boost / ward grants are omitted) — suitable
/// for factory-shape / dispatch tests. The four-arg overload registers the
/// static boost + ward grant against a
/// <see cref="ContinuousEffectsService"/> and wires the exile triggers to a
/// <see cref="TriggerManager"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Ward {2} resolution</b> — the granted Ward marker is not yet consulted
///   by the spell-resolution path (engine-wide ward gap; tracked with the rest
///   of the marker-keyword ward cards).
/// </summary>
[CardName("Sheltered by Ghosts")]
public static class ShelteredByGhostsFactory
{
    public const string CardName = "Sheltered by Ghosts";
    public const string Slug = "sheltered-by-ghosts";
    public const string Cost = "{1}{W}";
    public const int PowerBoost = 1;
    public const int ToughnessBoost = 0;

    /// <summary>CR 702.21 — printed ward cost: {2}.</summary>
    public const int WardAmount = 2;

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Enchant creature you control\n" +
        "When this Aura enters, exile target nonland permanent an opponent " +
        "controls until this Aura leaves the battlefield.\n" +
        "Enchanted creature gets +1/+0 and has lifelink and ward {2}.";

    /// <summary>Granted keyword carried on the boost effect: Lifelink
    /// (CR 702.15). Ward {2} is granted separately as a parameterised
    /// KeywordAbility.</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "Lifelink" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Constructs Sheltered by Ghosts with card identity + the ETB / LTB exile
    /// triggers only (no live continuous effects, no TriggerManager wiring).
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Constructs Sheltered by Ghosts. When
    /// <paramref name="continuousEffects"/> is supplied, the +1/+0 boost plus
    /// the Lifelink grant (Layer 7c / 6) and the Ward {2} grant (Layer 6) are
    /// registered against the service; each gates on the aura being on the
    /// battlefield AND attached. When <paramref name="triggers"/> is supplied,
    /// the ETB exile + LTB return triggers are registered so the bus drives
    /// them via <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB exile + LTB return — reuse Banishing Light's shared closure
        // (CR 701.21 / 603.6c). Identical "exile target nonland permanent an
        // opponent controls until this leaves" semantics — captures one
        // exiled card per ETB and returns it on LTB under its owner's
        // control (CR 110.2). Attaches the two TriggeredAbility instances.
        // ----------------------------------------------------------------
        BanishingLightFactory.WireExileEnchantmentTriggers(card, owner, triggers);

        if (continuousEffects != null)
        {
            // CR 613 — single AttachedBoostEffect carries the Layer 7c +1/+0
            // pump and the Layer 6 Lifelink grant.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));

            // CR 702.21 — grant Ward {2} as a parameterised marker keyword
            // (CR 613.1f, Layer 6). Mirrors Lavaspur Boots; reads
            // AttachedTo at sync time so re-attach transfers the grant.
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility(
                        "Ward", bearer, bearer.Controller ?? owner, arg: WardAmount)));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Sheltered by
    /// Ghosts. The printed "Enchant creature you control" clause restricts the
    /// legal target set to creatures the aura's controller controls
    /// (CR 702.5b / 303.4c). On resolution the aura enters the battlefield
    /// already attached to the chosen creature (CR 303.4f).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        // CR 109.4 / 702 — "you control" is relative to the aura's controller
        // (its owner before it has a distinct controller).
        var controller = aura.Controller ?? aura.Owner;

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature you control",
            battlefield: battlefield,
            predicate: p => p.HasType(CardType.Creature)
                            && ReferenceEquals(p.Controller, controller));
    }
}
