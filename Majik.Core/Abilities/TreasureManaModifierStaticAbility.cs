using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// A continuous static ability that modifies how the OTHER Treasure tokens a
/// player controls produce mana — Goldspan Dragon's
/// "Treasures you control have '{T}, Sacrifice this artifact: Add two mana of
/// any one color.'" (CR 611.2 — a continuous effect granting a replacement
/// ability/value to a set of permanents). Instead of adding a whole new
/// mana-ability subsystem this rides the existing
/// <see cref="Majik.Core.Mana"/> / <see cref="ManaAbility"/> path: a Treasure's
/// per-colour mana ability uses a dynamic <c>Func&lt;ManaCost&gt;</c> generator
/// that, at activation time (CR 605.1 — mana abilities resolve immediately,
/// no stack), consults <see cref="IsActiveFor"/> to see whether the producing
/// player currently controls any active modifier of this shape and, if so,
/// produces <see cref="ManaMultiplier"/>× the printed amount.
///
/// <para>This is a marker static — it carries no <c>ApplyEffect</c> body of its
/// own; the modification is pulled by the Treasure's generator rather than
/// pushed onto every Treasure (which would require re-binding their abilities
/// on every board change). The continuous effect's lifetime is the source
/// permanent's battlefield presence (CR 604.2): <see cref="IsActive"/> returns
/// false once the source leaves the battlefield, so a Treasure tapped after
/// Goldspan dies produces its base amount again — no register/unregister churn
/// across token creation.</para>
///
/// <para>The multiplier (not a flat "+1") models the printed wording: Goldspan
/// replaces "one mana" with "two mana", i.e. doubles. Two Goldspan Dragons do
/// NOT stack to four — the printed ability is a fixed value ("two"), and the
/// presence of any qualifying modifier sets the produced amount to that value
/// (CR 613.2; later-applied identical continuous effects of this shape
/// overwrite rather than compound). <see cref="IsActiveFor"/> therefore returns
/// a boolean presence, not a count.</para>
/// </summary>
public sealed class TreasureManaModifierStaticAbility : IStaticAbility
{
    /// <summary>
    /// The amount each unit of a modified Treasure's mana is replaced with
    /// (Goldspan = 2: "Add two mana of any one color").
    /// </summary>
    public int ManaMultiplier { get; }

    public object Source { get; }
    public Player Controller { get; }
    public string Description { get; }

    public TreasureManaModifierStaticAbility(
        object source,
        Player controller,
        int manaMultiplier = 2,
        string? description = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        if (manaMultiplier < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(manaMultiplier), manaMultiplier, "Multiplier must be ≥ 1.");
        }

        ManaMultiplier = manaMultiplier;
        Description =
            description
            ?? "Treasures you control have \"{T}, Sacrifice this artifact: "
               + "Add two mana of any one color.\"";
    }

    /// <summary>
    /// CR 604.2 — the continuous effect is active only while the source
    /// permanent is on the battlefield. A non-permanent source (test
    /// scaffolding) is always active.
    /// </summary>
    public bool IsActive()
    {
        if (Source is Permanent permanent)
        {
            return permanent.Zone == ZoneType.Battlefield;
        }

        return true;
    }

    /// <summary>Marker static — no push-side effect body (see type remarks).</summary>
    public void ApplyEffect()
    {
    }

    /// <summary>
    /// The mana-production multiplier the Treasures <paramref name="controller"/>
    /// controls should currently use, given the modifiers that player controls
    /// (CR 611.2 — "Treasures YOU control"). Returns the largest active
    /// modifier's <see cref="ManaMultiplier"/> (so a Goldspan = 2), or 1 when
    /// the player controls no active modifier — the base printed amount.
    /// Identical-shape modifiers overwrite rather than compound (CR 613.2), so
    /// two Goldspans still give 2, not 4.
    /// </summary>
    public static int ManaMultiplierFor(Player? controller)
    {
        if (controller == null)
        {
            return 1;
        }

        var multiplier = 1;
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card is not Permanent permanent)
            {
                continue;
            }

            foreach (var ability in permanent.Abilities)
            {
                if (ability is TreasureManaModifierStaticAbility modifier
                    && modifier.IsActive()
                    && modifier.ManaMultiplier > multiplier)
                {
                    multiplier = modifier.ManaMultiplier;
                }
            }
        }

        return multiplier;
    }

    /// <summary>
    /// Whether <paramref name="controller"/> currently controls any active
    /// Treasure-mana modifier of this shape.
    /// </summary>
    public static bool IsActiveFor(Player? controller) =>
        ManaMultiplierFor(controller) > 1;
}
