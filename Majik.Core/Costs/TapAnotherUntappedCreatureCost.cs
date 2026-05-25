using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Tap an untapped creature you control" — activated-ability cost that
/// taps a creature OTHER than the ability's source as an additional cost
/// (CR 118.12 — "tap" symbol on a non-source object is a tap-as-cost).
///
/// Companion to <see cref="SacrificeAnotherCreatureCost"/> — same shape
/// (deterministic-first-eligible fallback when <see cref="Target"/> is
/// null) but mutates state via <see cref="Permanent.Tap"/> instead of
/// zone movement.
///
/// Springleaf Drum is the canonical consumer: <c>{T}, Tap an untapped
/// creature you control: Add one mana of any color.</c> The drum's own
/// <c>{T}</c> is handled by <see cref="Majik.Core.Abilities.ManaAbility"/>;
/// this cost expresses the second tap.
///
/// ## Deferred (v1 gaps)
/// - <see cref="Target"/> must be set by the agent before <see cref="Pay"/>
///   is called; otherwise the first eligible untapped creature is chosen
///   deterministically. Same gap as
///   <see cref="SacrificeAnotherCreatureCost"/>'s fallback (and the rest
///   of the additional-cost family).
/// - Summoning-sickness restriction (CR 302.1) is honoured — Magic's "tap
///   this creature" cost cannot be paid by a creature its controller has
///   not controlled since their most recent turn began, and a tap-as-cost
///   on an attached creature counts as that creature being the source of
///   the tap. <see cref="Creature.HasSummoningSickness"/> gates eligibility.
/// </summary>
public sealed class TapAnotherUntappedCreatureCost : ICost
{
    private readonly Permanent _self;

    /// <summary>
    /// Optionally set by the agent to indicate which creature to tap.
    /// When null the cost falls back to the first eligible untapped
    /// creature on the controller's battlefield (deterministic v1).
    /// </summary>
    public Creature? Target { get; set; }

    public TapAnotherUntappedCreatureCost(Permanent self)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
    }

    public string Description =>
        $"tap an untapped creature you control other than {_self.Name}";

    private static bool IsEligible(Creature c, Permanent self) =>
        !ReferenceEquals(c, self)
        && !c.IsTapped
        && !c.HasSummoningSickness;

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => IsEligible(c, _self));
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target != null && IsEligible(Target, _self)
            ? Target
            : player.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .FirstOrDefault(c => IsEligible(c, _self));

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: no eligible untapped creature.");

        pick.Tap();
    }
}
