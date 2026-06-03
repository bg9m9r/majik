using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Hagra Mauling // Hagra Broodpit (Zendikar Rising, {2}{B}{B}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "This spell costs {1} less to cast if an opponent controls no basic
///    lands.
///    Destroy target creature."
///
/// Back face — <see cref="HagraBroodpitFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {B}.").
///
/// ## Opponent-board-aware cost reduction (CR 117.7 — headline)
///
/// "Costs {1} less if an opponent controls no basic lands" is a printed cost
/// reduction whose discount depends on the OPPONENT's battlefield, not the
/// caster's. The caster-only <see cref="CostReductionAbility.TotalReducer"/>
/// seam (Domain) cannot satisfy this — it sees only the caster. The discount
/// is modelled with the board-aware
/// <see cref="OpponentBoardCostReductionAbility"/> primitive: at cast time the
/// closure receives a <see cref="ReducerContext"/> (caster + full player
/// roster) and returns {1} when NO opponent controls a basic land, else 0.
///
/// <para>"Basic land" = a land with the Basic supertype (CR 205.4a / 305.6).
/// The condition is "an opponent controls no basic lands" — i.e. the {1}
/// discount applies iff <i>every</i> opponent's battlefield is free of basic
/// lands (when there is exactly one opponent — the Modern norm — this is just
/// "the opponent controls no basic lands"). Coloured pips ({B}{B}) are
/// untouched (CR 117.7c); floor-at-zero is enforced by
/// <see cref="CostReduction.GetEffectiveCost(ICard, Player,
/// System.Collections.Generic.IEnumerable{Player})"/>. When the cost-calc
/// caller threads no roster (shape tests / affordability probes), the context
/// degrades to caster-only and the discount does not apply (it cannot prove
/// the opponent has no basics), so the spell reads at its full {2}{B}{B}.</para>
///
/// ## MDFC infra (CR 711 / 712) — cast-either-face
///
/// The front-face Instant carries an <see cref="MdfcState"/> with a castable
/// <see cref="MdfcFace"/> back-face descriptor (the LAND Hagra Broodpit).
/// Mirrors <see cref="SinkIntoStuporFactory"/> / <see cref="ShatterskullSmashingFactory"/>
/// (instant/sorcery front + land back). At cast time the controller chooses a
/// face; choosing the back face materializes a fresh Hagra Broodpit land
/// instance (wired to the live <see cref="ReplacementBus"/> so its
/// unconditional "enters tapped" ETB fires). No transform happens.
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {2}{B}{B}, owner / controller.
/// - Opponent-board-aware {1} cost reduction (above).
/// - <b>Destroy target creature</b> — <see cref="BuildDefinition"/> builds a
///   <see cref="SpellDefinition"/> with a single 1..1 "target creature"
///   request. On resolution the chosen creature is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) iff it is still a
///   Creature on the battlefield (CR 608.2b). Indestructible (CR 702.12) /
///   regeneration (CR 701.15) honoured at the destroy gate. No colour filter —
///   unconditional removal, same shape as <see cref="MurderFactory"/>.
/// - <see cref="MdfcState"/> with a castable Hagra Broodpit back face.
///
/// ## References
///
/// - <see cref="MurderFactory"/> — "Destroy target creature" SpellDefinition
///   this factory's resolve half directly mirrors.
/// - <see cref="OpponentBoardCostReductionAbility"/> — the opponent-board-aware
///   cost-reduction primitive paid down for this card.
/// - <see cref="SinkIntoStuporFactory"/> — instant front + land back MDFC
///   architecture this factory mirrors.
/// </summary>
[CardName("Hagra Mauling")]
public static class HagraMaulingFactory
{
    public const string CardName = "Hagra Mauling";
    public const string BackName = "Hagra Broodpit";
    public const string PrintedManaCost = "{2}{B}{B}";

    /// <summary>
    /// Construct the front face of Hagra Mauling as an Instant with owner /
    /// controller wired, the opponent-board-aware cost reducer attached, and
    /// the <see cref="MdfcState"/> face tracker (with castable Hagra Broodpit
    /// land back face). The resolve-time <see cref="SpellDefinition"/> is built
    /// on demand via <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "This spell costs {1} less to cast if an opponent
        // controls no basic lands." Board-aware reducer: {1} when no opponent
        // controls a basic land (a Land with the Basic supertype — CR 205.4a /
        // 305.6), else 0. Floor-at-zero / coloured-pip preservation enforced
        // by CostReduction.GetEffectiveCost.
        card.AddAbility(new OpponentBoardCostReductionAbility(
            ctx =>
            {
                var opponents = ctx.Opponents.ToList();

                // No roster threaded (shape tests / affordability probes
                // collapse the context to caster-only): we can't observe an
                // opponent's board, so don't claim a discount we can't verify.
                // The real cast flow always threads the full roster, so this
                // guard only affects probes — the spell then reads at full
                // {2}{B}{B}, never over-discounting.
                if (opponents.Count == 0) return 0;

                var anyOpponentHasABasic = opponents
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Any(c => c is Land land && land.HasSupertype(CardSupertype.Basic));
                return anyOpponentHasABasic ? 0 : 1;
            },
            description: "costs {1} less to cast if an opponent controls no basic lands"));

        // CR 711 / 712 — attach the MDFC face tracker WITH a castable back-face
        // descriptor (the LAND Hagra Broodpit). MdfcCastFlow offers the
        // controller a face choice at cast time and materializes a fresh
        // back-face land instance when chosen, wired to the live ReplacementBus
        // so its unconditional "enters tapped" ETB fires. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                HagraBroodpitFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "destroy target creature"
    /// <see cref="SpellDefinition"/>. Single 1..1 "target creature" request;
    /// on resolve the target is destroyed via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) iff it is still a
    /// Creature on the battlefield (CR 608.2b). No colour filter — same as
    /// <see cref="MurderFactory"/>.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) honoured via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
