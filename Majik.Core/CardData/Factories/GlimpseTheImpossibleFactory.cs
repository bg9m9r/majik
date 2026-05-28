using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glimpse the Impossible (The Brothers' War,
/// {2}{R}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Exile the top three cards of your library. You may play those cards
///    this turn. At the beginning of the next end step, if any of those
///    cards remain exiled, put them into your graveyard, then create a 0/1
///    colorless Eldrazi Spawn creature token for each card put into your
///    graveyard this way. Those tokens have 'Sacrifice this token: Add
///    {C}.'"
///
/// ## Implemented (v1)
/// - Sorcery shape, printed mana cost {2}{R}, MV 3 (CR 202.3).
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/>): exile the top
///   three cards of the caster's library to the caster's exile zone (CR
///   701.20). Stamp a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) on each exiled card so the
///   caster may cast them from exile via
///   <see cref="ExileCastAlternativeCost"/> for their printed mana cost
///   (CR 118.9). "You may play those cards this turn" — the grant clears
///   at the next end step (see below).
/// - <b>"This turn" cast window</b>: the exile-cast grants are live from
///   resolution until the next end step fires. This differs from Reckless
///   Impulse's "until end of your next turn" (two Cleanup steps) — here
///   the printed text says "this turn" which collapses to "at the beginning
///   of the next end step" (the same turn's End step, CR 603.7).
/// - <b>Delayed End-step trigger (CR 603.7)</b>: when a
///   <see cref="TriggerManager"/> is supplied the resolve body registers a
///   one-shot <see cref="DelayedTriggeredAbility"/> that fires at the first
///   End step strictly after this resolve. On firing:
///   1. Any of the three originally-exiled cards that are STILL in the
///      caster's exile zone (not yet cast) are moved to the caster's
///      graveyard (CR 701.20).
///   2. One 0/1 colourless Eldrazi Spawn creature token is created for each
///      card moved to the graveyard this way (CR 111 / CR 111.4) via
///      <see cref="TokenFactory.CreateEldraziSpawn"/>. Each token carries
///      "Sacrifice this token: Add {C}." (v1: ManaAbility producing {C};
///      the sac cost rider is a deferred gap shared with EldraziSkyspawner
///      and TokenFactory.CreateEldraziSpawn).
///   3. The exile-cast grants on any exiled cards that were NOT cast are
///      cleared (they're now in the graveyard anyway; this is belt-and-
///      suspenders cleanup matching LightUpTheStage's ClearRuntimeExileCast
///      pattern).
///
/// ## Deferred (v1 gaps)
/// - <b>"May play" includes lands</b>: the exile-cast grant authorises
///   spell-casting; playing a land from exile would need a separate
///   "play this land from exile" grant. Same posture as LightUpTheStage /
///   RecklessImpulse v1.
/// - <b>Sac cost on Spawn token's mana ability</b>: same gap documented in
///   <see cref="TokenFactory.CreateEldraziSpawn"/> and EldraziSkyspawner —
///   the ManaAbility produces {C} without enforcing sacrifice.
/// - <b>Empty-library mid-exile</b>: v1 stops at library underflow without
///   raising an SBA flag (CR 701.20 does not impose one for exile, matching
///   RecklessImpulse / LightUpTheStage posture).
/// </summary>
[CardName("Glimpse the Impossible")]
public static class GlimpseTheImpossibleFactory
{
    public const string CardName = "Glimpse the Impossible";
    public const string PrintedManaCost = "{2}{R}";
    public const int CardsExiled = 3;

    /// <summary>
    /// Construct Glimpse the Impossible with no live runtime wiring. The
    /// card is shape-only — suitable for identity / dispatcher tests.
    /// The resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Glimpse the Impossible's resolve effect.
    ///
    /// <para>Phase 1 (immediate): exile the top three cards of
    /// <paramref name="caster"/>'s library and stamp a runtime exile-cast
    /// grant on each so the caster may cast them from exile for their
    /// printed mana cost this turn.</para>
    ///
    /// <para>Phase 2 (delayed — requires <paramref name="triggers"/>):
    /// register a one-shot <see cref="DelayedTriggeredAbility"/> (CR 603.7)
    /// that fires at the next End step. On resolution it inspects which of
    /// the three originally-exiled cards are still in exile (not yet cast),
    /// moves those to the caster's graveyard, and creates one 0/1 colourless
    /// Eldrazi Spawn token (with "Sacrifice this token: Add {C}.") for each
    /// card moved. Exile-cast grants on moved cards are cleared.</para>
    /// </summary>
    /// <param name="caster">The player who cast Glimpse the Impossible.</param>
    /// <param name="triggers">Optional TriggerManager. When null the exile
    /// and cast-grant still happen; only the delayed end-step rider is
    /// skipped (shape-only test path).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                "Glimpse the Impossible: exile top 3 + cast-this-turn grant + end-step graveyard/Spawn rider",
                () =>
                {
                    // ----------------------------------------------------------
                    // Phase 1: exile top 3 and grant exile-cast for this turn.
                    // CR 701.20 — move each card from library to exile zone.
                    // CR 118.9 — stamp RuntimeExileCast so ExileCastAlternativeCost
                    //            gates casts from exile for the caster only.
                    // ----------------------------------------------------------
                    var stamped = new List<Card>(CardsExiled);
                    for (var i = 0; i < CardsExiled; i++)
                    {
                        var top = caster.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null) break; // library underflow — CR 701.20 no SBA

                        caster.Zones.Library.RemoveCard(top);
                        caster.Zones.Exile.AddCard(top);
                        top.SetZone(ZoneType.Exile);

                        if (top is Card concrete)
                        {
                            concrete.GrantRuntimeExileCast(caster, concrete.ManaCostValue);
                            stamped.Add(concrete);
                        }
                    }

                    if (stamped.Count == 0 || triggers == null) return;

                    // ----------------------------------------------------------
                    // Phase 2: delayed end-step trigger (CR 603.7).
                    //
                    // "At the beginning of the next end step, if any of those
                    //  cards remain exiled, put them into your graveyard, then
                    //  create a 0/1 colorless Eldrazi Spawn creature token for
                    //  each card put into your graveyard this way."
                    //
                    // fires on first StepStartedEvent(End) strictly after this
                    // resolve (same fence used by WrennsResolveFactory /
                    // MishrasBaubleFactory). TriggerManager auto-unregisters
                    // delayed triggers after they fire.
                    // ----------------------------------------------------------
                    var resolvedAt = DateTime.UtcNow;

                    var endStepEffect = new Effect(
                        "Glimpse the Impossible: exile → graveyard + create Eldrazi Spawn tokens (delayed end step)",
                        () =>
                        {
                            var movedToGraveyard = 0;

                            foreach (var c in stamped)
                            {
                                // Only cards STILL in the caster's exile zone
                                // are moved. Already-cast cards are no longer
                                // in exile (they went to the stack / graveyard /
                                // battlefield) — "if any of those cards remain
                                // exiled" (CR 603.7 — condition checked at
                                // trigger resolution).
                                if (c.Zone != ZoneType.Exile) continue;
                                if (!caster.Zones.Exile.GetCards().Contains(c)) continue;

                                // CR 701.20 — move from exile to caster's graveyard.
                                caster.Zones.Exile.RemoveCard(c);
                                caster.Zones.Graveyard.AddCard(c);
                                c.SetZone(ZoneType.Graveyard);

                                // Belt-and-suspenders: clear the cast grant now
                                // that the card has left exile (grant is moot).
                                c.ClearRuntimeExileCast();

                                movedToGraveyard++;
                            }

                            // CR 111 / CR 111.4 — create one 0/1 colourless Eldrazi
                            // Spawn creature token for each card moved to the
                            // graveyard. If all three were cast, movedToGraveyard == 0
                            // and no tokens are created.
                            for (var i = 0; i < movedToGraveyard; i++)
                            {
                                TokenFactory.CreateEldraziSpawn(caster);
                            }
                        });

                    var delayed = new DelayedTriggeredAbility(
                        source: caster,
                        controller: caster,
                        condition: new EventTriggerCondition<StepStartedEvent>(
                            (e, _) => e.StepType == PhaseStateType.End
                                      && e.Timestamp > resolvedAt),
                        effects: new IEffect[] { endStepEffect });

                    triggers.RegisterDelayed(delayed);
                }),
        };
    }
}
