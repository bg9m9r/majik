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
/// Named-card factory for Crippling Blight (Magic 2013 et al., {B}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature."
///   "Enchanted creature gets -1/-1 and can't block."
///
/// ## Implementation
///
/// - Aura subtype + {B} mana cost.
/// - Cast-time targeting via <see cref="AuraSpellDefinitionBuilder"/>:
///   "Enchant creature" → any creature on the battlefield is a legal
///   target (CR 702.5b). BotIntent.Removal signals the debuff intent.
/// - Static "enchanted creature gets -1/-1" while on the battlefield and
///   attached, via <see cref="AttachedBoostEffect"/>(-1, -1) at Layer 7c
///   (CR 613.3c). Registered against the supplied
///   <see cref="ContinuousEffectsService"/> when provided.
/// - Static "can't block" while on the battlefield and attached, via a
///   <see cref="CripplingBlightLifecycle"/> that registers one
///   <see cref="CombatRestrictionEffect"/> (CannotBlock) on the bearer's
///   <see cref="ContinuousEffectsService"/>:
///     * CR 509.1c — creature can't be declared as a blocker.
///   The restriction uses <c>expiresAtEndOfTurn: false</c> — it persists
///   as long as the aura is attached. The lifecycle unregisters it when
///   the aura LTBs.
/// </summary>
[CardName("Crippling Blight")]
public static class CripplingBlightFactory
{
    public const string CardName = "Crippling Blight";
    public const string PrintedManaCost = "{B}";
    public const int PowerModifier = -1;
    public const int ToughnessModifier = -1;

    /// <summary>
    /// Constructs a Crippling Blight with card identity only (no continuous
    /// effects registered). Suitable for shape/dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null, eventBus: null);

    /// <summary>
    /// Constructs a fully-wired Crippling Blight. When
    /// <paramref name="continuousEffects"/> is supplied, the -1/-1 debuff
    /// is registered against the service (Layer 7c per CR 613.3c). When
    /// <paramref name="eventBus"/> is supplied, a
    /// <see cref="CripplingBlightLifecycle"/> is attached so the can't-block
    /// restriction registers/unregisters as the aura enters/leaves the
    /// battlefield via <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.3c — Layer 7c P/T modification.
            // AttachedBoostEffect(-1, -1) reduces the enchanted creature's
            // power and toughness by 1 each while the aura is on the
            // battlefield and attached (IsActive check inside the effect).
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerModifier,
                toughness: ToughnessModifier));
        }

        if (eventBus != null)
        {
            // CR 509.1c — can't-block restriction.
            var lifecycle = new CripplingBlightLifecycle(card, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Crippling Blight.
    /// "Enchant creature" — any creature on the supplied battlefield is a
    /// legal target (CR 702.5b). BotIntent.Removal signals that this is a
    /// debuff attachment.
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
/// CR 303.4 / 509.1c — Aura lifecycle for Crippling Blight's
/// "Enchanted creature can't block" static effect.
///
/// While the aura is on the battlefield AND attached to a creature, one
/// <see cref="CombatRestrictionEffect"/> is registered against the
/// bearer's <see cref="ContinuousEffectsService"/>:
///   * <see cref="CombatRestriction.CannotBlock"/> — CR 509.1c.
///
/// Uses <c>expiresAtEndOfTurn: false</c> — the restriction is persistent
/// (lasting while Crippling Blight is attached), not an ephemeral
/// end-of-turn effect. When the aura LTBs or detaches, the restriction is
/// unregistered immediately.
///
/// Lifecycle mirrors <see cref="PacifismLifecycle"/> structurally, minus
/// the CannotAttack restriction (Crippling Blight's oracle text is
/// can't-block only).
/// </summary>
public sealed class CripplingBlightLifecycle
{
    private readonly Permanent _source;
    private readonly IEventBus _eventBus;
    private readonly Action<GameEvent> _handler;

    private Creature? _registeredOn;
    private CombatRestrictionEffect? _cantBlock;
    private bool _attached;

    public CripplingBlightLifecycle(Permanent auraSource, IEventBus eventBus)
    {
        _source = auraSource ?? throw new ArgumentNullException(nameof(auraSource));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _handler = OnEvent;
    }

    /// <summary>True iff the can't-block restriction is currently registered
    /// on some bearer's <see cref="ContinuousEffectsService"/>.</summary>
    public bool IsActive => _registeredOn != null;

    /// <summary>
    /// Subscribe to zone-move events and register the restriction if
    /// the aura is already on the battlefield + attached at attach time.
    /// Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and unregister the restriction. Idempotent.
    /// </summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _eventBus.UnsubscribeAll(_handler);
        Unregister();
    }

    private void OnEvent(GameEvent e)
    {
        if (e is not CardMovedEvent moved) return;
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
        // no-op (same posture as PacifismLifecycle / BoundInGoldLifecycle).
        // Crippling Blight's enchant clause is "Enchant creature" so a
        // non-creature bearer is illegal at cast time, but the guard is defensive.
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

            _cantBlock = new CombatRestrictionEffect(
                CombatRestriction.CannotBlock,
                target: creatureBearer,
                expiresAtEndOfTurn: false);

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
            if (_cantBlock != null) effects.Unregister(_cantBlock);
        }
        _cantBlock = null;
        _registeredOn = null;
    }
}
