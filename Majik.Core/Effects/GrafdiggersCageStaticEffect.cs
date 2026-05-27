using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// Reusable lifecycle binder for Grafdigger's Cage's two printed static
/// effects (Dark Ascension Artifact {1}):
///   "Creature cards in graveyards and libraries can't enter the
///    battlefield."
///   "Players can't cast spells from graveyards or libraries."
///
/// While Grafdigger's Cage is on the battlefield, this binder wires two
/// surfaces:
///
/// 1. <b>CR 614 replacement effect</b> — registered on the supplied
///    <see cref="ReplacementBus"/>. Intercepts any
///    <see cref="ZoneMoveIntent"/> whose:
///    <list type="number">
///      <item>Destination is <see cref="ZoneType.Battlefield"/>.</item>
///      <item>Source is <see cref="ZoneType.Graveyard"/> or
///            <see cref="ZoneType.Library"/>.</item>
///      <item>Card has <see cref="CardType.Creature"/>.</item>
///    </list>
///    The intent is <b>cancelled</b> (Replace returns null), preventing
///    the move entirely. The card stays in its source zone — distinct
///    from Containment Priest, which rewrites the destination to Exile.
///    Cancellation matches the printed "can't enter the battlefield"
///    wording.
///
/// 2. <b>CR 601.3 global cast-from-zone restriction</b> — registers a
///    blocklist entry against <see cref="ZoneType.Graveyard"/> and
///    <see cref="ZoneType.Library"/> in
///    <see cref="CastingRestrictions"/>. The validator
///    (<see cref="ActionValidator.ValidateCastSpell"/>) consults the rail
///    via <see cref="CastingRestrictions.IsCastFromZoneGloballyBlocked"/>
///    and rejects every player's cast whose declared FromZone is one of
///    the blocked zones. Symmetric across all players (Cage affects
///    everyone, including its controller).
///
/// Lifecycle mirrors <see cref="ContainmentPriestExileReplacementEffect"/>:
/// <list type="bullet">
///   <item>Subscribe to <see cref="CardMovedEvent"/> on Attach.</item>
///   <item>Sync on Attach — active iff the source is on the battlefield.</item>
///   <item>On every relevant move event, re-sync.</item>
///   <item>Detach unsubscribes and removes both registrations.</item>
/// </list>
///
/// ## Scope notes
/// - "Creature cards" — the replacement filters on
///   <see cref="CardType.Creature"/> only; non-creature reanimation
///   targets (artifact / enchantment / planeswalker via Necromancy-style
///   alt-class effects) pass through unaffected. Token creatures
///   typically arrive from "create a token" effects (their ZoneMoveIntent
///   source is rarely Graveyard or Library), but the filter intentionally
///   does not exclude tokens — if a token would ever ETB from one of
///   those zones, Cage still cancels it (matches the printed text
///   literally).
/// - "Cast spells from graveyards or libraries" — the global block fires
///   on any <see cref="CastSpellAction.FromZone"/> = Graveyard or Library
///   regardless of caster. Flashback / escape / aftermath / disturb /
///   jump-start (graveyard casts), and Bolas's Citadel-style library
///   casts, all reject under this rail.
/// - <b>Symmetric</b>: Cage hits its controller too. No opponent-only
///   gate (distinct from Leyline of the Void's
///   <c>opponentResolver</c>-scoped graveyard rewrite).
/// </summary>
public sealed class GrafdiggersCageStaticEffect
{
    private readonly Permanent? _source;
    private readonly ReplacementBus _bus;
    private readonly IEventBus? _eventBus;
    private readonly Action<GameEvent> _handler;
    private readonly LambdaReplacement<ZoneMoveIntent> _effect;
    private readonly object _restrictionToken = new();
    private bool _attached;
    private bool _registered;

    /// <summary>
    /// Build a Grafdigger's Cage static-effects lifecycle.
    /// </summary>
    /// <param name="source">The Grafdigger's Cage permanent gating the
    /// effect. Must be non-null; both halves only apply while the source
    /// is on the battlefield.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register the creature-ETB cancel replacement on. Must be
    /// non-null.</param>
    /// <param name="eventBus">Event bus for <see cref="CardMovedEvent"/>.
    /// May be null — Attach will still sync once.</param>
    public GrafdiggersCageStaticEffect(
        Permanent source,
        ReplacementBus replacementBus,
        IEventBus? eventBus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bus = replacementBus ?? throw new ArgumentNullException(nameof(replacementBus));
        _eventBus = eventBus;
        _handler = OnEvent;

        // Build the cancel-replacement delegate once; reuse across
        // register/unregister cycles so the same object reference is used
        // for both Register and Unregister.
        _effect = new LambdaReplacement<ZoneMoveIntent>(
            applies: static (intent, _) =>
                intent.ToZone == ZoneType.Battlefield
                && (intent.FromZone == ZoneType.Graveyard
                    || intent.FromZone == ZoneType.Library)
                && intent.Card.HasType(CardType.Creature),
            // Return null to CANCEL the move (CR 614 — replacement may
            // cancel the affected event entirely). The card stays in its
            // source zone.
            replace: static (intent, _) => null,
            oneShot: false,
            tag: null);
    }

    /// <summary>Whether the static effects are currently registered.</summary>
    public bool IsActive => _registered;

    /// <summary>
    /// Subscribe to zone-move events and register if the source is already
    /// on the battlefield. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus?.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and remove both registrations. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus?.UnsubscribeAll(_handler);
        Unregister();
    }

    private void OnEvent(GameEvent e)
    {
        if (e is not CardMovedEvent moved) return;
        if (!ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    private void Sync()
    {
        if (_source?.Zone == ZoneType.Battlefield)
        {
            if (_registered) return;
            _bus.Register(_effect);
            CastingRestrictions.AddGlobalCastZoneBlock(_restrictionToken, ZoneType.Graveyard);
            CastingRestrictions.AddGlobalCastZoneBlock(_restrictionToken, ZoneType.Library);
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
        _bus.Unregister(_effect);
        CastingRestrictions.RemoveGlobalCastZoneBlock(_restrictionToken);
        _registered = false;
    }
}
