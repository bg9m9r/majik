using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inquisitor's Flail (Innistrad, {2}).
///
/// Artifact — Equipment. Oracle text:
///   "If equipped creature would deal combat damage, it deals double that
///    damage instead."
///   "If a source would deal combat damage to equipped creature, it deals
///    double that damage instead."
///   "Equip {2}."
///
/// ## Implemented (v1)
/// - Card identity (Artifact + Equipment subtype, mana cost {2}, owner /
///   controller wiring).
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive (PR #471) with the
///   Puresteel zero-equip cost-provider hook. Sorcery-speed gate,
///   "creature you control" target gathering, and attach-on-resolve are
///   encapsulated by the primitive; same wiring as Bone Saw / Colossus
///   Hammer / Skullclamp / Jitte / Sword of Fire and Ice.
///
/// ## Deferred (v1 gaps)
/// - <b>Damage-doubling replacement</b> (CR 614 / CR 615) — both printed
///   replacements ("If equipped creature would deal combat damage, it deals
///   double that damage instead" and the symmetric incoming clause).
///   <see cref="Majik.Core.Effects.ReplacementBus"/> + <see cref="Majik.Core.Effects.DamageIntent"/>
///   are the right entry point — combat damage already flows through
///   <c>CombatFlow.Apply{ToCreature|ToPlaneswalker|ToPlayer}</c> as a
///   <c>DamageIntent</c> — but the intent today carries no
///   <c>IsCombatDamage</c> discriminator (CR 510.1c), so a clean
///   "combat-damage only" filter would either misfire on non-combat damage
///   from creature sources (e.g. {T} ping abilities reusing the bus) or
///   require widening the intent record. Same deferred bucket as
///   Sword of Fire and Ice's DEBT-A protection scoping when this factory
///   first shipped — the structural shape (Equipment {2} + Equip {2}) is
///   live so Stoneforge Mystic can tutor it, Puresteel zero-equip flows
///   through, and the attach mechanics work; the damage doubling rider is
///   pending an <c>IsCombat</c> flag on <c>DamageIntent</c>.
/// </summary>
[CardName("Inquisitor's Flail")]
public static class InquisitorsFlailFactory
{
    public const string CardName = "Inquisitor's Flail";
    public const string Cost = "{2}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Construct Inquisitor's Flail. Single overload — no
    /// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> wiring is
    /// required because the damage-doubling replacements are deferred (no
    /// continuous effect to register today).
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive. Threads the Puresteel zero-
        // cost provider hook (same wiring as the rest of the equipment
        // cycle).
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
