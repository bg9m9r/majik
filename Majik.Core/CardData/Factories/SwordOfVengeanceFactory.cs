using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Vengeance (Magic 2013 / Future Sight, {3}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-23):
///   "Equipped creature gets +2/+0 and has first strike, vigilance,
///    trample, and haste."
///   "Equip {3}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch (counters,
/// draw, scry/surveil, stub damage, …). It has NO equip ability, NO dynamic
/// attached-boost effect, and NO attached keyword-grant — a JSON def alone
/// produces only a vanilla Artifact shell (the shipped
/// <c>sword-of-vengeance.json</c> mirrors <c>lavaspur-boots.json</c> /
/// <c>sword-of-the-meek.json</c>, which carry zero abilities). Every
/// functioning pure-stats+keywords equipment analogue
/// (<see cref="LavaspurBootsFactory"/>, <see cref="ColossusHammerFactory"/>,
/// <see cref="SwordOfFireAndIceFactory"/>) is hand-rolled for exactly this
/// reason, so Sword of Vengeance follows that established pattern.
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+0"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c). The
///   effect reads the source's <see cref="Permanent.AttachedTo"/>
///   dynamically, so re-equipping transfers the boost without
///   re-registration. Mirrors <see cref="LavaspurBootsFactory"/> /
///   <see cref="SwordOfFireAndIceFactory"/>.
/// - <b>"has first strike, vigilance, trample, and haste"</b>
///   (CR 702.7 / 702.20 / 702.19 / 702.10) — four Layer-6
///   <see cref="GrantAbilityEffect"/> instances (CR 613.1f) each
///   re-projecting a fresh <see cref="KeywordAbility"/> onto the live
///   equipped creature. Each selector reads
///   <see cref="Permanent.AttachedTo"/> at sync time, so re-equipping
///   transfers the grants; LTB / detach revoke them via the service's grant
///   lifecycle. The combat-keyword lookups in
///   <see cref="Majik.Core.Combat.CombatAbilities"/> read the granted markers
///   through the computed keyword set. The canonical keyword strings
///   ("First strike", "Vigilance", "Trample", "Haste") match the
///   <see cref="Majik.Core.Combat.CombatAbilities"/> probes exactly.
/// - <b>Equip {3}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). The boost / keyword grants are not registered against any
/// <see cref="ContinuousEffectsService"/> on that path. Use the two-arg
/// overload to wire the continuous effects.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of
///   the equipment cycle).
/// </summary>
[CardName("Sword of Vengeance")]
public static class SwordOfVengeanceFactory
{
    public const string CardName = "Sword of Vengeance";
    public const string Cost = "{3}";
    public const string EquipCost = "{3}";

    /// <summary>
    /// The four keyword grants — canonical strings matching the
    /// <see cref="Majik.Core.Combat.CombatAbilities"/> probes
    /// (CR 702.7 / 702.20 / 702.19 / 702.10).
    /// </summary>
    private static readonly string[] GrantedKeywords =
        { "First strike", "Vigilance", "Trample", "Haste" };

    /// <summary>
    /// Constructs Sword of Vengeance with no live continuous-effects wiring
    /// (the shape / dispatcher path). Neither the +2/+0 boost nor the keyword
    /// grants are registered against any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Sword of Vengeance. When <paramref name="continuousEffects"/>
    /// is supplied the static +2/+0 boost (Layer 7c) and the first strike /
    /// vigilance / trample / haste grants (Layer 6) are registered against it;
    /// each gates on the Sword being on the battlefield AND attached to a
    /// battlefield permanent. When null, all are skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // "Equipped creature gets +2/+0 and has first strike, vigilance,
        //  trample, and haste."
        // The boost + all four grants gate on the source being on the
        // battlefield AND attached (effect IsActive checks /
        // GrantAbilityEffect selector reads card.AttachedTo at sync time).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            // CR 613 Layer 7c — P/T modification.
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 0));

            // CR 702.7 / 702.20 / 702.19 / 702.10 — grant the four evergreen
            // combat keywords (CR 613.1f, Layer 6 ability-adding). Each grant
            // re-projects a fresh KeywordAbility onto the live equipped
            // creature; the selector reads card.AttachedTo so re-equipping
            // transfers them and detach revokes them.
            foreach (var keyword in GrantedKeywords)
            {
                var kw = keyword; // capture per-iteration
                continuousEffects.Register(new GrantAbilityEffect(
                    source: card,
                    targetSelector: () => card.AttachedTo,
                    abilityFactory: bearer =>
                        new KeywordAbility(kw, bearer, bearer.Controller ?? owner)));
            }
        }

        // --------------------------------------------------------------
        // Equip {3} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook for cycle parity.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
