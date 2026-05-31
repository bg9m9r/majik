using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vexing Devil (Avacyn Restored, {R}).
///
/// Creature — Devil 4/3. Oracle text (Scryfall errata, verified against the
/// task brief):
///   "When this creature enters, any opponent may have it deal 4 damage to
///    them. If a player does, sacrifice this creature."
///
/// The base shape (name, Creature, Devil subtype, {R}, 4/3) is materialised
/// from the embedded JSON definition (<c>vexing-devil.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed ETB triggered
/// ability is layered on top here — the JSON <c>AbilityDefinition</c>
/// schema doesn't yet express a "each opponent may → damage + sacrifice"
/// trigger (same posture as the other JSON-backed cards whose behaviour
/// outgrows the schema, e.g. <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - 4/3 Creature — Devil, mana cost {R}, owner / controller stamped.
/// - <b>ETB triggered ability (CR 603.6a)</b>: <see cref="TriggeredAbility"/>
///   wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution
///   the effect walks "each opponent" (supplied by the optional
///   <c>opponentResolver</c> — the Player aggregate doesn't expose an
///   opponents list at v1, so the caller threads it through, mirroring
///   <see cref="VoldarenEpicureFactory"/> / <see cref="CreepingChillFactory"/>):
///   <list type="number">
///     <item>Each opponent is asked, in turn order, "may have it deal 4
///       damage to them" via <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///       (CR 603.3c / CR 601-style "may" choice; the prompt is classified
///       <see cref="BotIntent.LoseLife"/> | <see cref="BotIntent.CostToDecline"/>
///       so the default heuristic agent declines — keeping the 4/3 alive is
///       the controller-favourable, opponent-neutral default).</item>
///     <item>If a player says yes, Vexing Devil (the source — "it deals 4
///       damage") deals 4 damage to that player via <see cref="Fx.DealDamage"/>
///       (CR 119), and then "If a player does, sacrifice this creature":
///       the Devil is sacrificed via <see cref="Fx.Sacrifice"/>
///       (CR 701.16 — battlefield → graveyard, Sacrifice reason so
///       Indestructible / regeneration gates don't apply).</item>
///   </list>
///   "If a player does" fires once — the first opponent to accept resolves
///   the damage and the sacrifice; once the Devil is sacrificed the loop
///   stops (it has already left the battlefield, and the printed clause is a
///   single sacrifice regardless of how many opponents accept). In practice
///   the printed card is only ever cast into a single-opponent game in
///   Modern; the multi-opponent loop is a defensive generalisation.
///
/// ## Bot intent
/// For the Devil's controller this is a "free" aggressive 4/3 that the
/// opponent can trade 4 life to remove on the spot — a classic aggro
/// tempo gamble. The opponent's prompt is downside (lose 4 life), so the
/// default agent declines and the controller keeps a 4/3 for {R}.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   structurally but not registered with any <see cref="TriggerManager"/>,
///   and the resolve body no-ops (no opponent resolver). Suitable for
///   dispatcher / structural tests. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?, Func{IReadOnlyList{Player}}?)"/>
///   — fully wired. The ETB trigger registers with <paramref name="triggers"/>;
///   the opponent walk uses <paramref name="opponentResolver"/>; the
///   sacrifice routes through <paramref name="zoneService"/> when supplied so
///   LTB / zone-change events fire (CR 603.6a).
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" enumeration</b>: no <c>Player.Opponents</c>
///   accessor exists at v1; the resolver-injection pattern is shared with
///   <see cref="VoldarenEpicureFactory"/> / <see cref="CreepingChillFactory"/>.
///   Without a resolver the whole ETB body no-ops.
/// - <b>Damage-source tracking</b>: <see cref="Fx.DealDamage"/> doesn't
///   thread the Devil through as the damage source, matching the rest of the
///   "it deals N damage to each opponent" family (Voldaren Epicure /
///   Creeping Chill). No source-watching trigger observes the Devil yet.
/// </summary>
[CardName("Vexing Devil")]
public static class VexingDevilFactory
{
    public const string CardName = "Vexing Devil";
    public const string Slug = "vexing-devil";
    public const int Power = 4;
    public const int Toughness = 3;

    /// <summary>Damage dealt to (and life lost by) an accepting opponent.</summary>
    public const int EtbDamageAmount = 4;

    /// <summary>
    /// Construct Vexing Devil with no runtime service wiring. The card has
    /// the correct shape (name, Creature, Devil, {R}, 4/3) and the ETB
    /// trigger is attached for structural / dispatcher inspection, but the
    /// trigger is not registered with a <see cref="TriggerManager"/> and the
    /// resolve body no-ops (no opponent resolver).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null, opponentResolver: null);

    /// <summary>
    /// Construct Vexing Devil with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for ETB registration. May be
    /// null — the trigger is attached structurally but not enrolled.</param>
    /// <param name="zoneService">Zone service the sacrifice routes through so
    /// LTB / zone-change events fire. May be null — raw-zone sacrifice path.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent".
    /// Without a resolver the ETB body no-ops.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Devil subtype, {R}, 4/3). The JSON carries no abilities — the ETB
        // trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When this creature enters, any opponent may have it deal 4
        //    damage to them. If a player does, sacrifice this creature."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: any opponent may take {EtbDamageAmount} damage; if one does, sacrifice this creature",
            async ctx =>
            {
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, owner)) continue;

                    // "any opponent may have it deal 4 damage to them" — CR
                    // 601-style "may" choice made by the opponent. Downside
                    // for the chooser (lose 4 life), so the default heuristic
                    // agent declines (BotIntent.LoseLife | CostToDecline).
                    var agent = ctx.Agent ?? AgentRegistry.Get(opp);
                    var accepts = agent?
                        .ChooseYesNoAsync(
                            $"Have {CardName} deal {EtbDamageAmount} damage to you?",
                            BotIntent.LoseLife | BotIntent.CostToDecline)
                        .GetAwaiter().GetResult()
                        ?? false; // No agent → decline (keep the 4/3).

                    if (!accepts) continue;

                    // CR 119 — "it deals 4 damage to them".
                    Fx.DealDamage(opp, EtbDamageAmount);

                    // "If a player does, sacrifice this creature." CR 701.16
                    // — sacrifice (battlefield → graveyard, Sacrifice reason).
                    // A single acceptance triggers a single sacrifice; once
                    // sacrificed the Devil has left the battlefield, so stop
                    // walking the remaining opponents.
                    if (zoneService != null)
                    {
                        zoneService.MoveCard(
                            card, ZoneType.Battlefield, ZoneType.Graveyard, owner);
                    }
                    else
                    {
                        Fx.Sacrifice(card);
                    }
                    return;
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
