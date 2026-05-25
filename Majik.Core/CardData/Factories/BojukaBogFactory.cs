using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bojuka Bog (Worldwake / Modern Horizons 2 / many
/// reprints).
///
/// Land. Oracle text:
///   "Bojuka Bog enters tapped.
///    When Bojuka Bog enters, exile target player's graveyard.
///    {T}: Add {B}."
///
/// ## Implemented (v1)
/// - <b>Land</b> with no printed subtype — Bojuka Bog is a plain
///   non-basic Land (no Swamp subtype on the printed face).
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional
///   "Bojuka Bog enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on a supplied
///   <see cref="ReplacementBus"/>, mirroring
///   <see cref="SunscorchedDesertFactory"/>'s wiring. Shape-only path
///   (no <see cref="ReplacementBus"/>) skips registration and the
///   Bog enters untapped — same posture every always-tapped factory
///   (Creeping Tar Pit / Valakut / Sunscorched Desert) takes.
/// - <b>ETB triggered ability (CR 603.6a)</b> — "When Bojuka Bog
///   enters, exile target player's graveyard." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a 1..1
///   "target player" <see cref="TargetRequest"/>. On resolution
///   reads <c>ChosenTargets[0][0]</c>, snapshots that player's
///   graveyard, and moves every card to that player's
///   <see cref="ZoneType.Exile"/>. CR 608.2b — empty graveyard is a
///   clean no-op. Falls back to the controller when no target was
///   set (v1 deterministic path — mirrors
///   <see cref="TormodsCryptFactory"/> / <see cref="NihilSpellbombFactory"/>).
/// - <b>{T}: Add {B}</b> — vanilla <see cref="ManaAbility"/>
///   (CR 605.1 — mana abilities don't use the stack). Declared
///   inline because <c>OracleManaBinder</c> auto-binds the
///   subtype-derived colour only for Basic lands.
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> overload attaches the
/// ETB trigger + mana ability for shape inspection. The
/// <see cref="Create(Player, IEventBus?, TriggerManager?, ReplacementBus?)"/>
/// overload wires the ETB trigger against the
/// <see cref="TriggerManager"/> for bus-driven firing AND registers
/// the enters-tapped replacement.
///
/// ## Deferred (v1 gaps)
/// - <b>Target player agent prompt</b>: v1 reads
///   <c>ChosenTargets[0][0]</c>; if no target was set, falls back to
///   the controller. Full agent-driven targeting deferred (same
///   posture as Tormod's Crypt / Nihil Spellbomb / Relic of Progenitus).
/// - <b>ZoneService routing for the exile sweep</b>: v1 performs raw
///   zone manipulation (Graveyard → Exile) rather than routing through
///   <see cref="ZoneService"/> — same shape Tormod's Crypt /
///   Nihil Spellbomb use. Wire ZoneService through when the
///   broader graveyard-hate sweep audit lands.
/// </summary>
[CardName("Bojuka Bog")]
public static class BojukaBogFactory
{
    public const string CardName = "Bojuka Bog";

    /// <summary>
    /// Construct Bojuka Bog with no live wiring. The ETB trigger is
    /// attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>); the enters-tapped replacement is
    /// omitted (no <see cref="ReplacementBus"/> available). The Bog
    /// enters untapped on this path (matches every other always-tapped
    /// factory's shape-only posture).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Bojuka Bog. When <paramref name="triggers"/> is
    /// supplied the ETB graveyard-exile trigger is registered so bus
    /// events auto-queue it. When <paramref name="replacements"/> is
    /// supplied the enters-tapped restriction is registered so the
    /// Bog enters tapped (CR 614.1c).
    /// </summary>
    public static Land Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Bojuka Bog is just "Land" — no printed subtype on this card.
        var card = new Land(CardName);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Enters-tapped replacement — CR 614.1c.
        //   "Bojuka Bog enters tapped."
        // Unconditional; no gate. Shape-only path (no ReplacementBus)
        // skips registration and the Bog enters untapped.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(card));
        }

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Bojuka Bog enters, exile target player's graveyard."
        // Single 1..1 "target player" TargetRequest; on resolution
        // snapshots the chosen player's graveyard (CR 608.2b — empty
        // graveyard is a clean no-op) and moves each card to that
        // player's Exile zone. Mirrors Tormod's Crypt's target-player
        // graveyard sweep.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var exileEffect = new Effect(
            $"{CardName}: exile target player's graveyard",
            () =>
            {
                if (etbTrigger == null) return;

                // Resolve target player from ChosenTargets; fall back
                // to the controller (v1 deterministic path mirrors
                // Tormod's Crypt / Nihil Spellbomb).
                Player targetPlayer;
                if (etbTrigger.ChosenTargets.Count > 0
                    && etbTrigger.ChosenTargets[0].Count > 0
                    && etbTrigger.ChosenTargets[0][0] is Player chosenPlayer)
                {
                    targetPlayer = chosenPlayer;
                }
                else
                {
                    targetPlayer = owner;
                }

                // Snapshot before mutating — CR 608.2b empty-graveyard
                // case is a clean no-op (the loop body simply doesn't
                // execute).
                var graveyardCards = targetPlayer.Zones.Graveyard.GetCards().ToList();
                foreach (var c in graveyardCards)
                {
                    targetPlayer.Zones.Graveyard.RemoveCard(c);
                    targetPlayer.Zones.Exile.AddCard(c);
                    c.SetZone(ZoneType.Exile);
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { exileEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}: Add {B}
        // CR 605.1 — mana abilities don't use the stack. Declared inline
        // because OracleManaBinder only auto-binds the subtype-derived
        // colour for Basic lands; Bojuka Bog is nonbasic.
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(card, owner, ManaCost.Parse("B")));

        return card;
    }
}
