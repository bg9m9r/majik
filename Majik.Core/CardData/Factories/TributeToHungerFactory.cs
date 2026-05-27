using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tribute to Hunger (Innistrad, {2}{B}).
///
/// Instant. Oracle text:
///   "Target opponent sacrifices a creature of their choice.
///    You gain life equal to that creature's toughness."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{B}, owner / controller.
/// - <b>Edict variant targeting an opponent</b> (CR 701.16 — sacrifice).
///   <see cref="BuildSpellDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target opponent" <see cref="TargetRequest"/>.
///   On resolution:
///   1. The opponent's toughness of the first creature they control is
///      captured BEFORE sacrifice (the creature ceases to exist as a
///      battlefield permanent after the zone move).
///   2. The creature is sacrificed via <see cref="Fx.Sacrifice(ICard)"/>
///      (CR 701.16 — sacrifice bypasses Indestructible / regeneration).
///   3. The spell controller gains life equal to the captured toughness
///      (CR 119.4 — life gain is unconditional once a creature was sacrificed).
/// - If the opponent controls no creatures, no sacrifice occurs and no life
///   is gained (no-op per CR 608.2b analogy — illegal resolution context).
///
/// ## Deferred (v1 gaps)
/// - <b>Player choice</b>: v1 deterministically picks the first creature in
///   the opponent's battlefield order. A full implementation would prompt the
///   targeted opponent to choose which creature to sacrifice.
/// </summary>
[CardName("Tribute to Hunger")]
public static class TributeToHungerFactory
{
    public const string CardName = "Tribute to Hunger";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (edict + lifegain) is built on demand via
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Tribute to Hunger is
    /// cast. Single 1..1 "target opponent" request; on resolution the opponent
    /// sacrifices a creature and the controller gains life equal to that
    /// creature's toughness (CR 701.16 + CR 119.4).
    /// </summary>
    /// <param name="controller">Spell controller — gains life on resolution.</param>
    /// <param name="resolver">Resolves the raw target token to a live engine
    /// object (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target opponent",
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
                        $"{CardName}: opponent sacrifices a creature; you gain life equal to its toughness",
                        () =>
                        {
                            if (raw is not Player opponent) return;

                            // Pick deterministically — v1 auto-selects the first
                            // creature the opponent controls (player "choice" deferred).
                            var pick = opponent.Zones.Battlefield.GetCards()
                                .OfType<Creature>()
                                .FirstOrDefault();

                            if (pick == null) return; // no creatures → no-op

                            // Capture toughness BEFORE sacrifice — the permanent
                            // will no longer be accessible as a battlefield object
                            // after the zone move (CR 701.16).
                            var toughness = pick.Toughness;

                            // CR 701.16 — sacrifice: bypasses Indestructible /
                            // regeneration (unlike "destroy").
                            Fx.Sacrifice(pick);

                            // CR 119.4 — controller gains life equal to the
                            // sacrificed creature's toughness.
                            Fx.GainLife(controller, toughness);
                        }),
                };
            });
    }
}
