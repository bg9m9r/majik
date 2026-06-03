using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice a [basic-land-subtype]" additional cost. Used by Lava Dart's
/// Flashback ("Sacrifice a Mountain"), Mox Diamond, Daze etc. when the
/// non-mana cost is sacrificing a specific basic-land type.
///
/// CR 701.16 (sacrifice) + CR 305.6 (basic land subtypes are Plains,
/// Island, Swamp, Mountain, Forest, Wastes).
///
/// Caller must pre-pick the land to sacrifice — auto-pick is intentionally
/// avoided so multi-land boards keep agent control over which Mountain
/// dies (e.g. shock lands vs basic Mountains).
/// </summary>
public sealed class SacrificeBasicLandCost : IAdditionalCost
{
    /// <summary>The chosen land to sacrifice.</summary>
    public ICard Target { get; }

    /// <summary>The required basic-land subtype.</summary>
    public CardSubtype RequiredSubtype { get; }

    private readonly IEventBus? _eventBus;

    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) on payment so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    public SacrificeBasicLandCost(ICard target, CardSubtype requiredSubtype, IEventBus? eventBus = null)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        RequiredSubtype = requiredSubtype;
        _eventBus = eventBus;
    }

    public string Description => $"sacrifice a {RequiredSubtype}";

    public bool CanPay(Player caster) =>
        ReferenceEquals(Target.Controller, caster)
        && Target.Zone == ZoneType.Battlefield
        && Target.HasType(CardType.Land)
        && Target.HasSubtype(RequiredSubtype);

    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;
        SacrificeCostHelper.Sacrifice(caster, Target, _eventBus);
        return true;
    }
}
