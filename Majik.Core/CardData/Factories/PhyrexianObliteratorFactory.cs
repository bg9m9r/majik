using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Obliterator (New Phyrexia,
/// {B}{B}{B}{B}).
///
/// Creature — Horror 5/5. Oracle text (Scryfall, verified):
///   "Trample
///    Whenever a source deals damage to Phyrexian Obliterator, that
///    source's controller sacrifices that many permanents."
///
/// ## Implemented (v1)
/// - 5/5 Creature — Horror at {B}{B}{B}{B}.
/// - <b>Trample (CR 702.19)</b>: <see cref="KeywordAbility"/>("Trample")
///   marker — combat code reads via
///   <see cref="Majik.Core.Combat.CombatAbilities"/>, same wiring shape
///   as every other named factory carrying Trample (Reality Smasher,
///   Temur Battle Rage, etc.).
/// - <b>Damage-received trigger (CR 603.1)</b>: triggered ability over
///   <see cref="DamageDealtEvent"/> filtered to
///   <c>TargetCard == this card</c>. Active in
///   <see cref="ZoneType.Battlefield"/> (the trigger only fires while
///   the Obliterator is in play — CR 603.6a + 614.12).
///
///   On resolution the source's controller sacrifices N permanents
///   where N is the damage amount captured off the live event. The
///   sacrifice picks route through
///   <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> when an
///   <paramref name="agentSelector"/> resolves an agent for the
///   "victim" (the source's controller); without an agent the
///   deterministic first-N-permanents fallback applies — same agent /
///   fallback posture as <see cref="Keywords.AnnihilatorFactory"/>.
///
///   Sacrifices route through <see cref="Fx.Sacrifice"/> → <see
///   cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Sacrifice"/> so the binder bypasses
///   Indestructible / regeneration (CR 701.16 / 702.12b / 701.15c)
///   and tokens cease to exist on the next SBA pass (CR 110.7 /
///   704.5d).
///
/// ## Edge-case notes
/// - <b>Sourceless damage</b>: a <see cref="DamageDealtEvent"/> without
///   a <see cref="DamageDealtEvent.SourceCard"/> falls back to the
///   <see cref="DamageDealtEvent.SourcePlayer"/> (player-as-source for
///   "you take X damage" effects like Sulfuric Vortex). When the
///   source's controller can't be resolved we no-op the trigger —
///   matches the printed-card reading "<em>that source's controller</em>"
///   (CR 119.1 — controller of a damage source must exist for the
///   sacrifice clause to apply).
/// - <b>Combat damage</b>: <see cref="CombatDamageDealtEvent"/>
///   subclasses <see cref="DamageDealtEvent"/>, so the
///   <see cref="EventTriggerCondition{T}"/> over the base event catches
///   both combat and non-combat hits (Lightning Bolt at the Obliterator,
///   Phyrexian Arena ping, etc.). Combat damage is the canonical case —
///   trampler / blocker math is unchanged.
/// - <b>Zero-amount damage</b>: prevention can reduce the amount to
///   zero (CR 615.5). The trigger's condition gates on
///   <c>Amount &gt; 0</c> — no trigger fires for prevented damage, no
///   sacrifice resolves. Matches printed-card reading.
/// - <b>Obliterator dies mid-trigger</b>: the trigger goes on the stack
///   when damage is dealt; SBAs after damage will move Obliterator to
///   the graveyard (5/5 vs lethal damage). The trigger still resolves
///   from the stack independently — the source's controller still
///   sacrifices N permanents even after Obliterator leaves play
///   (CR 603.6a — triggered abilities don't need their source to
///   persist past triggering).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The trigger is attached
///   but not registered with any <see cref="TriggerManager"/>; sacrifice
///   picks route through the deterministic fallback.
/// - <see cref="Create(Player, TriggerManager?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired. The trigger registers with the trigger bus so
///   <see cref="DamageDealtEvent"/> automatically queues the ability,
///   and the sacrifice prompt consults
///   <paramref name="agentSelector"/> for the source-controller's
///   picks.
/// </summary>
[CardName("Phyrexian Obliterator")]
public static class PhyrexianObliteratorFactory
{
    public const string CardName = "Phyrexian Obliterator";
    public const string PrintedManaCost = "{B}{B}{B}{B}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Phyrexian Obliterator with no live wiring. The Trample
    /// marker + damage-received trigger are attached; nothing registers
    /// with a trigger bus. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, agentSelector: null);

    /// <summary>
    /// Construct Phyrexian Obliterator with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the damage-received trigger
    /// registers with the bus so a <see cref="DamageDealtEvent"/>
    /// automatically queues the ability (CR 603.2).</param>
    /// <param name="agentSelector">When supplied, the sacrifice prompt
    /// consults the source-controller's
    /// <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> for each of
    /// the N picks (<see cref="Cards.BotIntent.Removal"/>). Null falls
    /// back to deterministic first-N-permanents.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Horror });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample marker. Combat-side reads via
        // CombatAbilities; the marker keeps the keyword scan surface
        // uniform with every other named Trample factory.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Damage-received trigger — CR 603.1.
        //   "Whenever a source deals damage to Phyrexian Obliterator,
        //    that source's controller sacrifices that many permanents."
        //
        // Same condition/effect closure shape as Boros Reckoner — capture
        // the live event's amount + source's controller in a closure and
        // forward to the resolved effect. The condition is intentionally
        // over the base DamageDealtEvent type so combat AND non-combat
        // pings both fire (CombatDamageDealtEvent subclasses
        // DamageDealtEvent — TriggerCondition.Matches against the base
        // type catches both).
        // ----------------------------------------------------------------
        int capturedAmount = 0;
        Player? capturedVictim = null;

        var condition = new EventTriggerCondition<DamageDealtEvent>((e, _) =>
        {
            if (e.TargetCard is not Creature recv) return false;
            if (!ReferenceEquals(recv, card)) return false;
            if (e.Amount <= 0) return false;

            // CR 119.1 — "source's controller". Damage sources are
            // either a card (creature combat / spell / ability) or a
            // player (player-as-source). Prefer the card's controller
            // (canonical for "the source's controller"); fall back to
            // the SourcePlayer for sourceless / player-as-source
            // damage.
            var sourceController = e.SourceCard?.Controller ?? e.SourcePlayer;
            if (sourceController == null) return false;

            capturedAmount = e.Amount;
            capturedVictim = sourceController;
            return true;
        });

        var effect = new Effect(
            $"{CardName}: source's controller sacrifices N permanents (N = damage taken)",
            () =>
            {
                var victim = capturedVictim;
                var n = capturedAmount;
                // Clear immediately so a stale closure can't double-fire
                // if the trigger re-resolves on a follow-up damage event
                // before the condition repopulates the closure.
                capturedVictim = null;
                capturedAmount = 0;

                if (victim == null || n <= 0) return;

                var sacrificed = 0;
                while (sacrificed < n)
                {
                    // Re-read each iteration — the previous sacrifice
                    // can trigger LTB / SBAs that remove additional
                    // permanents from the victim's battlefield, so a
                    // snapshot taken once would race.
                    var candidates = victim.Zones.Battlefield
                        .GetCards()
                        .ToList();
                    if (candidates.Count == 0) break;

                    ICard? pick;
                    var agent = agentSelector?.Invoke(victim);
                    if (agent != null)
                    {
                        pick = agent.ChooseFromBattlefieldAsync(
                                victim,
                                candidates,
                                Cards.BotIntent.Removal)
                            .GetAwaiter().GetResult();
                        // CR 608.2b — illegal-on-resolution check.
                        // If the agent returns something not on the
                        // victim's battlefield (or null), fall back
                        // to the first candidate.
                        if (pick == null
                            || pick.Zone != ZoneType.Battlefield
                            || !ReferenceEquals(pick.Controller, victim))
                        {
                            pick = candidates[0];
                        }
                    }
                    else
                    {
                        // Deterministic v1 fallback — first permanent.
                        pick = candidates[0];
                    }

                    // CR 701.16 / 702.12b / 701.15c — sacrifice
                    // bypasses Indestructible + regeneration.
                    Fx.Sacrifice(pick);
                    sacrificed++;
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
