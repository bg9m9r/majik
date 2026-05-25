using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Deceiver Exarch (New Phyrexia, {2}{U}).
///
/// Creature — Cleric 1/4. Oracle text:
///   "Flash
///    When this creature enters, choose one —
///     • Untap target permanent an opponent controls.
///     • Untap target noncreature permanent you control."
///
/// ## Implemented (v1)
/// - 1/4 Cleric with Flash keyword marker (<see cref="KeywordAbility"/>
///   — CR 702.8 — Flash gates instant-speed casting).
/// - ETB <see cref="TriggeredAbility"/> (CR 603.6a) with a single 1..1
///   <see cref="TargetRequest"/> covering BOTH printed modes' legal
///   target pool — "target permanent that's either an opponent's or a
///   noncreature you control." CR 700.2d's modal "Choose one —"
///   wording collapses observationally to a union target predicate in
///   v1 since both modes share the same effect ("Untap target ~"); the
///   only difference is target legality, which the resolve-time gate
///   enforces.
/// - Resolution: re-checks the legality at resolution (CR 608.2b /
///   608.2c) — the target must still be on the battlefield AND either
///   controlled by an opponent OR a non-creature controlled by the
///   ability's controller. On legal target, untap if tapped (no-op
///   otherwise — printed "Untap" is idempotent on an already-untapped
///   permanent).
///
/// ## Splinter Twin pairing (the famous Twin combo)
/// When Splinter Twin's granted "{T}: create token copy" ability is
/// activated on Deceiver Exarch, the spawned token is a copy of the
/// Exarch per CR 706.2 — so it inherits the Exarch's printed ETB
/// trigger AND the bearer's Twin-granted activated ability. The
/// token's own ETB fires (CR 603.6a); under mode 2 ("untap target
/// noncreature permanent you control") the Exarch original — wait,
/// the original is a Creature, so mode 2 targets the Splinter Twin
/// aura itself (a noncreature you control). The cleaner Twin loop
/// goes through the Exarch's own tapped state via the
/// Splinter-Twin-attached creature being tapped from the {T} cost,
/// which Pestermite's "tap or untap target permanent" trigger
/// untaps directly. Either way, infinite token spawn ad infinitum.
/// See
/// <see cref="Majik.Core.Tests.Effects.SplinterTwinTokenChainTests"/>
/// for token-inheritance / chain assertions.
///
/// ## Deferred (v1 gaps)
/// - <b>True modal "Choose one —" prompt</b>: v1 collapses the two
///   modes into a single fused-predicate TargetRequest because the
///   underlying effect ("Untap target ~") is identical between modes.
///   When per-ETB-modal infrastructure ships (sibling of the
///   already-shipped <see cref="IzzetCharmFactory"/> / spell-cast
///   modal shape but for triggered abilities), this collapses to two
///   <see cref="TargetRequest"/>s + <c>IPlayerAgent.ChooseModeAsync</c>.
/// - <b>Target legality at choose-time</b>: <c>LegalCandidates</c> is
///   left empty (same posture as Solitude / Snapcaster Mage / Subtlety
///   — production agent enumerates the live battlefield itself);
///   resolve-time recheck enforces the union predicate.
/// </summary>
[CardName("Deceiver Exarch")]
public static class DeceiverExarchFactory
{
    public const string CardName = "Deceiver Exarch";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>Construct Deceiver Exarch owned and controlled by
    /// <paramref name="owner"/>. The ETB trigger is attached structurally;
    /// callers that want bus-driven firing register the returned
    /// <see cref="TriggeredAbility"/> with their <see cref="TriggerManager"/>
    /// (same shape as SnapcasterMage / Subtlety).</summary>
    public const int ModeUntapOpponent = 0;
    public const int ModeUntapOwnNoncreature = 1;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Untap target permanent an opponent controls.",
        "Untap target noncreature permanent you control.",
    };

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash keyword marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // CR 603.6a + CR 700.2d — ETB modal trigger. Each printed mode is
        // exposed to the agent via the list-returning ChooseModeAsync
        // prompt; the resolved closure reads ChosenModes[0] and applies
        // the per-mode legality predicate (mode 0 — opponent's permanent;
        // mode 1 — your noncreature permanent). The TargetRequest stays
        // a union predicate so the agent's target prompt covers both
        // modes' legal pools; resolve-time legality narrows by chosen
        // mode (CR 608.2b — illegal target → no-op).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var condition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName} — untap target permanent (modal — opp's, or own noncreature)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution. Target must still
                // be on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 700.2d — read the chosen mode and apply that mode's
                // legality predicate. Fall back to the union predicate
                // when no mode was picked (legacy callers / tests that
                // pre-date SetChosenModes).
                var modeIndex = etbTrigger.ChosenModes.Count > 0
                    ? etbTrigger.ChosenModes[0]
                    : -1;

                bool legalUnderMode;
                switch (modeIndex)
                {
                    case ModeUntapOpponent:
                        legalUnderMode =
                            target.Controller != null
                            && !ReferenceEquals(target.Controller, owner);
                        break;
                    case ModeUntapOwnNoncreature:
                        legalUnderMode =
                            ReferenceEquals(target.Controller, owner)
                            && !target.HasType(CardType.Creature);
                        break;
                    default:
                        // Legacy fused-predicate fallback (preserves the
                        // pre-modal behaviour the existing test suite
                        // depends on — every permanent legal under either
                        // printed mode passes).
                        var isOpponents = target.Controller != null
                            && !ReferenceEquals(target.Controller, owner);
                        var isOwnNoncreature =
                            ReferenceEquals(target.Controller, owner)
                            && !target.HasType(CardType.Creature);
                        legalUnderMode = isOpponents || isOwnNoncreature;
                        break;
                }

                if (!legalUnderMode) return;

                // CR 701.27 — untap. Idempotent: already-untapped → no-op.
                if (target.IsTapped) target.Untap();
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent an opponent controls, or target noncreature permanent you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            modes: Modes,
            modeIntents: new[]
            {
                BotIntent.None,    // mode 0 — context-dependent
                BotIntent.None,    // mode 1 — context-dependent
            },
            requiredModeCount: 1);

        card.AddAbility(etbTrigger);

        return card;
    }
}
