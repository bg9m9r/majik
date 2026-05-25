using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline Binding (Dominaria United).
///
/// Enchantment — Aura — {W}{W}{W}{W}{W}. Oracle text:
///   "Domain — This spell costs {1} less to cast for each basic land
///    type among lands you control.
///    Enchant nonland permanent an opponent controls.
///    Enchanted permanent can't attack, block, or activate non-mana
///    abilities."
///
/// ## Implemented (v1)
/// - Enchantment with the Aura subtype, mana cost {W}{W}{W}{W}{W}.
/// - <b>Domain cost reduction (CR 702.16 / CR 117.7)</b>: a
///   <see cref="CostReductionAbility"/> using the whole-reducer shape:
///   <c>reduction = CountDomain(caster)</c>. Reuses
///   <see cref="TribalFlamesFactory.CountDomain"/> as the canonical
///   Domain counter (printed-subtypes mode — no live
///   <see cref="ContinuousEffectsService"/> here at cost-calculation
///   time). Floor-at-zero is enforced by
///   <see cref="CostReduction.GetEffectiveCost"/>; the four W pips are
///   untouched (CR 117.7c — colored pips don't reduce). With all five
///   basics on the battlefield, effective cost collapses to the five W
///   pips alone — exactly the canonical "{W}" effective floor the
///   format-defining Leyline Binding turn-2 play depends on.
/// - <b>"Enchant nonland permanent an opponent controls" target shape</b>:
///   produced by <see cref="BuildSpellDefinition"/> via
///   <see cref="AuraSpellDefinitionBuilder.ForAura"/> with the explicit
///   predicate <c>p =&gt; !p.HasType(CardType.Land) &amp;&amp;
///   p.Controller != caster</c>. 1..1 cardinality.
/// - <b>Static lockout effect (CR 602.5 / 509.1c / 508.1c)</b>: while
///   attached, the enchanted permanent gains three restrictions on its
///   per-permanent <see cref="ContinuousEffectsService"/>:
///     * <see cref="CombatRestriction.CannotAttack"/>
///     * <see cref="CombatRestriction.CannotBlock"/>
///     * <see cref="ActivationRestrictionEffect"/> (excludes mana
///       abilities — CR 605.1a)
///   These are registered as a unit when the aura ETBs onto the
///   battlefield and attaches, and unregistered when the aura leaves
///   the battlefield (LTB).
///
/// ## Deferred (v1 gaps)
/// - <b>Cast-time targeting prompt</b>: like Spreading Seas, the
///   spell-cast flow for Auras is not yet wired engine-wide. Tests can
///   exercise the static lifecycle by manually placing the aura on the
///   battlefield, calling <see cref="Permanent.AttachTo"/>, then
///   <see cref="LeylineBindingLifecycle.Sync"/> (or the LTB equivalent).
/// - <b>Activation restriction enforcement</b>: the
///   <see cref="ActivationRestrictionEffect"/> primitive is registered
///   correctly, but the broader engine activator does not yet consult
///   <see cref="ContinuousEffectsService.HasActivationRestriction"/>
///   on every activation attempt. The restriction is queryable today
///   (and tests verify the registration); the activator hookup is a
///   small follow-up.
/// </summary>
[CardName("Leyline Binding")]
public static class LeylineBindingFactory
{
    public const string CardName = "Leyline Binding";
    public const string PrintedManaCost = "{W}{W}{W}{W}{W}";

    public const string OracleText =
        "Domain — This spell costs {1} less to cast for each basic land " +
        "type among lands you control.\n" +
        "Enchant nonland permanent an opponent controls\n" +
        "Enchanted permanent can't attack, block, or activate non-mana " +
        "abilities.";

    /// <summary>
    /// Creates Leyline Binding with correct card identity + the Domain
    /// cost reducer but no live lifecycle. Suitable for factory-shape /
    /// naming + cost tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Leyline Binding. When <paramref name="eventBus"/>
    /// is supplied, a <see cref="LeylineBindingLifecycle"/> is attached so the
    /// three static restrictions register/unregister as the aura
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

        // CR 702.16 (Domain) + CR 117.7 — "This spell costs {1} less to
        // cast for each basic land type among lands you control."
        // Declarative DomainCostReductionAbility wraps the canonical
        // Domain.CountTypes primitive (max 5 distinct types; Wastes
        // excluded — CR 305.6) and multiplies by 1. Floor-at-zero is
        // enforced by CostReduction.GetEffectiveCost; the five W pips
        // are coloured (CR 117.7c) and never reduce.
        card.AddAbility(new DomainCostReductionAbility(multiplier: 1));

        if (eventBus != null)
        {
            // Lifecycle: register the three restrictions when the aura is
            // on the battlefield, unregister when it LTBs. Wired via the
            // CardMovedEvent stream same as AttachedAuraRetypeStaticEffect.
            var lifecycle = new LeylineBindingLifecycle(card, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Leyline
    /// Binding — "Enchant nonland permanent an opponent controls" →
    /// single Permanent target filtered by predicate.
    /// </summary>
    /// <param name="aura">The Leyline Binding permanent being cast.</param>
    /// <param name="caster">The casting player — used to scope the
    /// "an opponent controls" half of the predicate.</param>
    /// <param name="battlefield">Current battlefield permanents — the
    /// candidate pool is filtered to nonland permanents controlled by a
    /// player other than the caster.</param>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        Player caster,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(battlefield);

        // CR 303.4a — Aura's enchant clause is "Enchant nonland permanent
        // an opponent controls". Explicit predicate (not the oracle-text
        // parser overload) because the parser doesn't model the
        // "an opponent controls" controller-side filter.
        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target nonland permanent an opponent controls",
            battlefield: battlefield,
            predicate: p => !p.HasType(CardType.Land)
                            && !ReferenceEquals(p.Controller, caster));
    }
}

/// <summary>
/// CR 303.4 / 602.5 / 509.1c / 508.1c — Aura lifecycle for Leyline
/// Binding's "Enchanted permanent can't attack, block, or activate
/// non-mana abilities" static effect.
///
/// While the aura is on the battlefield AND attached to a permanent,
/// three restrictions are registered against the bearer's
/// <see cref="ContinuousEffectsService"/>:
///   * <see cref="CombatRestriction.CannotAttack"/> (per-creature; the
///     attack restriction only matters when the bearer is a creature,
///     but registering on any permanent is harmless — the combat
///     validator scopes by creature anyway).
///   * <see cref="CombatRestriction.CannotBlock"/> (per-creature, same
///     reasoning).
///   * <see cref="ActivationRestrictionEffect"/> with
///     <c>ExcludesManaAbilities = true</c> — non-mana ability lockout.
///
/// Lifecycle mirrors
/// <see cref="AttachedAuraRetypeStaticEffect"/>: subscribe to
/// <see cref="CardMovedEvent"/>, sync on attach + every move. When the
/// aura is not on the battlefield, the three effects unregister. When
/// the aura is on the battlefield but its <see cref="Permanent.AttachedTo"/>
/// slot is null, no registration happens (nothing to scope to). When the
/// bearer has no live <see cref="ContinuousEffectsService"/> (shape
/// tests), the lifecycle silently no-ops — restrictions only register
/// when the bearer can hold them.
/// </summary>
public sealed class LeylineBindingLifecycle
{
    private readonly Permanent _source;
    private readonly IEventBus _eventBus;
    private readonly Action<GameEvent> _handler;

    private Creature? _registeredOn;
    private CombatRestrictionEffect? _cantAttack;
    private CombatRestrictionEffect? _cantBlock;
    private ActivationRestrictionEffect? _cantActivate;
    private bool _attached;

    public LeylineBindingLifecycle(Permanent auraSource, IEventBus eventBus)
    {
        _source = auraSource ?? throw new ArgumentNullException(nameof(auraSource));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _handler = OnEvent;
    }

    /// <summary>True iff the three restrictions are currently registered
    /// on some bearer's <see cref="ContinuousEffectsService"/>.</summary>
    public bool IsActive => _registeredOn != null;

    /// <summary>
    /// Subscribe to zone-move events and register the restrictions if the
    /// aura is already on the battlefield + attached at attach time.
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
    /// Sync the lifecycle to the aura's current zone + attachment state.
    /// Public so tests / external code can poke it after a manual
    /// <see cref="Permanent.AttachTo"/> without relying on the event bus.
    /// </summary>
    public void Sync()
    {
        var bearer = _source.AttachedTo;
        // v1 scope: the per-permanent ContinuousEffectsService currently
        // only exists on Creature (Creature.ActiveEffects). The aura's
        // restrictions register against that service, so non-creature
        // bearers (planeswalkers, artifacts, …) silently no-op at v1.
        // Modern Leyline Binding's primary targets are creatures +
        // planeswalkers; the planeswalker path is a follow-up that needs
        // ActiveEffects threaded through Permanent.
        var creatureBearer = bearer as Creature;
        var shouldBeActive = _source.Zone == ZoneType.Battlefield
                             && creatureBearer != null
                             && creatureBearer.ActiveEffects != null;

        // If the bearer changed (re-attach to a different permanent), tear
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
