using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice another creature" — activated-ability cost that requires the
/// controller to sacrifice a creature other than the ability's source.
///
/// Implements <see cref="ICost"/> so it can slot directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list.
///
/// ## Deferred (v1 gaps)
/// - <see cref="Target"/> must be set by the agent before <see cref="Pay"/>
///   is called; otherwise the first eligible creature is chosen
///   deterministically (alphabetical-ish, whatever <c>GetCards()</c> order
///   returns). Full agent-driven target prompting requires the ITarget /
///   TargetResolver infrastructure (deferred — same gap as WalkingBallista
///   ping targeting).
/// </summary>
public sealed class SacrificeAnotherCreatureCost : ICost
{
    private readonly Permanent _self;

    /// <summary>
    /// Optionally set by the agent to indicate which creature to sacrifice.
    /// When null the cost falls back to the first eligible creature on the
    /// controller's battlefield (deterministic v1 behaviour).
    /// </summary>
    public Creature? Target { get; set; }

    public SacrificeAnotherCreatureCost(Permanent self)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
    }

    public string Description =>
        $"sacrifice a creature other than {_self.Name}";

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => !ReferenceEquals(c, _self));
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target ?? player.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => !ReferenceEquals(c, _self));

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: no eligible creature to sacrifice.");

        player.Zones.Battlefield.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }
}
