using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magebane Lizard (Magic 2014 / reprinted, {1}{R}).
///
/// Creature — Lizard 1/4. Oracle text (verified against the embedded Scryfall
/// seed):
///   "Whenever a player casts a noncreature spell, this creature deals damage
///    to that player equal to the number of noncreature spells they've cast
///    this turn."
///
/// ## Implementation
///
/// - 1/4 Lizard, mana cost {1}{R}. Base shape (name, Creature, Lizard subtype,
///   {1}{R}, 1/4) is materialised from the embedded JSON definition
///   (<c>magebane-lizard.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities;
///   the noncreature-cast ping trigger is layered on here (the JSON
///   AbilityDefinition schema can't express the ANY-player + dynamic
///   noncreature-count-damage shape — the declarative
///   <c>whenever_a_player_casts_spell</c> trigger has no noncreature filter and
///   <c>deal_damage_to_triggering_player</c> deals a FIXED amount, so the
///   combination here is hand-rolled, the Ash Zealot / Eidolon idiom).
///
/// - <b>Noncreature-cast ping trigger (CR 603.1)</b>: fires whenever ANY player
///   casts a noncreature spell — including this card's controller (CR 700.6 —
///   "a player" is unrestricted; no controller scoping, unlike Firebrand Archer
///   / Prowess). As the condition matches it STAMPS the casting player onto the
///   resolving ability via
///   <see cref="Majik.Core.Abilities.TriggeredAbility.SetTriggeringPlayer"/>
///   (CR 603.3 — "that player"), exactly like
///   <see cref="VengefulTrackerFactory"/>'s declarative
///   <c>whenever_a_player_casts_spell</c> path.
///
/// - <b>Dynamic damage = noncreature spells that player has cast this turn
///   (CR 119)</b>: on resolution the lizard deals damage to the stamped player
///   equal to <see cref="Majik.Core.Game.TurnState.NoncreatureSpellsCastByPlayer"/>
///   read off the LIVE resolution context
///   (<see cref="Majik.Core.Abilities.ResolutionContext.Game"/> →
///   <c>GameContext.TurnState</c>). The per-player noncreature tally is fed at
///   cast time by <c>TurnDriver.OnSpellCast</c> — so by the time this trigger
///   resolves (later, off the stack) the just-cast spell is already counted and
///   the value is &gt;= 1, matching the printed "the number of noncreature
///   spells they've cast this turn" (which includes the triggering spell).
///   Damage routes through <see cref="Fx.DealDamageAny"/> with this creature as
///   the source (CR 119.2 — "this creature deals damage"); <c>DealDamageAny</c>
///   no-ops on a non-positive amount.
///
/// ## Deferred (v1 gaps)
/// - <b>TurnState-less shape paths</b>: when resolved with no live
///   <c>GameContext.TurnState</c> (shape-only tests) the count reads 0 and the
///   ping no-ops, but the trigger still fires and is observable on the stack —
///   same resolver-null posture as the each-opponent family
///   (<see cref="FirebrandArcherFactory"/>).
/// </summary>
[CardName("Magebane Lizard")]
public static class MagebaneLizardFactory
{
    public const string CardName = "Magebane Lizard";
    public const string Slug = "magebane-lizard";

    /// <summary>
    /// Construct Magebane Lizard with no live trigger-manager wiring. The
    /// noncreature-cast ping trigger is attached to the card for shape
    /// observability. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to (production routed build).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Magebane Lizard with an optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied the ping trigger is
    /// registered so the bus surfaces it as pending on a matching noncreature
    /// <see cref="SpellCastEvent"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers; not
    /// used directly by this factory.</param>
    /// <param name="triggers">TriggerManager for the ping trigger. May be null —
    /// the trigger is still attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Lizard
        // subtype, {1}{R}, 1/4). The JSON carries no abilities — the ping
        // trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 603.1 — "Whenever a player casts a noncreature spell, …".
        // ANY player's cast (CR 700.6) whose spell is NOT a creature spell
        // (CR 205.3 / 302.1). As it matches, STAMP the casting player as "that
        // player" (CR 603.3) for the untargeted resolve to read back.
        var pingCondition = new EventTriggerCondition<SpellCastEvent>((e, ability) =>
        {
            var caster = e.Spell?.Controller;
            if (caster is null) return false;
            if (e.Spell!.Card.HasType(CardType.Creature)) return false;

            if (ability is TriggeredAbility ta)
            {
                ta.SetTriggeringPlayer(caster);
            }
            return true;
        });

        var pingEffect = new Effect(
            $"{CardName}: deal damage to the casting player equal to the noncreature spells they've cast this turn",
            ctx =>
            {
                // CR 603.3 — "that player" is the stamped triggering caster.
                var target = ctx.TriggeringPlayer;
                if (target is null) return ValueTask.CompletedTask;

                // CR 119 — "damage equal to the number of noncreature spells
                // they've cast this turn". Read the LIVE per-player noncreature
                // tally off the resolution context (TurnDriver feeds it at cast
                // time, so the triggering spell is already counted → value >= 1).
                // No live TurnState (shape paths) → 0 → DealDamageAny no-ops.
                var amount = ctx.Game?.TurnState?.NoncreatureSpellsCastByPlayer(target) ?? 0;

                // CR 119.2 — "this creature deals damage"; the lizard is the
                // source. DealDamageAny guards non-positive amounts.
                Fx.DealDamageAny(target, amount, card);
                return ValueTask.CompletedTask;
            });

        var pingTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: pingCondition,
            effects: new IEffect[] { pingEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(pingTrigger);
        triggers?.RegisterTriggeredAbility(pingTrigger);

        return card;
    }
}
