using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghostfire Blade (Khans of Tarkir, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2."
///   "Equip {3}"
///   "This Equipment's equip ability costs {2} less to activate if it
///    targets a colorless creature."
///
/// Mechanically a flat +2/+2 equip in the same family as
/// <see cref="BonesplitterFactory"/> (+2/+0, Equip {1}) — differing in the
/// boost shape (+2/+2 vs +2/+0), the printed equip cost ({3} vs {1}), and a
/// target-color-dependent equip cost reduction unique to colorless-matters
/// shells (Eldrazi / artifact creatures).
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR
///   613 Layer 7c). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///   transfers the boost without re-registration. Gated on the Blade being
///   on the battlefield AND attached.
/// - <b>Equip {3}, reduced by {2} if it targets a colorless creature</b> —
///   activated ability (CR 702.6 / 702.6e cost-modification) wired via the
///   <see cref="EquipActivatedAbility"/> primitive. The printed {3} is the
///   ability's <see cref="EquipActivatedAbility.EquipCost"/>; the conditional
///   reduction is applied through the ability's
///   <see cref="EquipActivatedAbility.CostProvider"/> hook (CR 118.5 —
///   cost-reduction at activation), which inspects the chosen equip target's
///   colour at cost-pay time. CR 117.7c — only the generic component is
///   reduced; the reduction floors at zero.
///
/// ## Cost reduction (CR 118.5 / 117.7c)
///
/// At <see cref="EquipActivatedAbility.CostProvider"/> consult time the
/// chosen equip target is read off the Blade's own
/// <see cref="EquipActivatedAbility"/> (the same channel
/// <c>AttachOnResolve</c> uses). If that target's effective colours
/// (<see cref="Permanent.GetEffectiveColors"/>, CR 105.2 / 202.2) are empty
/// — i.e. the creature is colorless — the printed {3} is reduced by {2} to
/// an effective {1}; otherwise the printed {3} stands. When no target is yet
/// chosen (shape inspection / pre-target legality probe) the printed cost is
/// returned unchanged, so tooltips and the
/// <see cref="EquipActivatedAbility.EquipCost"/> assertion always read {3}.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +2/+2 boost is
/// registered immediately; its <c>IsActive</c> gates on the Blade being on
/// the battlefield AND attached to a battlefield permanent, so an unequipped
/// (or off-battlefield) Blade silently contributes nothing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service wiring
/// and produces the correct card shape only — suitable for factory-shape /
/// dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b) —
///   v1 picks the first controller-side creature deterministically
///   (inherited from <see cref="EquipActivatedAbility"/>).
/// - <b>Puresteel zero-equip interaction</b> — the colorless reduction does
///   NOT compose with the Puresteel zero-equip provider here (only one
///   cost-provider hook exists per ability); v1 honours the colorless
///   reduction, which is the printed Ghostfire Blade rider. Stacking
///   external static equip-cost reducers onto this card is out of scope.
/// </summary>
[CardName("Ghostfire Blade")]
public static class GhostfireBladeFactory
{
    public const string CardName = "Ghostfire Blade";
    public const string PrintedManaCost = "{1}";
    public const string EquipCost = "{3}";

    /// <summary>The generic-mana reduction applied when the equip target is
    /// a colorless creature (CR 118.5).</summary>
    public const int ColorlessEquipDiscount = 2;

    /// <summary>
    /// Constructs a Ghostfire Blade with no live continuous-effects wiring
    /// (the shape / dispatcher path). The Equip activated ability is attached
    /// (including the colorless cost reduction) but the +2/+2 boost is not
    /// registered against any <see cref="ContinuousEffectsService"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Ghostfire Blade. When <paramref name="continuousEffects"/>
    /// is supplied, the static +2/+2 boost (Layer 7c) is registered against
    /// it; the effect is gated on the Blade being on the battlefield and
    /// attached to a battlefield permanent.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +2/+2."
        // Gates on the source being on the battlefield AND attached
        // (see AttachedBoostEffect.IsActive). CR 613 Layer 7c.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
        }

        // --------------------------------------------------------------
        // Equip {3} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive. The colorless-target {2}-less
        // rider (CR 118.5) is applied through the CostProvider hook, which
        // is consulted at cost-pay time when the chosen target is known.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: ColorlessReducedEquipCost);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// CR 118.5 / 117.7c — effective equip cost provider. Returns the printed
    /// {3} reduced by {2} (generic, floored at zero) when the chosen equip
    /// target is a colorless creature; otherwise returns the printed {3}.
    ///
    /// The chosen target is read off the Blade's own
    /// <see cref="EquipActivatedAbility"/> — the same channel the resolve-time
    /// attach uses — so the reduction reflects the actual announced target.
    /// Before a target is chosen (shape probe / pre-announce) the printed cost
    /// is returned unchanged.
    /// </summary>
    public static ManaCost ColorlessReducedEquipCost(Permanent source)
    {
        var printed = ManaCost.Parse(EquipCost);
        if (source == null) return printed;

        var target = ChosenEquipTarget(source);
        if (target == null) return printed;

        // CR 105.2 / 202.2 — a creature is colorless iff its effective colour
        // set is empty.
        var colours = target.GetEffectiveColors();
        if (colours.Count != 0) return printed;

        // CR 117.7c — reduce only the generic component; floor at zero.
        return printed.WithGeneric(printed.Generic - ColorlessEquipDiscount);
    }

    /// <summary>
    /// Reads the currently-chosen equip target off the Blade's
    /// <see cref="EquipActivatedAbility"/>, mirroring the resolve-time
    /// attach picker. Returns null when no target has been announced.
    /// </summary>
    private static Creature? ChosenEquipTarget(Permanent source)
    {
        foreach (var ability in source.Abilities)
        {
            if (ability is EquipActivatedAbility eq
                && ReferenceEquals(eq.Source, source)
                && eq.ChosenTargets.Count > 0
                && eq.ChosenTargets[0].Count > 0
                && eq.ChosenTargets[0][0] is Creature chosen)
            {
                return chosen;
            }
        }

        return null;
    }
}
