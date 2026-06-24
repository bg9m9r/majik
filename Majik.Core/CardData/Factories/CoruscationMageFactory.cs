using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Coruscation Mage (Bloomburrow, {1}{R}).
///
/// Creature — Otter Wizard 2/2. Oracle text (verified against Scryfall,
/// 2026-06-24):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    Whenever you cast a noncreature spell, this creature deals 1 damage to
///    each opponent."
///
/// The base shape (name, Creature, Otter Wizard subtypes, {1}{R}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>coruscation-mage.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Offspring keyword and the
/// noncreature-cast damage trigger are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express either shape (same posture
/// as <see cref="ElectrostaticFieldFactory"/> for the cast-damage trigger and
/// <see cref="ManifoldMouseFactory"/> for Offspring).
///
/// ## Offspring {2} (CR 702.169)
///
/// Wired through the generic Offspring keyword subsystem:
/// <see cref="OffspringAdditionalCost"/> (the optional additional cast cost,
/// CR 702.169a — drains {2} and stamps <see cref="Card.WasOffspringPaid"/>) +
/// <see cref="OffspringAbility.Attach"/> (the ETB trigger, CR 702.169b — when
/// this creature enters, if its Offspring cost was paid, create a 1/1 token
/// copy of it). The caller layers <see cref="BuildOffspringCost"/> onto the
/// cast via <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c>
/// when the caster chooses to pay; declining simply omits it. Same wiring as
/// <see cref="ManifoldMouseFactory"/>.
///
/// ## Noncreature-cast damage trigger (CR 603.1)
///
/// A <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
/// fires whenever this card's controller ("you") casts a spell whose card is
/// NOT a Creature (CR 205.3 — every card type except Creature: instant,
/// sorcery, artifact, enchantment, planeswalker, battle, land-spell). On
/// resolution it deals 1 damage to each opponent (CR 800.4 — "opponent" means
/// every other player still in the game). Damage routes through
/// <see cref="Fx.DealDamage"/> so each opponent's life total drops and
/// <c>LifeLostThisTurn</c> increments (CR 119.3 / 119.8). Same on-cast
/// each-opponent shape as <see cref="ElectrostaticFieldFactory"/>; the only
/// difference is the predicate — Coruscation Mage fires on ANY noncreature
/// spell, not just instant/sorcery (so the predicate is "not a Creature"
/// rather than "Instant or Sorcery").
///
/// Coruscation Mage's own cast does NOT trigger this: the
/// <see cref="SpellCastEvent"/> for Coruscation Mage itself fires while it is
/// still a Creature spell on the stack (CR 110.4), failing the noncreature
/// predicate — same self-exclusion as <see cref="SlickshotShowOffFactory"/>'s
/// pump and Prowess.
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" enumeration</b>: read from the LIVE resolution
///   context via <see cref="ContextOpponents.Of"/> rather than a captured
///   resolver (the resolver-null bug class — #2540 / #2549). Without a live
///   game context (shape-only paths) the damage half is a safe no-op; the
///   trigger still fires and is observable as pending.
/// </summary>
[CardName("Coruscation Mage")]
public static class CoruscationMageFactory
{
    public const string CardName = "Coruscation Mage";
    public const string Slug = "coruscation-mage";
    public const string OffspringCostText = "{2}";

    /// <summary>CR 119 — fixed 1 damage to each opponent per noncreature cast.</summary>
    public const int DamageAmount = 1;

    /// <summary>CR 702.169 — the Offspring additional cost ({2}). Exposed so
    /// callers build the cost without hard-coding the value.</summary>
    public static ManaCost OffspringCost => ManaCost.Parse(OffspringCostText);

    /// <summary>
    /// Construct Coruscation Mage with no live TriggerManager wiring. The
    /// Offspring ETB trigger, the Offspring keyword marker, and the
    /// noncreature-cast damage trigger are attached to the card shape for
    /// dispatcher / structural tests; the damage half no-ops (no live game
    /// context). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Coruscation Mage with an optional TriggerManager. When
    /// <paramref name="triggers"/> is supplied both the Offspring ETB trigger
    /// and the noncreature-cast damage trigger are registered so the centralised
    /// event pump queues them automatically in a real match. "Each opponent" is
    /// read from the live resolution context at resolution
    /// (<see cref="ContextOpponents.Of"/>), so the damage is correct on the
    /// production routed build.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager for the Offspring + cast triggers.
    /// May be null — the triggers are still attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Otter
        // Wizard subtypes, {1}{R}, 2/2). The JSON carries no abilities —
        // Offspring and the cast trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Offspring {2} ETB token-copy (CR 702.169b).
        OffspringAbility.Attach(card, triggers);

        // CR 702.169 — expose the keyword marker so the keyword scan surface is
        // uniform (same shape as Manifold Mouse). The "{cost}" rider is carried
        // by the OffspringAdditionalCost the caller layers onto the cast.
        card.AddAbility(new KeywordAbility("Offspring", card, owner, arg: 2));

        // CR 603.1 — "Whenever you cast a noncreature spell, this creature deals
        // 1 damage to each opponent."
        // Predicate: the spell's controller is "you" (this card's controller)
        // AND the spell's card is NOT a Creature (CR 205.3). Coruscation Mage's
        // own cast is filtered out — its SpellCastEvent fires while it is a
        // Creature spell on the stack (CR 110.4).
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            if (caster is null || !ReferenceEquals(caster, card.Controller ?? owner))
                return false;

            return !e.Spell.Card.HasType(CardType.Creature);
        });

        var damageEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to each opponent (whenever you cast a noncreature spell)",
            ctx =>
            {
                // CR 800.4 — iterate every opponent and deal 1 damage.
                // CR 119.3 — damage to a player reduces their life total;
                // Fx.DealDamage routes Player → Player.LoseLife (CR 119.8).
                // "Each opponent" is read from the LIVE resolution context —
                // NOT a captured resolver, which was null on the routed prod
                // build and made the damage INERT in real games (resolver-null
                // bug class; mirrors Stormbreath #2540 / Electrostatic Field).
                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    Fx.DealDamage(opp, DamageAmount);
                }
                return ValueTask.CompletedTask;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>Build the Offspring {2} additional cost for this spell. Layer it
    /// onto the cast via SpellCastFlow's <c>additionalCosts</c> when the caster
    /// chooses to pay Offspring; omit it to decline. Same shape as
    /// <see cref="ManifoldMouseFactory.BuildOffspringCost"/>.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);
}
