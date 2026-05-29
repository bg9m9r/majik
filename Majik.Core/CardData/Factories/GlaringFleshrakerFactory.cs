using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glaring Fleshraker (Edge of Eternities, {2}{C}).
///
/// Creature — Eldrazi Drone 2/2 (colorless — printed cost is generic +
/// {C}, no colored pip). Oracle text (verified against Scryfall):
///   "Whenever you cast a colorless spell, create a 0/1 colorless Eldrazi
///    Spawn creature token with "Sacrifice this token: Add {C}."
///    Whenever another colorless creature you control enters, this creature
///    deals 1 damage to each opponent."
///
/// The card's base shape (name, Creature, Eldrazi Drone subtypes, {2}{C},
/// 2/2) is materialised from the embedded JSON definition
/// (<c>glaring-fleshraker.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed triggers are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// yet express cast-spell or ETB triggers, so they live in the factory
/// (same posture as <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Cast-colorless-spell trigger (CR 603.1)</b> — fires on a
///   <see cref="SpellCastEvent"/> whose <see cref="Majik.Core.Spells.ISpell.Controller"/>
///   matches Glaring Fleshraker's controller AND whose
///   <see cref="Majik.Core.Spells.ISpell.Card"/> is colorless
///   (<see cref="CardColors.GetColors"/> returns an empty set — CR 105.2c
///   "a colorless object has no color"). Predicate shape mirrors
///   <see cref="SpriteDragonFactory"/>'s cast-trigger (controller match +
///   a card-characteristic gate), but the gate is "colorless" rather than
///   "noncreature". On resolution the effect mints one Eldrazi Spawn token
///   (0/1 colorless creature with "Sacrifice this token: Add {C}.") under
///   the controller via <see cref="TokenFactory.CreateEldraziSpawn"/> — the
///   shared Eldrazi-Spawn primitive (CR 111.10).
/// - <b>Another-colorless-creature-enters trigger (CR 603.6a)</b> — fires
///   on a <see cref="CardMovedEvent"/> → Battlefield for a creature OTHER
///   than this card, controlled by this card's controller, that is colorless
///   (<see cref="CardColors.GetColors"/> empty). Predicate is
///   <see cref="Triggers.OnAnotherCreatureYouControlEnters"/> narrowed by
///   the colorless gate. On resolution the Fleshraker deals 1 damage to
///   each opponent, routed through <see cref="Fx.DealDamageAny"/> against
///   the <c>opponentResolver</c> (same resolver-injection pattern as
///   <see cref="VoldarenEpicureFactory"/> — the Player aggregate exposes no
///   opponents list at v1, so the caller threads it through). Note the
///   self-made Eldrazi Spawn tokens are themselves colorless creatures, so
///   minting a Spawn from clause 1 (or from anything else) feeds this
///   trigger — the printed engine of the card.
///
/// ## Single-arg dispatcher path
///
/// The <see cref="Create(Player)"/> overload attaches both triggers
/// structurally (correct card shape for factory-shape / dispatch tests).
/// Neither trigger is registered with a <see cref="TriggerManager"/>; the
/// damage half no-ops with no resolver, and the token half mints with a raw
/// zone move (null <see cref="ZoneService"/>). Production callers use the
/// full overload.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Live "each opponent" enumeration</b> — no <c>Player.Opponents</c>
///   accessor at v1; resolver-injection shared with
///   <see cref="VoldarenEpicureFactory"/> / <see cref="CreepingChillFactory"/>.
/// - <b>Eldrazi Spawn sacrifice cost</b> — the minted Spawn's
///   "Sacrifice this token: Add {C}." is wired as a plain
///   <see cref="ManaAbility"/> producing {C} without enforcing the
///   sacrifice cost (documented deferral inherited from
///   <see cref="TokenFactory.CreateEldraziSpawn"/>, same gap as Treasure /
///   Food sac costs).
/// </summary>
[CardName("Glaring Fleshraker")]
public static class GlaringFleshrakerFactory
{
    public const string CardName = "Glaring Fleshraker";
    public const string Slug = "glaring-fleshraker";
    public const int EtbDamageAmount = 1;

    /// <summary>
    /// Construct Glaring Fleshraker with no live wiring. Both triggers are
    /// attached structurally (cast-colorless-spell + another-colorless-
    /// creature-enters) but NOT registered with a
    /// <see cref="TriggerManager"/>; the damage half no-ops (no resolver)
    /// and the token half uses a raw zone move (null
    /// <see cref="ZoneService"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, zoneService: null, opponentResolver: null);

    /// <summary>
    /// Construct a fully-wired Glaring Fleshraker.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for registration. May be null
    /// — both triggers attach structurally but are not enrolled.</param>
    /// <param name="zoneService">Zone-service used when seating the Eldrazi
    /// Spawn token so any "whenever a creature/token enters" trigger fires
    /// (notably the Fleshraker's own second trigger). May be null — raw
    /// zone move performed instead.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent"
    /// for the second trigger's burn half. Without a resolver the damage
    /// half no-ops.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi + Drone subtypes, {2}{C}, 2/2). The JSON carries no
        // abilities — both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Trigger 1 — cast-colorless-spell (CR 603.1).
        //   "Whenever you cast a colorless spell, create a 0/1 colorless
        //    Eldrazi Spawn creature token with "Sacrifice this token:
        //    Add {C}.""
        // Predicate: controller match AND the spell's card is colorless
        // (CR 105.2c — no color). Effect mints one Eldrazi Spawn via the
        // shared TokenFactory primitive (CR 111.10). Functions while the
        // Fleshraker is on the battlefield (CR 603.5).
        // ----------------------------------------------------------------
        var spawnEffect = new Effect(
            $"{CardName}: create a 0/1 colorless Eldrazi Spawn (cast a colorless spell)",
            () => TokenFactory.CreateEldraziSpawn(owner, zoneService));

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
                ReferenceEquals(e.Spell.Controller, owner)
                && CardColors.GetColors(e.Spell.Card).Count == 0),
            effects: new IEffect[] { spawnEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        // ----------------------------------------------------------------
        // Trigger 2 — another-colorless-creature-enters (CR 603.6a).
        //   "Whenever another colorless creature you control enters, this
        //    creature deals 1 damage to each opponent."
        // Predicate mirrors Triggers.OnAnotherCreatureYouControlEnters
        // (battlefield entry of a creature other than self under this
        // controller — the Soul Warden shape), narrowed by the colorless
        // gate (CR 105.2c — a colorless object has no color). Effect deals 1
        // to each opponent via the resolver-injection pattern (Voldaren
        // Epicure shape). The self-made Eldrazi Spawn tokens are colorless
        // creatures, so they feed this trigger.
        // ----------------------------------------------------------------
        var colorlessEntersCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Creature)
            && !ReferenceEquals(e.Card, card)
            && ReferenceEquals(e.Card.Controller, owner)
            && CardColors.GetColors(e.Card).Count == 0);

        var damageEffect = new Effect(
            $"{CardName}: deal {EtbDamageAmount} damage to each opponent (another colorless creature entered)",
            () =>
            {
                // CR 119 — damage to a player is life loss. The Player
                // aggregate exposes no opponents list at v1, so the caller
                // threads "each opponent" through opponentResolver (shared
                // with Voldaren Epicure / Creeping Chill). Without a
                // resolver the burn half no-ops.
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, owner)) continue;
                    Fx.DealDamageAny(opp, EtbDamageAmount);
                }
            });

        var entersTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: colorlessEntersCondition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(entersTrigger);
        triggers?.RegisterTriggeredAbility(entersTrigger);

        return card;
    }
}
