using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pacifism (Tempest et al., {1}{W}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature."
///   "Enchanted creature can't attack or block."
///
/// ## Implementation
///
/// - Aura subtype + {1}{W} mana cost.
/// - Cast-time targeting via <see cref="AuraSpellDefinitionBuilder"/>:
///   "Enchant creature" → any creature on the battlefield is a legal
///   target (CR 702.5b). BotIntent.Removal signals the lockdown intent.
/// - Static "enchanted creature can't attack or block" while Pacifism
///   is on the battlefield and attached, via a
///   <see cref="PacifismLifecycle"/> that registers two
///   <see cref="CombatRestrictionEffect"/>s (CannotAttack + CannotBlock)
///   on the bearer's <see cref="ContinuousEffectsService"/>:
///     * CR 508.1c — creature can't be declared as an attacker.
///     * CR 509.1c — creature can't be declared as a blocker.
///   Both restrictions use <c>expiresAtEndOfTurn: false</c> — they last
///   as long as the aura is attached, not just until end of turn.
///   The lifecycle unregisters both restrictions when the aura LTBs.
/// - No activation restriction (contrast Leyline Binding / Bound in
///   Gold — Pacifism's oracle text is narrowly attack + block only).
/// </summary>
[CardName("Pacifism")]
public static class PacifismFactory
{
    public const string CardName = "Pacifism";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>
    /// Constructs a Pacifism with correct card identity but no live
    /// lifecycle. Suitable for factory-shape / naming + dispatch tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, eventBus: null);

    /// <summary>
    /// Constructs a fully-wired Pacifism. When
    /// <paramref name="eventBus"/> is supplied, a
    /// <see cref="PacifismLifecycle"/> is attached so the two static
    /// combat restrictions register/unregister as the aura
    /// enters/leaves the battlefield via <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        if (eventBus != null)
        {
            var lifecycle = new PacifismLifecycle(card, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Pacifism.
    /// "Enchant creature" — any creature on the supplied battlefield is a
    /// legal target (CR 702.5b). BotIntent.Removal signals that this is a
    /// lockdown attachment.
    /// CR 303.4f — on resolve the aura enters the battlefield already
    /// attached to the chosen target.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: static p => p.HasType(CardType.Creature),
            intent: BotIntent.Removal);
    }
}

/// <summary>
/// CR 303.4 / 508.1c / 509.1c — Aura lifecycle for Pacifism's
/// "Enchanted creature can't attack or block" static effect.
///
/// While the aura is on the battlefield AND attached to a creature, two
/// <see cref="CombatRestrictionEffect"/>s are registered against the
/// bearer's <see cref="ContinuousEffectsService"/>:
///   * <see cref="CombatRestriction.CannotAttack"/> — CR 508.1c.
///   * <see cref="CombatRestriction.CannotBlock"/> — CR 509.1c.
///
/// Both use <c>expiresAtEndOfTurn: false</c> — they are persistent
/// (lasting while Pacifism is attached), not ephemeral end-of-turn
/// effects. When the aura LTBs or detaches, both restrictions are
/// unregistered immediately.
///
/// Lifecycle mirrors <see cref="BoundInGoldLifecycle"/> structurally,
/// minus the activation-restriction piece (Pacifism's oracle text is
/// attack + block only — no "can't activate abilities" clause).
/// </summary>
public sealed class PacifismLifecycle
{
    private readonly Permanent _source;
    private readonly IEventBus _eventBus;
    private readonly Action<CardMovedEvent> _handler;

    private Creature? _registeredOn;
    private CombatRestrictionEffect? _cantAttack;
    private CombatRestrictionEffect? _cantBlock;
    private bool _attached;

    public PacifismLifecycle(Permanent auraSource, IEventBus eventBus)
    {
        _source = auraSource ?? throw new ArgumentNullException(nameof(auraSource));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _handler = OnEvent;
    }

    /// <summary>True iff the two restrictions are currently registered
    /// on some bearer's <see cref="ContinuousEffectsService"/>.</summary>
    public bool IsActive => _registeredOn != null;

    /// <summary>
    /// Subscribe to zone-move events and register the restrictions if
    /// the aura is already on the battlefield + attached at attach time.
    /// Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus.Subscribe(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and unregister the restrictions. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus.Unsubscribe(_handler);
        Unregister();
    }

    private void OnEvent(CardMovedEvent e)
    {
        var moved = e;
        if (!ReferenceEquals(moved.Card, _source)) return;
        Sync();
    }

    /// <summary>
    /// Sync the lifecycle to the aura's current zone + attachment state.
    /// Public so tests / external code can poke it after a manual
    /// <see cref="Permanent.AttachTo"/> without relying on the event bus.
    /// </summary>
    public void Sync()
    {
        var bearer = _source.AttachedTo;
        // v1 scope: the per-permanent ContinuousEffectsService only
        // exists on Creature in the engine. Non-creature bearers silently
        // no-op (same posture as LeylineBindingLifecycle / BoundInGoldLifecycle).
        // Pacifism's enchant clause is "Enchant creature" so a non-creature
        // bearer is illegal at cast time, but the guard is defensive.
        var creatureBearer = bearer as Creature;
        var shouldBeActive = _source.Zone == ZoneType.Battlefield
                             && creatureBearer != null
                             && creatureBearer.ActiveEffects != null;

        // If the bearer changed (re-attach to a different creature), tear
        // down the previous registration before standing the new one up.
        if (_registeredOn != null && !ReferenceEquals(_registeredOn, creatureBearer))
        {
            Unregister();
        }

        if (shouldBeActive && _registeredOn == null)
        {
            // creatureBearer/ActiveEffects checked non-null above.
            var effects = creatureBearer!.ActiveEffects!;

            _cantAttack = new CombatRestrictionEffect(
                CombatRestriction.CannotAttack,
                target: creatureBearer,
                expiresAtEndOfTurn: false);
            _cantBlock = new CombatRestrictionEffect(
                CombatRestriction.CannotBlock,
                target: creatureBearer,
                expiresAtEndOfTurn: false);

            effects.Register(_cantAttack);
            effects.Register(_cantBlock);
            _registeredOn = creatureBearer;
        }
        else if (!shouldBeActive)
        {
            Unregister();
        }
    }

    private void Unregister()
    {
        if (_registeredOn == null) return;
        var effects = _registeredOn.ActiveEffects;
        if (effects != null)
        {
            if (_cantAttack != null) effects.Unregister(_cantAttack);
            if (_cantBlock != null) effects.Unregister(_cantBlock);
        }
        _cantAttack = null;
        _cantBlock = null;
        _registeredOn = null;
    }
}
