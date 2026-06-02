using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Squee, Goblin Nabob (Mercadian Masques, {2}{R}).
///
/// Legendary Creature — Goblin 1/1. Oracle text (verified against Scryfall):
///   "At the beginning of your upkeep, you may return this card from your
///    graveyard to your hand."
///
/// ## Implemented (v1)
/// - 1/1 Legendary Goblin with mana cost {2}{R}, owner / controller stamped.
/// - <b>Upkeep recursion trigger (CR 603.1 / CR 500.4 / CR 603.6d — a
///   graveyard-resident trigger)</b>: fires on the controller's own Upkeep
///   <see cref="Majik.Core.Events.StepStartedEvent"/>
///   (<see cref="Triggers.OnStepBegin"/> scoped to the owner) and is active
///   <b>only while Squee is in its owner's Graveyard</b>
///   (<c>activeZones = {Graveyard}</c>). On resolution, the resident zone is
///   re-checked (CR 603.6d) and, if Squee is still in the graveyard, it moves
///   Graveyard → Hand. When a <see cref="ZoneService"/> is wired the move goes
///   through <see cref="ZoneService.MoveCard"/> so zone-change events fire;
///   otherwise a raw zone move is performed.
/// - <b>"You may"</b>: when an <see cref="IPlayerAgent"/> is supplied the
///   return consults <see cref="IPlayerAgent.ChooseYesNoAsync(string,BotIntent,System.Threading.CancellationToken)"/>
///   (<see cref="BotIntent.Reanimate"/> | <see cref="BotIntent.CardAdvantage"/>
///   — a pure upside, so the deterministic bot auto-accepts); a false answer
///   declines and leaves Squee in the graveyard. The no-agent path preserves
///   the legacy auto-accept posture (same as the Bloodghast landfall return).
///
/// Hand-built (not a JSON card-definition) because the declarative
/// card-definition union does not yet expose a "beginning of your upkeep"
/// trigger or a graveyard-resident "return this card to your hand" effect —
/// the same gap documented on <see cref="PhyrexianArenaFactory"/> (upkeep
/// trigger) and <see cref="BloodghastFactory"/> (graveyard-resident return).
/// All the underlying engine primitives already exist; this factory composes
/// them, mirroring those two analogues.
///
/// ## Deferred (v1 gaps)
/// - Async agent path: the return prompt is bridged sync-over-async at effect
///   execution time (the trigger effect is a synchronous <see cref="Effect"/>),
///   matching the Bloodghast landfall return. A fully async resolution that
///   threads a live <see cref="ResolutionContext"/> through
///   <see cref="ZoneService.MoveCardAsync"/> is deferred until the trigger
///   resolution path is uniformly async.
/// </summary>
[CardName("Squee, Goblin Nabob")]
public static class SqueeGoblinNabobFactory
{
    public const string CardName = "Squee, Goblin Nabob";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Squee with no runtime service wiring. The card has the correct
    /// shape (name, type, supertype, P/T, mana cost, subtype) and the upkeep
    /// recursion trigger is attached for structural inspection, but the trigger
    /// is not registered with a <see cref="TriggerManager"/> (fire it manually
    /// in tests) and the "you may" return auto-accepts.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, agent: null);

    /// <summary>
    /// Construct Squee with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone service used by the upkeep trigger to
    /// move Squee from graveyard to hand so zone-change events fire. May be
    /// null — a raw zone move is performed instead.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident trigger
    /// registration (CR 603.6d). May be null — the trigger is attached to the
    /// card for shape but not registered with the bus.</param>
    /// <param name="agent">Optional agent consulted for the "you may" return
    /// (<see cref="BotIntent.Reanimate"/> | <see cref="BotIntent.CardAdvantage"/>).
    /// Null preserves the legacy auto-accept posture.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Goblin });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep recursion trigger — CR 603.1, CR 500.4, CR 603.6d.
        //   "At the beginning of your upkeep, you may return this card from
        //    your graveyard to your hand."
        // Active only while Squee is in its owner's Graveyard
        // (activeZones = {Graveyard}). Triggers.OnStepBegin filters the
        // StepStartedEvent on (Upkeep, owner) so it fires only on the
        // controller's own upkeeps.
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return from graveyard to hand (upkeep trigger)",
            async ctx =>
            {
                // CR 603.6d — re-check zone at resolution. If Squee has left
                // the graveyard since the trigger was put on the stack, do
                // nothing.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                // "You may" — consult the agent when wired; else auto-accept
                // (legacy posture, same as the Bloodghast landfall return).
                if (agent != null)
                {
                    var yes = await agent.ChooseYesNoAsync(
                        "Return Squee, Goblin Nabob from your graveyard to your hand?",
                        BotIntent.Reanimate | BotIntent.CardAdvantage).ConfigureAwait(false);
                    if (!yes) return;
                }

                if (zoneService != null)
                {
                    // ZoneService.MoveCard fires zone-change events (CR 603.6a)
                    // so portal/log subscribers see the recursion.
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Hand, owner);
                }
                else
                {
                    // Raw zone move — no zone-change event published.
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        return card;
    }
}
