using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nest Invader (Rise of the Eldrazi / Conspiracy /
/// Eternal Masters, <c>{1}{G}</c>).
///
/// Creature — Eldrazi Drone 2/2 (green — printed cost carries a {G} pip).
/// Oracle text (verified against Scryfall):
///   "When this creature enters, create a 0/1 colorless Eldrazi Spawn
///    creature token. It has \"Sacrifice this token: Add {C}.\""
///
/// The card's base shape (name, Creature, Eldrazi + Drone subtypes,
/// <c>{1}{G}</c>, 2/2) is materialised from the embedded JSON definition
/// (<c>nest-invader.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed ETB trigger
/// is layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// yet express ETB triggers, so it lives in the factory (same posture as
/// <see cref="GlaringFleshrakerFactory"/> / <see cref="BaskingBroodscaleFactory"/>).
///
/// ## Implemented (v1)
/// <list type="bullet">
///   <item><b>ETB triggered ability (CR 603.6a)</b> — fires on a
///   <see cref="CardMovedEvent"/> filtered to (this card,
///   <c>ToZone == Battlefield</c>), the standard "when this creature enters"
///   shape (same condition as <see cref="EldraziSkyspawnerFactory"/>). On
///   resolution it mints one 0/1 colourless Eldrazi Spawn creature token
///   under Nest Invader's controller via
///   <see cref="TokenFactory.CreateEldraziSpawn"/> — the shared
///   Eldrazi-Spawn primitive (CR 111.10), which ships the
///   "Sacrifice this token: Add {C}." mana ability.</item>
/// </list>
///
/// ## Wiring overloads
/// <list type="bullet">
///   <item><see cref="Create(Player)"/> — shape only. The ETB trigger is
///   attached for shape observability but not registered with any
///   <see cref="TriggerManager"/>; the token half mints with a raw zone move
///   (null <see cref="ZoneService"/>). Suitable for dispatcher / structural
///   tests. This is the overload <see cref="NamedCardFactory"/> dispatches
///   to.</item>
///   <item><see cref="Create(Player, ZoneService?, TriggerManager?)"/> —
///   fully wired. The trigger registers with <paramref name="triggers"/>;
///   the Spawn token's ETB routes through <paramref name="zones"/> so its own
///   <see cref="CardMovedEvent"/> publishes (downstream ETB subscribers see
///   the Spawn's arrival).</item>
/// </list>
///
/// ## Deferred (v1 gaps)
/// <list type="bullet">
///   <item><b>"Sacrifice this token: Add {C}." cost</b> on the Eldrazi Spawn
///   token: <see cref="ManaAbility"/> doesn't carry a sac cost yet (same gap
///   as Eldrazi Skyspawner's Scion / Glaring Fleshraker / Treasure / Food).
///   The Spawn produces {C} without enforcing the sacrifice — see
///   <see cref="TokenFactory.CreateEldraziSpawn"/>.</item>
/// </list>
/// </summary>
[CardName("Nest Invader")]
public static class NestInvaderFactory
{
    public const string CardName = "Nest Invader";
    public const string Slug = "nest-invader";

    /// <summary>
    /// Construct Nest Invader with no live wiring. The ETB trigger is
    /// attached for shape observability but not registered with any
    /// <see cref="TriggerManager"/>; the token half uses a raw zone move
    /// (null <see cref="ZoneService"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Nest Invader with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Eldrazi Spawn token's ETB
    /// routes through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding <see cref="CardMovedEvent"/> lands the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi + Drone subtypes, {1}{G}, 2/2). The JSON carries no
        // abilities — the ETB trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create a 0/1 colorless Eldrazi
        //    Spawn creature token. It has \"Sacrifice this token: Add
        //    {C}.\""
        // No targets — pure token-creation. Condition is the standard
        // self-scoped battlefield-entry shape (same as Eldrazi Skyspawner).
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create a 0/1 colourless Eldrazi Spawn creature token with \"Sacrifice this token: Add {{C}}.\"",
            () =>
            {
                // CR 111.10 — 0/1 colourless Eldrazi Spawn with the
                // (deferred-cost) "Sacrifice this token: Add {C}." mana
                // ability. Created under the controller's control.
                var controller = card.Controller ?? owner;
                TokenFactory.CreateEldraziSpawn(controller, zones);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
