using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Teetering Peaks (Zendikar, mono-red enters-tapped
/// pump land). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, target creature gets +2/+0 until end of turn.
///    {T}: Add {R}."
///
/// <para>
/// The card shape — name, Land type, and the single {R} mana ability
/// (CR 605.1a — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/teetering-peaks.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, mirroring the
/// JSON-driven posture of <see cref="AkoumRefugeFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>. The shape-only single-arg path skips the
/// registration (no bus available) — same posture as
/// <see cref="AkoumRefugeFactory"/>.
/// </para>
///
/// <para>
/// The "When this land enters, target creature gets +2/+0 until end of turn"
/// clause is a targeted ETB triggered ability (CR 603.6a). It is hand-rolled
/// here — not declared in the JSON — because the declarative effect-verb
/// schema has no "pump target creature" verb yet (the targeted JSON verbs are
/// destroy / exile / return / untap / tap / damage / prevent-damage /
/// lose-life / gain-control). The trigger declares a single 1..1
/// "target creature" <see cref="TargetRequest"/> whose
/// <see cref="TargetRequest.CandidateGatherer"/> enumerates every creature on
/// the battlefield ("target creature" means ANY creature — no opponent
/// restriction). On resolution it reads the chosen target off
/// <see cref="TriggeredAbility.ChosenTargets"/> and registers a
/// <see cref="PumpUntilEndOfTurnEffect"/>(+2, +0) on the target's
/// <see cref="Creature.ActiveEffects"/> (CR 613.1g layer 7c; CR 514.2 —
/// expires in cleanup) — the same pump primitive used by
/// <see cref="DistortionStrikeFactory"/> / the pump-template family. CR 608.2b
/// — an illegal target at resolution (no longer a creature on the
/// battlefield) fizzles cleanly (no-op).
/// </para>
/// </summary>
[CardName("Teetering Peaks")]
public static class TeeteringPeaksFactory
{
    public const string CardName = "Teetering Peaks";
    public const string Slug = "teetering-peaks";

    /// <summary>Layer 7c power bonus (CR 613.1g).</summary>
    public const int PumpPower = 2;

    /// <summary>Layer 7c toughness bonus (CR 613.1g) — +0.</summary>
    public const int PumpToughness = 0;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Teetering Peaks owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against; the ETB trigger is
    /// attached structurally but not registered with any trigger bus). This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>Construct Teetering Peaks with optional replacement-bus +
    /// trigger-manager wiring.</summary>
    /// <param name="replacements">When supplied, the unconditional
    /// enters-tapped restriction (CR 614.1c) is registered against it.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so a
    /// self-ETB event automatically queues the ability (CR 603.2).</param>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as AkoumRefugeFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a):
        //   "When this land enters, target creature gets +2/+0 until end of
        //    turn."
        //
        // Hand-rolled (not JSON) because the declarative schema has no
        // "pump target creature" verb. Single 1..1 "target creature" request
        // — "target creature" means ANY creature (no opponent restriction).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: target creature gets +{PumpPower}/+{PumpToughness} until end of turn",
            () =>
            {
                if (etb == null) return;
                if (etb.ChosenTargets.Count == 0 || etb.ChosenTargets[0].Count == 0) return;

                if (etb.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — both clauses apply only while the target is
                // still a creature on the battlefield; otherwise fizzle.
                if (target.Zone != ZoneType.Battlefield) return;
                if (target.ActiveEffects == null) return;

                // CR 613.1g layer 7c — +2/+0; CR 514.2 — until end of turn.
                target.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));
            });

        etb = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    // "target creature" — ANY creature on the battlefield
                    // (no controller restriction).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        land.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return land;
    }
}
