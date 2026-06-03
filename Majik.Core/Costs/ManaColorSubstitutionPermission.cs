using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// The class of payment a <see cref="ManaColorSubstitutionPermission"/> widens.
///
/// A "you may spend mana as though it were mana of any color" permission
/// (CR 609.4b) is always scoped to a specific kind of payment — Robber of the
/// Rich scopes it to casting a specific stolen card, Agatha's Soul Cauldron
/// scopes it to activating abilities of creatures the controller controls,
/// Fist of Suns / Cascading Cataracts widen casting in general. This enum names
/// those scopes so the payment path can ask "may this player spend any colour
/// <em>for this purpose</em>?" without over-applying the relaxation.
/// </summary>
public enum ManaSpendPurpose
{
    /// <summary>Activating activated abilities of creatures the player controls
    /// (Agatha's Soul Cauldron).</summary>
    ActivateCreatureAbilities,

    /// <summary>Casting spells (Fist of Suns, Cascading Cataracts, …).</summary>
    CastSpells,
}

/// <summary>
/// CR 609.4b — a reusable payment-time "you may spend mana as though it were
/// mana of any color" static permission.
///
/// This is the generalization of the one-off clause Robber of the Rich carries
/// on its runtime exile-cast grant (<see cref="Card.RuntimeExileCastSpendAsAnyColor"/>):
/// instead of a per-card boolean stamped on an exiled card, this is a static
/// ability a permanent contributes while on the battlefield (CR 604.1). The
/// mana-payment path consults <see cref="PlayerMaySpendAnyColorFor"/> and, when
/// it returns <c>true</c>, folds the cost's coloured pips into generic
/// (<see cref="ValueObjects.ManaCost.WithColoredFoldedToGeneric"/>) — the exact
/// same permissive read-side relaxation the
/// <see cref="ManaPaymentResolver.Pay(Player, ValueObjects.ManaCost, Players.Agents.ManaPayment, bool)"/>
/// <c>spendAsAnyColor</c> seam applies for Robber. The cost's mana value is
/// unchanged (CR 106.6); only which mana qualifies for a coloured pip widens.
///
/// Consumers:
/// - <see cref="ManaColorSubstitutableManaCost"/> — an <see cref="ICost"/> that
///   honours the permission when paid from a player's pool.
/// - Agatha's Soul Cauldron contributes one with
///   <see cref="ManaSpendPurpose.ActivateCreatureAbilities"/>.
/// </summary>
public sealed class ManaColorSubstitutionPermission : IStaticAbility
{
    /// <summary>The payment scope this permission widens (CR 609.4b).</summary>
    public ManaSpendPurpose Purpose { get; }

    public object Source { get; }
    public Player Controller { get; }
    public string Description { get; }

    public ManaColorSubstitutionPermission(
        object source, Player controller, ManaSpendPurpose purpose)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Purpose = purpose;
        Description =
            "You may spend mana as though it were mana of any color " +
            $"({purpose}).";
    }

    /// <summary>
    /// CR 604.1 — a static ability applies only while its source is on the
    /// battlefield. Mirrors <see cref="StaticAbility.IsActive"/>'s default.
    /// </summary>
    public bool IsActive() =>
        Source is Permanent permanent && permanent.Zone == ZoneType.Battlefield;

    /// <summary>
    /// Continuous effect is implicit (the payment path reads the permission);
    /// nothing to apply imperatively. Present to satisfy
    /// <see cref="IStaticAbility"/>.
    /// </summary>
    public void ApplyEffect()
    {
        // No imperative application — payment code queries this permission.
    }

    /// <summary>
    /// Does <paramref name="player"/> currently have an active
    /// <see cref="ManaColorSubstitutionPermission"/> for
    /// <paramref name="purpose"/>? Scans the abilities of every permanent the
    /// player controls on their battlefield (CR 604.1 — only battlefield
    /// permanents contribute static abilities). Returns <c>true</c> if any such
    /// permission is active.
    /// </summary>
    public static bool PlayerMaySpendAnyColorFor(Player player, ManaSpendPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(player);

        foreach (var card in player.Zones.Battlefield.GetCards())
        {
            foreach (var ability in card.Abilities)
            {
                if (ability is ManaColorSubstitutionPermission perm
                    && perm.Purpose == purpose
                    && perm.IsActive())
                {
                    return true;
                }
            }
        }

        return false;
    }
}
