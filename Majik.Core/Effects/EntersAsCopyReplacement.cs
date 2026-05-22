using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 706.10 — "You may have this creature enter as a copy of any creature
/// [on the battlefield | you control | in a graveyard]." Watches the
/// owning card's ETB <see cref="ZoneMoveIntent"/> and, on apply, registers
/// a <see cref="CopyEffect"/> against the entering creature using the
/// caster's first valid candidate as the copy source.
///
/// v1 lossy:
/// - The "you may" choice is auto-yes when any candidate exists; no agent
///   prompt yet. (Deferred: thread agent picker through ReplacementBus.)
/// - "Except it has X" / "except it's a [size/type]" / "with X extra
///   counters" riders are NOT applied — copy mirrors printed P/T +
///   keywords only.
/// - Pool is hard-coded per-binder: battlefield (Clone/Stunt Double),
///   battlefield-you-control (Mirror Image), or graveyard (Body Double).
///   Pool is provided to the constructor.
/// </summary>
public sealed class EntersAsCopyReplacement : IReplacementEffect<ZoneMoveIntent>
{
    public enum CopyPool { AnyBattlefield, BattlefieldYouControl, GraveyardAny }

    private readonly ICard _card;
    private readonly CopyPool _pool;
    private readonly ContinuousEffectsService _effects;

    public EntersAsCopyReplacement(
        ICard card,
        CopyPool pool,
        ContinuousEffectsService effects)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _pool = pool;
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        // Side-effect: register a CopyEffect against the entering card so
        // Power/Toughness/keywords are computed from the chosen source.
        // Returns the intent unchanged; ZoneService will continue placing
        // the card on the battlefield.
        if (_card is not Creature copier) return intent;

        var controller = intent.Controller ?? _card.Owner;
        var source = PickSource(controller);
        if (source != null)
        {
            _effects.Register(new CopyEffect(copier, source));
        }
        return intent;
    }

    private Creature? PickSource(Player? controller)
    {
        // v1 deterministic pick. The controller's view of the battlefield
        // covers "you control"; AnyBattlefield walks the controller's
        // opponents too where available (today only the controller is
        // reachable, so AnyBattlefield acts as "anything we can see").
        // GraveyardAny: controller's graveyard for v1 (Body Double's
        // "any graveyard" is lossy here).
        if (controller == null) return null;

        IEnumerable<Creature> candidates = _pool switch
        {
            CopyPool.GraveyardAny => controller.Zones.Graveyard.GetCards().OfType<Creature>(),
            _ => controller.Zones.Battlefield.GetCards().OfType<Creature>(),
        };
        // Exclude the copier itself — copying yourself is a no-op.
        return candidates.FirstOrDefault(c => !ReferenceEquals(c, _card));
    }
}
