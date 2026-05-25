using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bone Splinters (Worldwake, {B}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, sacrifice a creature.
///    Destroy target creature."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}.
/// - Additional cost (CR 601.2f): <see cref="SacrificeACreatureAdditionalCost"/>
///   declared on the <see cref="SpellDefinition"/>. <see cref="SpellCastFlow"/>
///   refuses the cast when the caster controls no creature (CR 601.2g —
///   additional cost that can't be paid → cast is illegal). Same posture
///   as <see cref="EldritchEvolutionFactory"/>.
/// - <b>Destroy target creature</b> — <see cref="BuildSpellDefinition"/>
///   declares a single 1..1 "target creature" request with
///   <see cref="BotIntent.Removal"/>. On resolution the targeted creature
///   is destroyed via <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) iff it is still a creature on the battlefield (CR 608.2b
///   — illegal target → no-op).
///
/// Indestructible (CR 702.12) cancels the destroy and active regeneration
/// shields (CR 701.15) are consumed via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="ZoneMoveReason.Destroy"/>. Note Bone Splinters does NOT
/// print "can't be regenerated" (unlike Terminate), so the regen shield
/// IS honoured.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice target prompt</b>. <see cref="SacrificeACreatureAdditionalCost"/>
///   picks the first creature on the caster's battlefield deterministically
///   (same v1 behaviour as Fling / Thud / Eldritch Evolution). Full agent-
///   driven sacrifice-target prompting requires the ITarget / TargetResolver
///   pipeline.
/// - <b>Self-sacrifice loophole</b>: the engine doesn't currently prevent
///   the caster from picking the same creature as both the sacrificed
///   cost and the targeted destroy. The rules disallow it (CR 117.2 —
///   targets locked at announcement before costs are paid; the to-be-
///   sacrificed creature still IS a creature on the battlefield at
///   targeting time, but ceases to be on resolution, so the destroy fizzles
///   on its own legality check). Defensive resolve-time guard handles this
///   correctly without explicit ordering knowledge.
/// </summary>
[CardName("Bone Splinters")]
public static class BoneSplintersFactory
{
    public const string CardName = "Bone Splinters";
    public const string PrintedManaCost = "{B}";

    /// <summary>
    /// Build a Bone Splinters sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time target request + destroy effect
    /// is built on demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Bone Splinters is
    /// cast. Declares the printed sacrifice-a-creature additional cost
    /// (CR 601.2f) alongside a single 1..1 "target creature"
    /// <see cref="TargetRequest"/>; on resolution the targeted creature is
    /// destroyed (CR 701.7) iff it is still a creature on the battlefield
    /// at resolution (CR 608.2b).
    /// </summary>
    /// <param name="resolver">Resolves the raw target token to a live
    /// engine object (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature",
                        () =>
                        {
                            if (raw is not Creature target) return;

                            // CR 608.2b — resolution-time legality check.
                            // Target must still be a creature on the
                            // battlefield. If the targeted creature was
                            // the one the caster sacrificed for the
                            // additional cost, it is now in the graveyard
                            // and this guard makes the destroy a no-op.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 701.7 — destroy. Indestructible
                            // (CR 702.12) cancels; active regeneration
                            // shield (CR 701.15) IS consumed (printed
                            // text does not say "can't be regenerated").
                            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                        }),
                };
            },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}
