using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.1c — "enters tapped unless [condition]" replacement, evaluated
/// against the controller's board state at the moment of ETB. The condition
/// is a delegate so each oracle variant (subtype check, count check, etc)
/// can plug in its own predicate without growing this class.
///
/// Returns the intent with <see cref="ZoneMoveIntent.EntersTapped"/> = true
/// when the condition is unmet; otherwise leaves the intent untouched
/// (permanent enters untapped). The card itself is excluded from any
/// "other permanents" tallies via the <c>self</c> parameter.
/// </summary>
public sealed class ConditionalEntersTappedReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;
    private readonly Func<Player, ICard, bool> _entersUntappedIf;

    /// <param name="card">The land/permanent this replacement is bound to.</param>
    /// <param name="entersUntappedIf">
    /// Predicate evaluated at ETB. Receives the controller and the entering
    /// card (so the predicate can exclude `self` from board-state counts).
    /// Return <c>true</c> to enter untapped; <c>false</c> to enter tapped.
    /// </param>
    public ConditionalEntersTappedReplacement(ICard card, Func<Player, ICard, bool> entersUntappedIf)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _entersUntappedIf = entersUntappedIf ?? throw new ArgumentNullException(nameof(entersUntappedIf));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        var controller = intent.Controller ?? _card.Owner;
        if (controller is null) return intent with { EntersTapped = true };

        return _entersUntappedIf(controller, _card)
            ? intent
            : intent with { EntersTapped = true };
    }
}
