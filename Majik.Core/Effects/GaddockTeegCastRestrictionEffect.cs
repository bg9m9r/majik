using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Gaddock Teeg's two printed static effects
/// (CR 601.3):
///   "Noncreature spells with mana value 4 or greater can't be cast.
///    Noncreature spells with {X} in their mana costs can't be cast."
///
/// While the source permanent is on the battlefield, both restrictions are
/// registered into <see cref="CastingRestrictions"/>:
/// <list type="bullet">
///   <item><c>AddNoncreatureManaValueAtLeastBlock(token, 4)</c> — the
///         mana-value-4-or-greater band (CR 202.3b — printed MV + chosen X).</item>
///   <item><c>AddNoncreatureXCostBlock(token)</c> — the {X}-in-cost band
///         (CR 107.3 — the printed cost contains the {X} symbol).</item>
/// </list>
/// and <see cref="Majik.Core.Rules.ActionValidator"/> rejects any
/// <c>CastSpellAction</c> whose card is a noncreature spell matching either
/// band. Both blocks are symmetric — Gaddock Teeg's printed text is not
/// player-scoped, so it restricts every player's noncreature spells (including
/// its own controller's). When Gaddock Teeg leaves the battlefield both blocks
/// are removed.
///
/// Lifecycle mirrors <see cref="SanctumPrelateCastRestrictionEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> and register on
///         Attach.</item>
///   <item>Sync on every relevant zone move.</item>
///   <item>Detach unregisters from <see cref="CastingRestrictions"/>.</item>
/// </list>
/// </summary>
public sealed class GaddockTeegCastRestrictionEffect
{
    /// <summary>
    /// CR 601.3 — the printed threshold: noncreature spells with mana value
    /// 4 or greater can't be cast.
    /// </summary>
    public const int ManaValueThreshold = 4;

    private readonly Permanent _source;
    private readonly IEventBus? _eventBus;
    private readonly object _token = new();
    private readonly Action<CardMovedEvent> _handler;
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a Gaddock Teeg cast-restriction lifecycle.
    /// </summary>
    /// <param name="source">The Gaddock Teeg permanent gating the effect.
    /// Must be non-null.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public GaddockTeegCastRestrictionEffect(
        Permanent source,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eventBus = eventBus;
        _handler = OnEvent;
    }

    /// <summary>Whether the blocks are currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>
    /// Subscribe to zone-move events and register if the source is already on
    /// the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.Subscribe(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and remove the registrations. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.Unsubscribe(_handler);
        Unregister();
    }

    private void OnEvent(CardMovedEvent e)
    {
        if (!ReferenceEquals(e.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (_source.Zone == ZoneType.Battlefield)
        {
            if (_registered) return;
            CastingRestrictions.AddNoncreatureManaValueAtLeastBlock(_token, ManaValueThreshold);
            CastingRestrictions.AddNoncreatureXCostBlock(_token);
            _registered = true;
        }
        else
        {
            Unregister();
        }
    }

    private void Unregister()
    {
        if (!_registered) return;
        CastingRestrictions.RemoveNoncreatureManaValueAtLeastBlock(_token);
        CastingRestrictions.RemoveNoncreatureXCostBlock(_token);
        _registered = false;
    }
}
