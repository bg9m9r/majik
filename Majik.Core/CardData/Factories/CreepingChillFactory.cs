using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Creeping Chill (Guilds of Ravnica, {3}{B}).
///
/// Sorcery. Oracle text:
///   "Creeping Chill deals 3 damage to each opponent and you gain 3 life.
///    When Creeping Chill is put into your graveyard from your library,
///    you may exile it. If you do, Creeping Chill deals 3 damage to each
///    opponent and you gain 3 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost <c>{3}{B}</c>, owner/controller assigned.
/// - <b>Cast resolve effect</b> exposed via
///   <see cref="BuildResolveEffect"/>: deals 3 damage to each opponent
///   supplied by the caller and gains the controller 3 life. Mirrors the
///   Omnath / Meathook Massacre resolver pattern — the engine has no
///   first-class "each opponent" iterator without a supplied list. Damage
///   routes through <see cref="Fx.DealDamageAny"/> so any Player /
///   Planeswalker shape is consistent; life gain routes through
///   <see cref="Fx.GainLife"/>.
/// - <b>Mill-trigger (CR 603.6c)</b>: a graveyard-resident
///   <see cref="TriggeredAbility"/> watches <see cref="CardMovedEvent"/>
///   filtered to <c>FromZone == Library &amp;&amp; ToZone == Graveyard</c>
///   for THIS card (reference identity — self-referential trigger, same
///   shape as Narcomoeba). <c>activeZones = {Graveyard}</c> — the trigger
///   fires after the mill move completes.
/// - <b>"You may exile it. If you do, deal 3 + gain 3"</b>: at trigger
///   resolution, when an <see cref="IPlayerAgent"/> is supplied the
///   factory consults <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///   (<see cref="BotIntent.LoseLife"/> | <see cref="BotIntent.Heal"/> —
///   the prompt represents a positive-value exile-for-burn). On Yes, the
///   card is moved Graveyard → Exile via <see cref="ZoneService.MoveCard"/>
///   when wired, then the same 3-damage / 3-life resolve effect re-runs.
///   On No (or the post-exile re-check finding the card no longer in the
///   exile zone — defensive CR 608.2b), the burn half no-ops. Null agent
///   → legacy auto-accept (same posture as Bloodghast / Arclight Phoenix).
///
/// ## Deferred (v1 gaps)
/// - <b>"Each opponent" live enumeration</b>: requires an
///   <c>opponentResolver</c> closure — same shape as the Omnath /
///   Meathook Massacre / The Meathook Massacre pattern. Without a
///   resolver the burn half iterates nothing and silently no-ops. The
///   life-gain half always fires.
/// - <b>Mill-trigger damage half</b>: same resolver-driven shape as the
///   cast resolve. The factory exposes a single
///   <see cref="BuildResolveEffect"/> body so the mill trigger and the
///   cast resolution share one implementation — keeps the printed "deal
///   3 + gain 3" identical between the two halves (CR 117.2 — equivalent
///   text means equivalent effect).
/// </summary>
[CardName("Creeping Chill")]
public static class CreepingChillFactory
{
    public const string CardName = "Creeping Chill";
    public const string PrintedManaCost = "{3}{B}";
    public const int Damage = 3;
    public const int LifeGain = 3;

    /// <summary>
    /// Construct Creeping Chill with no runtime service wiring. Shape /
    /// dispatch path. The mill-trigger is attached structurally for
    /// inspection but not bus-driven.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, agent: null,
            opponentResolver: null);

    /// <summary>
    /// Construct Creeping Chill with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone-service for the "exile it" half of
    /// the mill trigger. May be null — raw zone move performed instead.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident
    /// trigger registration. May be null — trigger is attached
    /// structurally but not registered.</param>
    /// <param name="agent">Optional agent for the "you may exile" prompt
    /// (<see cref="BotIntent.LoseLife"/> | <see cref="BotIntent.Heal"/>).
    /// Null → legacy auto-accept.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent"
    /// for the burn half (mill trigger). Without a resolver the burn half
    /// no-ops; the life-gain half always fires.</param>
    public static Sorcery Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Mill-trigger — CR 603.1 + CR 603.6c.
        //   "When Creeping Chill is put into your graveyard from your
        //    library, you may exile it. If you do, Creeping Chill deals
        //    3 damage to each opponent and you gain 3 life."
        // ----------------------------------------------------------------
        var millEffect = new Effect(
            $"{CardName}: exile from graveyard + deal {Damage} to each opp + gain {LifeGain}",
            async ctx =>
            {
                // CR 608.2b — re-check the zone at resolution.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                // "You may exile it" — consult agent or auto-accept.
                if (agent != null)
                {
                    var yes = (await agent.ChooseYesNoAsync(
                        "Exile Creeping Chill to deal 3 to each opponent and gain 3 life?",
                        BotIntent.LoseLife | BotIntent.Heal).ConfigureAwait(false));
                    if (!yes) return;
                }

                // Exile. ZoneService-routed when supplied so any future
                // exile-watching trigger (Rest in Peace, Leyline of the
                // Void) can observe the move.
                if (zoneService != null)
                {
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Exile, owner);
                }
                else
                {
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);
                }

                // "If you do" — burn + life-gain only on successful exile.
                ApplyBurnAndLifeGain(owner, opponentResolver);
            });

        var millCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            ReferenceEquals(e.Card, card)
            && e.FromZone == ZoneType.Library
            && e.ToZone == ZoneType.Graveyard);

        var millTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: millCondition,
            effects: new IEffect[] { millEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(millTrigger);
        triggers?.RegisterTriggeredAbility(millTrigger);

        return card;
    }

    /// <summary>
    /// Build the cast-resolve effect: deal <see cref="Damage"/> to each
    /// opponent supplied via <paramref name="opponents"/>, then gain
    /// <see cref="LifeGain"/> life on <paramref name="controller"/>.
    /// Mirrors the on-mill burn half — CR 117.2 — equivalent text =
    /// equivalent effect.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player controller,
        IReadOnlyList<Player> opponents)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(opponents);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: deal {Damage} to each opponent + gain {LifeGain} life",
                () => ApplyBurnAndLifeGain(controller, () => opponents)),
        };
    }

    private static void ApplyBurnAndLifeGain(
        Player controller, Func<IReadOnlyList<Player>>? opponentResolver)
    {
        var opps = opponentResolver?.Invoke();
        if (opps != null)
        {
            foreach (var opp in opps)
            {
                if (ReferenceEquals(opp, controller)) continue;
                Fx.DealDamageAny(opp, Damage);
            }
        }

        Fx.GainLife(controller, LifeGain);
    }
}
