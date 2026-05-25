using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

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
/// ## Implementation
///
/// - Card identity (Artifact + Equipment subtype, mana cost {2}, owner /
///   controller wiring).
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive (PR #471) with the
///   Puresteel zero-equip cost-provider hook. Sorcery-speed gate,
///   "creature you control" target gathering, and attach-on-resolve are
///   encapsulated by the primitive; same wiring as Bone Saw / Colossus
///   Hammer / Skullclamp / Jitte / Sword of Fire and Ice.
/// - <b>Damage-doubling replacements</b> (CR 614 / CR 510.1c) — two
///   <see cref="DamageDoubleReplacement"/> registrations on the supplied
///   <see cref="ReplacementBus"/>:
///     1. <i>Source-side</i> — when the Flail's
///        <see cref="Permanent.AttachedTo"/> creature is the
///        <see cref="DamageIntent.Source"/> AND the intent is combat
///        damage, double the amount.
///     2. <i>Target-side</i> — when the Flail's
///        <see cref="Permanent.AttachedTo"/> creature is the
///        <see cref="DamageIntent.TargetCreature"/> AND the intent is
///        combat damage, double the amount.
///   The replacements gate on the Flail being on the battlefield AND
///   currently attached, so detach / blink / bounce automatically
///   suspends doubling without explicit deregistration. Non-combat
///   damage paths (spells, ping abilities) leave
///   <see cref="DamageIntent.IsCombatDamage"/> false and are skipped.
/// </summary>
[CardName("Inquisitor's Flail")]
public static class InquisitorsFlailFactory
{
    public const string CardName = "Inquisitor's Flail";
    public const string Cost = "{2}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs an Inquisitor's Flail with card identity + Equip {2}
    /// only — no damage-doubling replacements registered. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Constructs an Inquisitor's Flail. When <paramref name="replacements"/>
    /// is supplied, the two combat-damage doubling replacements
    /// (source-side + target-side) are registered against it; otherwise
    /// only the structural shape + Equip {2} are wired.
    /// </summary>
    public static Artifact Create(Player owner, ReplacementBus? replacements)
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

        // --------------------------------------------------------------
        // Damage-doubling replacements — gated on the Flail being on
        // the battlefield AND attached, then on the intent being combat
        // damage AND the equipped creature being the source / target.
        // --------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<DamageIntent>(
                new DamageDoubleReplacement(card, source: true));
            replacements.Register<DamageIntent>(
                new DamageDoubleReplacement(card, source: false));
        }

        return card;
    }
}

/// <summary>
/// CR 614 replacement: when a <see cref="DamageIntent"/> is combat damage
/// (CR 510.1) and the Flail's currently-equipped creature is either the
/// source or the target (selected by the <see cref="_sourceSide"/> ctor
/// flag), double the amount. Backs both printed Inquisitor's Flail
/// replacements.
///
/// Gates on the Flail sitting on the battlefield AND
/// <see cref="Permanent.AttachedTo"/> being non-null, so detach / blink /
/// bounce automatically suspend doubling without explicit
/// deregistration. Each registration is one-side-only (source xor
/// target) so <see cref="ReplacementBus"/>'s per-effect dedup (CR 616.1c)
/// still lets both replacements fire on a single intent when the same
/// creature deals combat damage to itself (e.g. mirror-match face-bite).
/// </summary>
public sealed class DamageDoubleReplacement : IReplacementEffect<DamageIntent>
{
    private readonly Permanent _equipment;
    private readonly bool _sourceSide;

    public DamageDoubleReplacement(Permanent equipment, bool source)
    {
        _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        _sourceSide = source;
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (!intent.IsCombatDamage) return false;
        if (intent.Amount <= 0) return false;
        if (_equipment.Zone != ZoneType.Battlefield) return false;

        var equipped = _equipment.AttachedTo;
        if (equipped == null) return false;

        if (_sourceSide)
        {
            // "If equipped creature would deal combat damage, it deals
            // double that damage instead."
            return ReferenceEquals(intent.Source, equipped);
        }

        // "If a source would deal combat damage to equipped creature,
        // it deals double that damage instead." — target-side gate only
        // fires when the equipped creature is the damage target.
        return intent.TargetCreature is not null
            && ReferenceEquals(intent.TargetCreature, equipped);
    }

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
        => intent with { Amount = intent.Amount * 2 };
}
