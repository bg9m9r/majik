using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wee Dragonauts (Guildpact, {1}{U}{R}).
///
/// Creature — Faerie Wizard 1/3. Oracle text (verified against Scryfall):
///   "Flying
///    Whenever you cast an instant or sorcery spell, this creature gets
///    +2/+0 until end of turn."
///
/// The base shape (name, Creature, Faerie/Wizard subtypes, {1}{U}{R}, 1/3)
/// is materialised from the embedded JSON definition
/// (<c>wee-dragonauts.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flying and the instant/sorcery
/// cast pump are layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't yet express the cast-pump trigger shape (same posture as
/// <see cref="KessigFlamebreatherFactory"/>, whose noncreature-cast trigger
/// is also layered in code).
///
/// ## Implementation
///
/// - 1/3 Faerie Wizard, mana cost {1}{U}{R}.
/// - <b>Flying (CR 702.9)</b>: a <see cref="KeywordAbility"/> marker, read by
///   the combat block-restriction path.
/// - <b>Cast-instant/sorcery pump trigger (CR 603.1 / CR 514.2)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   fires whenever this card's controller casts an Instant or Sorcery spell
///   (CR 205.3 — an "instant or sorcery spell" is a spell whose card has the
///   Instant or Sorcery card type). On resolve it registers a raw
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, 0) on Wee Dragonauts' own
///   <see cref="Creature.ActiveEffects"/> when one is wired — delivering the
///   printed "+2/+0 until end of turn" (Layer 7c +P/+T, end-of-turn-expirable
///   per CR 514.2). Same shape as <see cref="SlickshotShowOffFactory"/>'s
///   cast pump, but narrowed to Instant/Sorcery (not all noncreature spells)
///   and +2/+0 (not +3/+0). Multiple instant/sorcery casts in a single turn
///   stack additively: each cast registers a fresh PumpUntilEndOfTurnEffect,
///   all deltas apply at Layer 7c (CR 613 — multiple Layer 7c effects all
///   apply to the same characteristic). The +0 toughness portion is
///   deliberate — this is NOT prowess (+1/+1); the printed body is +2/+0.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Flying marker attached; the
///   cast-pump trigger is attached to the card for shape observability, but
///   without a <see cref="TriggerManager"/> the bus won't pick it up, and
///   without a <see cref="ContinuousEffectsService"/> on
///   <see cref="Creature.ActiveEffects"/> the pump body silently no-ops on
///   execute. Suitable for dispatcher / structural tests. This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. Pump trigger registered with <paramref name="triggers"/>;
///   <paramref name="effects"/> is bound onto the card's
///   <see cref="Creature.ActiveEffects"/> so live P/T reads flow through the
///   layers compute (CR 613 — Layer 7c applies
///   <see cref="PumpUntilEndOfTurnEffect"/>).
/// </summary>
[CardName("Wee Dragonauts")]
public static class WeeDragonautsFactory
{
    public const string CardName = "Wee Dragonauts";
    public const string Slug = "wee-dragonauts";
    public const int PumpPower = 2;
    public const int PumpToughness = 0;

    /// <summary>
    /// Construct Wee Dragonauts with no live wiring. Flying is attached; the
    /// cast-pump trigger is attached for shape observability but the pump
    /// body silently no-ops without a <see cref="ContinuousEffectsService"/>
    /// on <see cref="Creature.ActiveEffects"/>, and the trigger isn't
    /// registered with any <see cref="TriggerManager"/>. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Wee Dragonauts with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers
    /// (e.g. LTB unregister); not used directly by this factory.</param>
    /// <param name="triggers">TriggerManager for the cast-instant/sorcery
    /// pump trigger. May be null — the trigger is still attached to the card
    /// shape so <see cref="ICard.Abilities"/> includes it.</param>
    /// <param name="effects">ContinuousEffectsService for the +2/+0 EOT pump
    /// (CR 613 Layer 7c, CR 514.2 EOT cleanup). Bound onto the card's
    /// <see cref="Creature.ActiveEffects"/> so live P/T reads flow through the
    /// layers compute. May be null — the pump body silently no-ops on
    /// execute.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Faerie/Wizard subtypes, {1}{U}{R}, 1/3). The JSON carries no
        // abilities — Flying + the cast pump are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // Bind the effects service onto the card so live P/T reads through
        // ActiveEffects flow through the layers compute (mirrors
        // SlickshotShowOff / MonasteryMentor). Done before the trigger so the
        // closure has a stable reference.
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, this
        // creature gets +2/+0 until end of turn." Predicate: spell controller
        // matches AND the spell's card is an Instant or Sorcery (CR 205.3).
        // Wee Dragonauts' own cast does NOT trigger this — its SpellCastEvent
        // fires while it is on the stack as a Creature spell (CR 110.4),
        // failing the instant/sorcery predicate.
        var pumpCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && (e.Spell.Card.HasType(CardType.Instant)
                || e.Spell.Card.HasType(CardType.Sorcery)));

        var pumpEffect = new Effect(
            $"{CardName}: +{PumpPower}/+{PumpToughness} until end of turn (whenever you cast an instant or sorcery spell)",
            () =>
            {
                // CR 514.2 — EOT cleanup is automatic via the layers service's
                // ExpiresAtEndOfTurn flag on PumpUntilEndOfTurnEffect. Without
                // a live effects service the pump silently no-ops.
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness));
            });

        var pumpTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: pumpCondition,
            effects: new IEffect[] { pumpEffect });

        card.AddAbility(pumpTrigger);
        triggers?.RegisterTriggeredAbility(pumpTrigger);

        return card;
    }
}
