using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, sacrifice a creature."
/// Caller specifies WHICH creature; this class just performs the
/// payment + validation.
/// </summary>
public sealed class SacrificeCreatureCost : IAdditionalCost
{
    private readonly Creature _target;

    public SacrificeCreatureCost(Creature target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public string Description => $"sacrifice {_target.Name}";

    public bool CanPay(Player caster) =>
        ReferenceEquals(_target.Controller, caster)
        && _target.Zone == ZoneType.Battlefield
        && _target.HasType(CardType.Creature);

    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;
        caster.Zones.Battlefield.RemoveCard(_target);
        caster.Zones.Graveyard.AddCard(_target);
        _target.Zone = ZoneType.Graveyard;
        return true;
    }
}
