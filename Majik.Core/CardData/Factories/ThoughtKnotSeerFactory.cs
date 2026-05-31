using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thought-Knot Seer (Oath of the Gatewatch, {3}{C}).
///
/// Creature — Eldrazi 4/4. Oracle text (Scryfall, verified):
///   "When this creature enters, target opponent reveals their hand. You
///    choose a nonland card from it and exile that card.
///    When this creature leaves the battlefield, target opponent draws a
///    card."
///
/// ## Implemented (v1)
/// - 4/4 Creature — Eldrazi at {3}{C}.
/// - <b>ETB triggered ability (CR 603.6a)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). One 1..1 "target opponent" <see cref="TargetRequest"/>
///   tagged <see cref="BotIntent.HandHate"/> so the heuristic bot can
///   distinguish this prompt from a self-discard
///   (<see cref="BotIntent.Discard"/>). On resolution:
///   1. revealed-hand surfacing — the bus-level
///      <see cref="CardRevealedEvent"/> fan-out is handled by the outer
///      SpellCastFlow / TriggerManager (same posture as Grief / Cabal
///      Therapy; no inline event synthesis here);
///   2. Thought-Knot's <em>controller</em> picks a nonland card from the
///      revealed hand via <see cref="IPlayerAgent.ChooseFromHandAsync"/>
///      (intent = HandHate). The candidate list is pre-filtered to nonland
///      cards. A null pick from the agent falls back to the first nonland
///      card (legacy deterministic posture matching Grief / Liliana of the
///      Veil's discard fallback);
///   3. the chosen card is exiled — moved Hand → Exile via
///      <see cref="ZoneService.MoveCard"/> when supplied (publishes
///      <see cref="CardMovedEvent"/>) or raw zone mutation otherwise.
///   Empty hand / lands-only hand → clean no-op.
/// - <b>LTB triggered ability (CR 603.6c / CR 603.10c)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, FromZone =
///   Battlefield). One 1..1 "target opponent" <see cref="TargetRequest"/>;
///   on resolution the chosen player draws one card
///   (<see cref="Player.DrawCard"/>). Per the oracle wording the LTB target
///   is independently chosen — most decks point it back at the same
///   opponent, but Modern allows multi-opponent groups so the engine
///   honours the printed TARGET wording (the prompt-time agent picks).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both triggers attached for
///   shape observability; not registered with any <see cref="TriggerManager"/>,
///   no <see cref="ZoneService"/> wiring. Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired. Triggers register with <paramref name="triggers"/>; the
///   exile zone-move publishes <see cref="CardMovedEvent"/> via
///   <paramref name="zones"/>; controller's <see cref="IPlayerAgent"/> is
///   sourced via <paramref name="agentSelector"/> for the nonland pick.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event fan-out</b>: no inline <see cref="CardRevealedEvent"/>
///   per revealed card (same posture as Grief; future RevealHelper-aligned
///   PR can lift this).
/// </summary>
[CardName("Thought-Knot Seer")]
public static class ThoughtKnotSeerFactory
{
    public const string CardName = "Thought-Knot Seer";
    public const string PrintedManaCost = "{3}{C}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Thought-Knot Seer with no live wiring. Both triggers are
    /// attached for shape observability; neither is registered with a
    /// <see cref="TriggerManager"/>; the ETB exile uses raw zone manipulation.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null, agentSelector: null);

    /// <summary>
    /// Construct Thought-Knot Seer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the ETB exile routes through
    /// <see cref="ZoneService.MoveCard"/> so <see cref="CardMovedEvent"/>
    /// publishes for any zone-change subscribers.</param>
    /// <param name="triggers">When supplied, both triggers register with
    /// the bus so the corresponding <see cref="CardMovedEvent"/>s land them
    /// on the stack automatically (CR 603.2).</param>
    /// <param name="agentSelector">When supplied, the ETB nonland pick
    /// consults the owning controller's <see cref="IPlayerAgent"/> via
    /// <see cref="IPlayerAgent.ChooseFromHandAsync"/>
    /// (<see cref="BotIntent.HandHate"/>). Otherwise the pick falls back
    /// to the first nonland card in the revealed hand (mirrors Grief /
    /// Liliana of the Veil's discard fallback posture).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.16 (Reveal) /
        // CR 701.21 (Exile).
        //   "When this creature enters, target opponent reveals their
        //    hand. You choose a nonland card from it and exile that card."
        // Single 1..1 "target opponent" TargetRequest; agent-pick from
        // hand on resolution (BotIntent.HandHate).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: target opponent reveals hand → controller exiles a nonland card",
            async ctx =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player victim) return;

                // CR 701.16 — "Target opponent reveals their hand" is a
                // public state transition. The factory shell does not
                // synthesise a per-card reveal event (matches Grief's
                // posture); UI wiring lifts through the outer event bus
                // when a TriggerManager / SpellCastFlow is attached.

                // CR 701.21 — "You choose a nonland card from it and exile
                // that card." Controller's agent picks; deterministic
                // fallback = first nonland card.
                var nonlandHand = victim.Zones.Hand.GetCards()
                    .Where(c => !c.HasType(CardType.Land))
                    .ToList();
                if (nonlandHand.Count == 0) return; // hand empty or lands-only

                ICard? pick = null;
                var chooser = card.Controller ?? owner;
                var agent = agentSelector?.Invoke(chooser);
                if (agent != null)
                {
                    pick = (await agent.ChooseFromHandAsync(victim, nonlandHand, BotIntent.HandHate).ConfigureAwait(false));
                    // Guard: agent may return an illegal pick (left hand,
                    // is a land). Fall back deterministically.
                    if (pick == null
                        || pick.Zone != ZoneType.Hand
                        || pick.HasType(CardType.Land)
                        || !ReferenceEquals(pick.Owner, victim))
                    {
                        pick = nonlandHand[0];
                    }
                }
                else
                {
                    pick = nonlandHand[0];
                }

                // CR 701.21 — exile is a zone change. Route through
                // ZoneService when supplied so CardMovedEvent fires for
                // any zone-change subscribers (Containment Priest,
                // Tormod's Crypt, etc.).
                if (zones != null)
                {
                    zones.MoveCard(pick, ZoneType.Hand, ZoneType.Exile);
                }
                else
                {
                    victim.Zones.Hand.RemoveCard(pick);
                    victim.Zones.Exile.AddCard(pick);
                    pick.SetZone(ZoneType.Exile);
                }
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.HandHate),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "When this creature leaves the battlefield, target opponent
        //    draws a card."
        // Matches any FromZone == Battlefield movement (dies, bounce,
        // exile — all qualify per CR 603.10c).
        // ----------------------------------------------------------------
        TriggeredAbility? ltb = null;
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName}: target opponent draws a card (LTB)",
            () =>
            {
                if (ltb == null) return;
                var chosen = ltb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player drawTarget) return;

                // CR 121.1 — "draws a card" routes through the shared
                // Fx.DrawCards primitive so the empty-library state-loss
                // marker is set if the library is empty (CR 704.5b).
                Fx.DrawCards(drawTarget, 1);
            });

        ltb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last
            // existed on the battlefield. ActiveZones = Battlefield
            // matches the "looks back" semantics other LTB triggers
            // (Spell Queller, Wurmcoil Engine) already rely on.
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // Drawing a card for the opponent is a downside for
                    // the Thought-Knot Seer controller — but the printed
                    // text doesn't gate the trigger; tag with Draw so the
                    // bot ranker recognises the shape (the rider is paid
                    // for by the ETB exile + the body's mana value).
                    Intent: BotIntent.Draw),
            });

        card.AddAbility(ltb);
        triggers?.RegisterTriggeredAbility(ltb);

        return card;
    }
}
