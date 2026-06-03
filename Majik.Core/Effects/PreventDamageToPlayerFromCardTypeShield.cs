using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.16e — the damage-prevention half of player-level protection from a
/// card type (Serra's Emissary's "You ... have protection from the chosen
/// card type"). Cancels every <see cref="DamageIntent"/> targeting the
/// protected player (the source's controller) whose source is a card of the
/// chosen <see cref="CardType"/> (DEBT-A: a protected object can't be Damaged
/// by anything matching the quality).
///
/// The damage source is read off <see cref="DamageIntent.Source"/>; when it is
/// an <see cref="ICard"/> (a creature dealing combat damage, a spell/ability's
/// source permanent) its card types are consulted via
/// <see cref="ICard.HasType"/>. Non-card sources (a bare player source) never
/// match — a player is not a card type.
///
/// Self-gates on the granting permanent's <see cref="Permanent.Zone"/> and
/// resolves the protected player from its CURRENT controller (CR 614.6),
/// mirroring <see cref="PreventAllDamageToPlayerShield"/> — the owning factory
/// registers it once, no explicit LTB unregister. Not
/// <see cref="IEndOfTurnExpirable"/> — Serra's Emissary's protection is a
/// static, not a "this turn" effect.
/// </summary>
public sealed class PreventDamageToPlayerFromCardTypeShield : IReplacementEffect<DamageIntent>
{
    private readonly Permanent _source;
    private readonly CardType _type;

    public PreventDamageToPlayerFromCardTypeShield(Permanent source, CardType type)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _type = type;
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.Amount <= 0) return false;
        var controller = _source.Controller;
        if (controller == null || !ReferenceEquals(intent.TargetPlayer, controller)) return false;
        return intent.Source is ICard sourceCard && sourceCard.HasType(_type);
    }

    // CR 615.1 / 702.16e — prevention cancels the damage entirely.
    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history) => null;
}
