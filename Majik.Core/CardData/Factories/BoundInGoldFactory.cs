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
/// Named-card factory for Bound in Gold (Kaldheim, {2}{W}).
///
/// Enchantment — Aura. Printed oracle text per Scryfall (Kaldereim KHM
/// printing, 2021-02-05, oracle id
/// <c>ca597a0d-b510-4b25-9a83-4d4e613546f5</c>):
///   "Enchant permanent
///    Enchanted permanent can't attack, block, or crew Vehicles, and
///    its activated abilities can't be activated unless they're mana
///    abilities."
///
/// ## Implemented (v1)
///
/// - <b>Enchantment — Aura {2}{W}</b>. Owner / controller wired.
/// - <b>"Enchant permanent" target shape (CR 303.4 / 702.5b)</b>:
///   produced by <see cref="BuildSpellDefinition"/> via
///   <see cref="AuraSpellDefinitionBuilder.ForAura"/> with the predicate
///   <c>p =&gt; true</c> (every permanent qualifies — Kaldheim print
///   matches the broad "Enchant permanent" line, including lands).
///   1..1 cardinality.
/// - <b>Static lockout effect (CR 602.5 / 509.1c / 508.1c)</b> — mirrors
///   <see cref="LeylineBindingLifecycle"/> exactly. While the aura is on
///   the battlefield AND attached to a Creature, three restrictions are
///   registered on the bearer's <see cref="ContinuousEffectsService"/>:
///     * <see cref="CombatRestriction.CannotAttack"/> — "can't attack"
///     * <see cref="CombatRestriction.CannotBlock"/> — "can't block"
///     * <see cref="ActivationRestrictionEffect"/> with
///       <c>ExcludesManaAbilities = true</c> — "its activated abilities
///       can't be activated unless they're mana abilities" (CR 605.1a
///       — mana abilities are explicitly excluded).
///   Lifecycle attaches/detaches via <see cref="CardMovedEvent"/>
///   subscription (same posture as Leyline Binding).
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Can't crew Vehicles"</b>: the printed clause restricts the
///   enchanted permanent from being declared as the crew creature for
///   Vehicles (CR 702.121b). The engine does not yet expose a
///   crew-restriction primitive (only <see cref="CombatRestriction"/>
///   has CannotAttack / CannotBlock / CannotBeBlocked). Tracked as a
///   small follow-up paired with the Vehicle crew-cost surface.
/// - <b>Non-creature bearer scope</b>: the <see cref="LeylineBindingLifecycle"/>-
///   shaped wiring only registers the three restrictions when the
///   bearer is a <see cref="Creature"/> (the per-permanent
///   <see cref="ContinuousEffectsService"/> only exists on Creature in
///   v1). When Bound in Gold enchants a Planeswalker / Artifact / Land
///   the restrictions silently no-op. The activator hookup for
///   non-creature bearers is the same deferred plumbing Leyline
///   Binding documents.
/// - <b>Cast-time targeting prompt</b>: like Leyline Binding / Spreading
///   Seas, the spell-cast flow for Auras is not yet wired engine-wide.
///   Tests exercise the static lifecycle by manually placing the aura
///   on the battlefield, calling <see cref="Permanent.AttachTo"/>,
///   then <see cref="BoundInGoldLifecycle.Sync"/> (or the LTB
///   equivalent).
/// - <b>Activation restriction enforcement</b>: the
///   <see cref="ActivationRestrictionEffect"/> primitive is registered
///   correctly, but the engine activator does not yet consult
///   <see cref="ContinuousEffectsService.HasActivationRestriction"/>
///   on every activation attempt — same gap Leyline Binding documents.
/// </summary>
[CardName("Bound in Gold")]
public static class BoundInGoldFactory
{
    public const string CardName = "Bound in Gold";
    public const string PrintedManaCost = "{2}{W}";

    public const string OracleText =
        "Enchant permanent\n" +
        "Enchanted permanent can't attack, block, or crew Vehicles, and " +
        "its activated abilities can't be activated unless they're mana " +
        "abilities.";

    /// <summary>
    /// Constructs Bound in Gold with correct card identity but no live
    /// lifecycle. Suitable for factory-shape / naming + dispatch tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, eventBus: null);

    /// <summary>
    /// Constructs a fully-wired Bound in Gold. When
    /// <paramref name="eventBus"/> is supplied a
    /// <see cref="BoundInGoldLifecycle"/> is attached so the three
    /// static restrictions register/unregister as the aura enters/leaves
    /// the battlefield via <see cref="CardMovedEvent"/>.
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
            var lifecycle = new BoundInGoldLifecycle(card, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Bound in
    /// Gold — "Enchant permanent" → single Permanent target with no
    /// filter (every permanent on the battlefield qualifies; the
    /// printed Scryfall oracle is the broad "Enchant permanent" line).
    /// </summary>
    /// <param name="aura">The Bound in Gold permanent being cast.</param>
    /// <param name="battlefield">Current battlefield permanents — every
    /// permanent is a legal candidate (no controller-side or
    /// type-side filter).</param>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        // CR 303.4a — Aura's enchant clause is "Enchant permanent".
        // No filter: every permanent is a legal target.
        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target permanent",
            battlefield: battlefield,
            predicate: _ => true);
    }
}

/// <summary>
/// CR 303.4 / 602.5 / 509.1c / 508.1c — Aura lifecycle for Bound in
/// Gold's "Enchanted permanent can't attack, block, or crew Vehicles,
/// and its activated abilities can't be activated unless they're mana
/// abilities" static effect.
///
/// Mirrors <see cref="LeylineBindingLifecycle"/> structurally — three
/// restrictions registered as a unit (CombatRestriction.CannotAttack +
/// CombatRestriction.CannotBlock + ActivationRestrictionEffect with
/// ExcludesManaAbilities = true). The "can't crew Vehicles" piece is
/// deferred — no crew-restriction primitive exists yet (see factory
/// xmldoc "Deferred (v1 gaps)").
/// </summary>
public sealed class BoundInGoldLifecycle
{
    private readonly Permanent _source;
    private readonly IEventBus _eventBus;
    private readonly Action<GameEvent> _handler;

    private Creature? _registeredOn;
    private CombatRestrictionEffect? _cantAttack;
    private CombatRestrictionEffect? _cantBlock;
    private ActivationRestrictionEffect? _cantActivate;
    private bool _attached;

    public BoundInGoldLifecycle(Permanent auraSource, IEventBus eventBus)
    {
        _source = auraSource ?? throw new ArgumentNullException(nameof(auraSource));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _handler = OnEvent;
    }

    /// <summary>True iff the three restrictions are currently registered
    /// on some bearer's <see cref="ContinuousEffectsService"/>.</summary>
    public bool IsActive => _registeredOn != null;

    /// <summary>
    /// Subscribe to zone-move events and register the restrictions if
    /// the aura is already on the battlefield + attached at attach
    /// time. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _eventBus.SubscribeAll(_handler);
        Sync();
    }

    /// <summary>
    /// Unsubscribe and unregister the restrictions. Idempotent.
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
    /// Sync the lifecycle to the aura's current zone + attachment
    /// state. Public so tests / external code can poke it after a
    /// manual <see cref="Permanent.AttachTo"/> without relying on the
    /// event bus.
    /// </summary>
    public void Sync()
    {
        var bearer = _source.AttachedTo;
        // v1 scope: the per-permanent ContinuousEffectsService only
        // exists on Creature in the engine. Non-creature bearers
        // silently no-op (same posture as LeylineBindingLifecycle).
        var creatureBearer = bearer as Creature;
        var shouldBeActive = _source.Zone == ZoneType.Battlefield
                             && creatureBearer != null
                             && creatureBearer.ActiveEffects != null;

        if (_registeredOn != null && !ReferenceEquals(_registeredOn, creatureBearer))
        {
            Unregister();
        }

        if (shouldBeActive && _registeredOn == null)
        {
            var effects = creatureBearer!.ActiveEffects!;

            _cantAttack = new CombatRestrictionEffect(
                CombatRestriction.CannotAttack,
                target: creatureBearer,
                expiresAtEndOfTurn: false);
            _cantBlock = new CombatRestrictionEffect(
                CombatRestriction.CannotBlock,
                target: creatureBearer,
                expiresAtEndOfTurn: false);
            _cantActivate = new ActivationRestrictionEffect(
                target: creatureBearer,
                excludesManaAbilities: true,
                expiresAtEndOfTurn: false);

            effects.Register(_cantAttack);
            effects.Register(_cantBlock);
            effects.Register(_cantActivate);
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
            if (_cantActivate != null) effects.Unregister(_cantActivate);
        }
        _cantAttack = null;
        _cantBlock = null;
        _cantActivate = null;
        _registeredOn = null;
    }
}
