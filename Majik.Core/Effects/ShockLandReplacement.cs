using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614 / Ravnica shock-land replacement. Watches the land's ETB
/// <see cref="ZoneMoveIntent"/> and either:
///   - pays 2 life on behalf of the controller and lets it enter
///     untapped, or
///   - sets EntersTapped=true if the controller can't / shouldn't pay.
///
/// Policy MVP: pay 2 life when controller's LifeTotal &gt; 2. Replace
/// with a real agent prompt (YesNoCostAsync style) when SpellCastFlow
/// learns that shape.
/// </summary>
public sealed class ShockLandReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _land;

    public ShockLandReplacement(ICard land)
    {
        _land = land ?? throw new ArgumentNullException(nameof(land));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _land)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        var controller = intent.Controller ?? _land.Owner;
        var enterTapped = controller is null || controller.LifeTotal <= 2;
        if (!enterTapped && controller != null)
        {
            controller.LoseLife(2);
        }
        return intent with { EntersTapped = enterTapped };
    }
}
