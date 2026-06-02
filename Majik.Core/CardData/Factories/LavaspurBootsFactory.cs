using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lavaspur Boots (Outlaws of Thunder Junction, {1}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-01):
///   "Equipped creature gets +1/+0 and has haste and ward {1}.
///    (Whenever it becomes the target of a spell or ability an opponent
///     controls, counter it unless that player pays {1}.)"
///   "Equip {1}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch (counters,
/// draw, scry/surveil, stub damage, …). It has NO equip ability, NO dynamic
/// attached-boost effect, and NO attached keyword-grant — a JSON def alone
/// produces only a vanilla Artifact shell (the shipped
/// <c>lavaspur-boots.json</c> mirrors <c>nettlecyst.json</c> /
/// <c>blade-of-the-bloodchief.json</c>, which carry zero abilities). The
/// functioning equipment analogues (<see cref="ColossusHammerFactory"/>,
/// <see cref="SwordOfFireAndIceFactory"/>, <see cref="HammerOfNazahnFactory"/>,
/// <see cref="NettlecystFactory"/>) are themselves hand-rolled for exactly
/// this reason, so Lavaspur Boots follows that established pattern.
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +1/+0"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c). The
///   effect reads the source's <see cref="Permanent.AttachedTo"/>
///   dynamically, so re-equipping transfers the boost without
///   re-registration. Mirrors <see cref="ColossusHammerFactory"/> /
///   <see cref="SwordOfFireAndIceFactory"/>.
/// - <b>"has haste"</b> (CR 702.10) — a Layer-6 <see cref="GrantAbilityEffect"/>
///   (CR 613.1f) re-projecting a fresh <see cref="KeywordAbility"/>("Haste")
///   onto the live equipped creature. The selector reads
///   <see cref="Permanent.AttachedTo"/> at sync time, so re-equipping
///   transfers the grant; LTB / detach revoke it via the service's grant
///   lifecycle. <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/>
///   reads the granted marker through the computed keyword set.
/// - <b>"has ward {1}"</b> (CR 702.21) — a Layer-6
///   <see cref="GrantAbilityEffect"/> projecting a parameterised
///   <see cref="KeywordAbility"/>("Ward", arg: 1) onto the equipped
///   creature. Ward is currently a marker keyword across the engine (the
///   <see cref="Majik.Core.Keywords.WardEffect"/> trigger primitive exists
///   as a stand-alone check but is not yet wired into the spell-resolution
///   path — same treatment as <see cref="KappaCannoneerFactory"/> and the
///   rest of the ward cards), so the boots project the same {1} marker the
///   resolution-path consultation will read once that wiring lands.
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). The boost / haste / ward grants are not registered against any
/// <see cref="ContinuousEffectsService"/> on that path. Use the two-arg
/// overload to wire the continuous effects.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of
///   the equipment cycle).
/// - <b>Ward {1} resolution</b> — the granted Ward marker is not yet
///   consulted by the spell-resolution path (engine-wide ward gap; tracked
///   with the rest of the marker-keyword ward cards).
/// </summary>
[CardName("Lavaspur Boots")]
public static class LavaspurBootsFactory
{
    public const string CardName = "Lavaspur Boots";
    public const string Cost = "{1}";
    public const string EquipCost = "{1}";

    /// <summary>CR 702.21 — printed ward cost: {1}.</summary>
    public const int WardAmount = 1;

    /// <summary>
    /// Constructs Lavaspur Boots with no live continuous-effects wiring (the
    /// shape / dispatcher path). Neither the +1/+0 boost nor the haste / ward
    /// grants are registered against any service.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Lavaspur Boots. When <paramref name="continuousEffects"/>
    /// is supplied the static +1/+0 boost (Layer 7c) and the haste / ward {1}
    /// grants (Layer 6) are registered against it; each gates on the Boots
    /// being on the battlefield AND attached to a battlefield permanent. When
    /// null, all three are skipped.
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
        // "Equipped creature gets +1/+0 and has haste and ward {1}."
        // All three gate on the source being on the battlefield AND
        // attached (effect IsActive checks / GrantAbilityEffect selector).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            // CR 613 Layer 7c — P/T modification.
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: 0));

            // CR 702.10 — grant Haste (CR 613.1f, Layer 6 ability-adding).
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility("Haste", bearer, bearer.Controller ?? owner)));

            // CR 702.21 — grant Ward {1} as a parameterised marker keyword
            // (CR 613.1f, Layer 6). Ward is currently a marker across the
            // engine; the WardEffect resolution primitive is not yet wired
            // into spell resolution (same gap as Kappa Cannoneer et al.).
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    new KeywordAbility("Ward", bearer, bearer.Controller ?? owner, arg: WardAmount)));
        }

        // --------------------------------------------------------------
        // Equip {1} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
