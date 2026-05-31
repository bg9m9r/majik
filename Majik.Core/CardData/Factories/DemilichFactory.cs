using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Demilich (Innistrad: Midnight Hunt-pillar UB
/// finisher, {3}{U}{U}{U}).
///
/// Engine-friendly oracle text shipped here (matches MODERN_COVERAGE.md
/// backlog row #10 and the Murktide / Izzet Tempo pillar wiring):
///   "Flying.
///    This spell costs {U} less to cast for each instant or sorcery card
///    in your graveyard.
///    When you cast this spell, exile two instants or sorceries from your
///    graveyard."
///
/// ## Implemented (v1)
/// - Creature — Zombie Wizard 4/3 at {3}{U}{U}{U}; owner / controller wired.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker; combat
///   code reads it the same way Murktide Regent's Flying is wired.
/// - <b>Self cost reduction (CR 117.7)</b>: <see cref="CostReductionAbility"/>
///   in <see cref="CostReductionAbility.TotalReducer"/> shape — counts
///   instant / sorcery cards in the caster's graveyard at cost-calc time
///   and reduces generic mana by that count. Coloured pips are untouched
///   (CR 117.7c) and the cost is floored at zero inside
///   <see cref="CostReduction.GetEffectiveCost"/>, so:
///     - 0 in graveyard → pays {3}{U}{U}{U}
///     - 3 in graveyard → pays {U}{U}{U}
///     - 5 in graveyard → still pays {U}{U}{U} (floor at coloured pips —
///       reducer can drive generic to 0 but cannot touch the three blue
///       pips per CR 117.7c).
/// - <b>On-cast trigger (CR 603.6a / CR 603.10)</b>: triggered ability
///   over <see cref="SpellCastEvent"/> filtered to
///   <c>ReferenceEquals(e.Spell.Card, card)</c> (same self-cast detection
///   posture as <see cref="UlamogTheCeaselessHungerFactory"/> /
///   <see cref="CrashingFootfallsFactory"/>). On resolution, two instant
///   or sorcery cards are exiled from the caster's graveyard.
///   - <b>Exile target selection</b>: when an <see cref="IPlayerAgent"/>
///     is registered via <see cref="AgentRegistry"/>, the controller is
///     asked to pick up to two instant/sorcery cards from their own
///     graveyard via <see cref="IPlayerAgent.ChooseFromPileAsync"/>
///     (Intent: <see cref="BotIntent.None"/> — exile-from-own-graveyard
///     as an obligatory cast cost is not classified as upside / removal).
///     No agent → deterministic first-two-eligible-cards fallback (same
///     posture as Ledger Shredder's surveil fallback).
///   - <b>Active zone</b>: trigger registered with
///     <see cref="ZoneType.Stack"/> active zones (matches Ulamog /
///     Crashing Footfalls — the spell is on the stack when
///     <see cref="SpellCastEvent"/> fires).
///   - Routes the two graveyard → exile moves through
///     <see cref="ZoneService.MoveCard"/> when a zone service is supplied
///     so <see cref="CardMovedEvent"/> publishes; raw zone manipulation
///     otherwise. Mirrors Ulamog's two-mode wiring.
///
/// ## Deferred (v1 gaps)
/// - <b>Flashback-style "cast from graveyard"</b>: real-card Demilich also
///   has "You may cast this card from your graveyard by exiling four
///   instant and/or sorcery cards from your graveyard in addition to
///   paying its other costs." That clause is intentionally NOT shipped
///   here — this v1 follows the MODERN_COVERAGE row #10 / engine-spec
///   surface (graveyard-count cost reduction + on-cast exile two only),
///   and the cast-from-graveyard alt-cost surface is the same shape as
///   Yawgmoth's Will / Escape (see <see cref="EscapeAlternativeCost"/>)
///   pending a generalised cast-from-graveyard-with-additional-exile
///   primitive.
/// - <b>Attack-trigger copy-from-graveyard clause</b>: same — not in the
///   engine-spec surface; Murktide already covers the
///   exile-instant/sorcery-from-graveyard target shape if a later PR
///   wants to layer the attack copy on top.
/// - <b>"You may"</b>: real-card Demilich's attack clause is a "may", but
///   the on-cast clause shipped here is non-optional ("exile two ...
///   from your graveyard"). No <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///   prompt is needed.
/// </summary>
[CardName("Demilich")]
public static class DemilichFactory
{
    public const string CardName = "Demilich";
    public const string PrintedManaCost = "{3}{U}{U}{U}";
    public const int Power = 4;
    public const int Toughness = 3;
    public const int OnCastExileCount = 2;

    /// <summary>
    /// Construct Demilich with no live wiring. All abilities are attached
    /// for shape observability; the on-cast trigger is not registered with
    /// any <see cref="TriggerManager"/>; exile moves use raw zone
    /// manipulation. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Demilich with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the on-cast exile moves route
    /// through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers (Containment Priest, Tormod's Crypt, etc.).</param>
    /// <param name="triggers">When supplied, the on-cast trigger
    /// registers with the bus so a <see cref="SpellCastEvent"/> for this
    /// card lands the trigger on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Zombie, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Marker only; combat code reads
        // KeywordAbility (same wiring shape as Murktide Regent).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {U} less to cast for each instant
        // or sorcery card in your graveyard." Whole-reduction shape
        // (CostReductionAbility(totalReducer)) — the function counts
        // instants/sorceries in the caster's graveyard at cost-calc time.
        // CR 117.7c — cost cannot drive coloured pips below printed; the
        // floor at zero on generic mana is enforced inside
        // CostReduction.GetEffectiveCost, so the three {U} pips remain
        // regardless of graveyard size.
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster =>
            {
                if (caster?.Zones?.Graveyard == null) return 0;
                var n = 0;
                foreach (var g in caster.Zones.Graveyard.GetCards())
                {
                    if (g.HasType(CardType.Instant) || g.HasType(CardType.Sorcery)) n++;
                }
                return n;
            },
            description:
                "This spell costs {U} less to cast for each instant or " +
                "sorcery card in your graveyard."));

        // ----------------------------------------------------------------
        // On-cast trigger — CR 603.6a / CR 603.10.
        //   "When you cast this spell, exile two instants or sorceries
        //    from your graveyard."
        // Fires while Demilich is on the stack (SpellCastEvent is
        // published as the spell moves to the stack), so activeZones =
        // Stack — matches Ulamog / Crashing Footfalls self-cast wiring.
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var castEffect = new Effect(
            $"{CardName}: exile two instants or sorceries from your graveyard (on cast)",
            async ctx =>
            {
                // CR 603.10b illegal-on-resolution recheck — re-pull the
                // caster's graveyard at resolve time; the controller may
                // have shifted from the cast moment to now.
                var controller = card.Controller ?? owner;
                var candidates = controller.Zones.Graveyard.GetCards()
                    .Where(c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
                    .Cast<ICard>()
                    .ToList();
                if (candidates.Count == 0) return;

                // Pick up to OnCastExileCount cards. Agent-prompt when
                // available (Intent: GraveyardManagement); first-N
                // fallback otherwise. Mirrors Ledger Shredder's
                // agent-or-default surveil-decision posture.
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                var picks = new List<ICard>(OnCastExileCount);
                var remaining = candidates.ToList();
                for (var i = 0; i < OnCastExileCount && remaining.Count > 0; i++)
                {
                    ICard? choice;
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute
                        // becomes async (same pattern as ConsiderFactory).
                        choice = (await agent.ChooseFromPileAsync(
                            controller,
                            remaining,
                            $"{CardName}: choose an instant or sorcery to exile from your graveyard",
                            BotIntent.None).ConfigureAwait(false));
                    }
                    else
                    {
                        choice = remaining[0];
                    }
                    if (choice == null) break;
                    picks.Add(choice);
                    remaining.Remove(choice);
                }

                foreach (var pick in picks)
                {
                    if (pick is not Card pickCard) continue;
                    // Re-verify: still a legal pick at the exact move
                    // moment. CR 701.21 — exile is a zone change.
                    if (pickCard.Zone != ZoneType.Graveyard) continue;
                    if (!pickCard.HasType(CardType.Instant)
                        && !pickCard.HasType(CardType.Sorcery)) continue;

                    if (zones != null)
                    {
                        zones.MoveCard(pickCard, ZoneType.Graveyard, ZoneType.Exile);
                    }
                    else
                    {
                        var graveOwner = pickCard.Owner ?? controller;
                        graveOwner.Zones.Graveyard.RemoveCard(pickCard);
                        graveOwner.Zones.Exile.AddCard(pickCard);
                        pickCard.SetZone(ZoneType.Exile);
                    }
                }
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            interveningIf: null,
            // CR 603.6a — fires while Demilich is on the stack.
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
