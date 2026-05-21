using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.1c — unconditional "this permanent enters tapped" replacement.
/// Watches the card's own ETB <see cref="ZoneMoveIntent"/> and sets
/// <see cref="ZoneMoveIntent.EntersTapped"/> = true so
/// <see cref="Services.ZoneService"/> taps the permanent on landing.
///
/// Use this for cards whose oracle text is a plain "[Card] enters tapped."
/// (Spymaster's Vault, Underground Mortuary, Bloomburrow tap lands, etc).
/// Conditional ETB-tapped clauses ("unless you control…", "may pay 2 life…")
/// need their own binder/replacement — see <see cref="ShockLandReplacement"/>
/// for the shock-land variant.
/// </summary>
public sealed class EntersTappedReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;

    public EntersTappedReplacement(ICard card)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { EntersTapped = true };
}
