using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aboleth Spawn (Commander Legends: Battle for
/// Baldur's Gate, {3}{U}{U}).
///
/// Creature — Fish Horror 4/3. Oracle text (Scryfall, verified):
///   "Flash
///    Ward {1}
///    When Aboleth Spawn enters, gain control of target creature an
///    opponent controls until end of turn. Untap that creature. It gains
///    haste until end of turn."
///
/// ## Implemented (v1)
/// - 4/3 Creature — Fish Horror at {3}{U}{U}.
/// - <b>Flash (CR 702.8)</b> + <b>Ward {1} (CR 702.21)</b> as
///   <see cref="KeywordAbility"/> markers. Flash gates instant-speed
///   casting; Ward is a keyword-surface marker only (same posture as
///   Kappa Cannoneer / Tolarian Terror — the spell-resolution Ward
///   consultation is a deferred cross-factory gap).
/// - <b>ETB triggered ability (CR 603.6a)</b>: declares a 1..1 "target
///   creature an opponent controls" <see cref="TargetRequest"/>
///   (<see cref="BotIntent.Removal"/> — same intent the bot uses for any
///   "steal-and-attack" effect). <see cref="TargetRequest.CandidateGatherer"/>
///   enumerates every battlefield creature whose controller is not the
///   Spawn's controller (CR 109.1 — "opponent" = any other player).
///   Resolution-time legality re-checks zone + opponent control
///   (CR 608.2b).
/// - <b>Untap (CR 701.20)</b> on resolve via <see cref="Fx.Untap"/>: the
///   "Untap that creature" half is anaphoric on the targeted creature
///   (CR 700.2 / no separate target request).
/// - <b>Haste until end of turn (CR 702.10)</b>: registered as a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the target's
///   <see cref="Creature.ActiveEffects"/>. Lifts summoning sickness for
///   attack-declaration (CR 702.10b) so the stolen creature can swing
///   the same turn it's borrowed — matching Reckless Charge's haste-EOT
///   posture on a separately controlled target.
///
/// ## Deferred (v1 gaps)
/// - <b>"Gain control … until end of turn"</b>: no EOT-bounded
///   control-change primitive exists yet — <see cref="ControlChangeEffect"/>
///   is sealed and ties IsActive to battlefield presence only. v1 ships
///   the untap + haste-EOT halves (the parts that compose cleanly with
///   the granted haste's attack-declaration check via Reckless Charge's
///   continuous-effects pattern) and documents the control-grant gap
///   here; the printed "steal + swing" tempo line collapses to "untap +
///   haste EOT" until a duration-aware control swap lands (sibling gap
///   to Threaten / Act of Treason which currently use the same posture).
/// - <b>Ward {1} trigger wiring</b>: same posture as Tolarian Terror /
///   Kappa Cannoneer — keyword marker present; the cost-paying-or-
///   countering surface lands once the Ward trigger primitive is
///   plumbed onto spell resolution.
/// </summary>
[CardName("Aboleth Spawn")]
public static class AbolethSpawnFactory
{
    public const string CardName = "Aboleth Spawn";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int Power = 4;
    public const int Toughness = 3;

    /// <summary>CR 702.21 — printed Ward cost: {1}.</summary>
    public const string WardCost = "{1}";

    /// <summary>Granted keyword. CR 702.10 — Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// Construct Aboleth Spawn with no live <see cref="TriggerManager"/>
    /// wiring. The ETB trigger is attached structurally for shape /
    /// dispatcher tests; not registered with any trigger bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Aboleth Spawn with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the ETB
    /// trigger registers so a self-ETB <see cref="Events.CardMovedEvent"/>
    /// automatically queues the ability (CR 603.2).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Fish, CardSubtype.Horror });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 702.21 — Ward {1} keyword marker. Same posture as
        // Tolarian Terror / Kappa Cannoneer — the spell-resolution Ward
        // consultation surface is deferred (see class xmldoc).
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a):
        //   "When Aboleth Spawn enters, gain control of target creature an
        //    opponent controls until end of turn. Untap that creature. It
        //    gains haste until end of turn."
        //
        // v1 ships untap + haste-EOT; the control-grant half is a
        // documented cross-factory gap (no EOT-bounded control-change
        // primitive yet — see class xmldoc).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: untap target opponent creature + haste EOT (control-grant deferred)",
            () =>
            {
                if (etb == null) return;
                if (etb.ChosenTargets.Count == 0 || etb.ChosenTargets[0].Count == 0) return;

                if (etb.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 109.1 — re-check the "opponent controls" predicate at
                // resolution. If the target's controller is now the same as
                // Aboleth Spawn's, the printed text no longer applies (and
                // gaining control of your own creature is a no-op anyway).
                var myController = card.Controller ?? owner;
                if (ReferenceEquals(target.Controller, myController)) return;

                // CR 701.20 — Untap. The "Untap that creature" half is
                // anaphoric on the chosen target (CR 700.2).
                Fx.Untap(target);

                // CR 613.1c / CR 702.10 — Haste until end of turn (Layer 6
                // keyword grant). Lifts summoning sickness for
                // attack-declaration (CR 702.10b). Skipped silently when
                // the target has no live continuous-effects service
                // (shape-only test path — mirrors Reckless Charge's
                // ActiveEffects null-guard).
                if (target.ActiveEffects != null)
                {
                    target.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(target, GrantedKeyword));
                }
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 109.1 — opponent-scoped gather. Mirrors Ravenous
                    // Chupacabra's "destroy target creature an opponent
                    // controls" gatherer.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
