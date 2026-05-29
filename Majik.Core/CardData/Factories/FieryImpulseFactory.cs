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
/// Named-card factory for Fiery Impulse (Magic Origins, {R}).
///
/// Instant. Oracle text:
///   "Fiery Impulse deals 2 damage to target creature.
///    Spell mastery — If there are two or more instant and/or sorcery cards
///    in your graveyard, Fiery Impulse deals 3 damage instead."
///
/// ## Implementation
///
/// CR 702.137 — Spell mastery is a spell/ability quality whose condition is
/// checked as the spell resolves (CR 608.2): count the instant and/or
/// sorcery cards (CR 205.2a / CR 205.3i — card types) in the spell
/// controller's graveyard. If that count is ≥ 2 the higher damage value (3)
/// applies instead of the base (2).
///
/// Same shape as <see cref="UnholyHeatFactory"/> (a graveyard-state-gated
/// conditional-damage instant), except:
///   - the gate is spell mastery (≥ 2 instant/sorcery cards) instead of
///     delirium, and
///   - the target is "target creature" (CR 115.4 — the only legal target),
///     mirroring the Mode 0 gatherer + resolution-time legality re-check in
///     <see cref="AbradeFactory"/>.
///
/// Card-shape only here; the resolve-time spell definition (target + damage
/// effect with the spell-mastery gate) is built on-demand via
/// <see cref="BuildSpellDefinition(Player, Func{object, object})"/> because
/// <see cref="SpellDefinition"/> needs a target resolver supplied by the
/// caller's <see cref="GameContext"/>.
/// </summary>
[CardName("Fiery Impulse")]
public static class FieryImpulseFactory
{
    public const string CardName = "Fiery Impulse";
    public const string PrintedManaCost = "{R}";

    public const int BaseDamage = 2;
    public const int SpellMasteryDamage = 3;

    /// <summary>CR 702.137a — spell mastery threshold: two or more instant
    /// and/or sorcery cards in your graveyard.</summary>
    public const int SpellMasteryThreshold = 2;

    /// <summary>CardDef DSL — card shape only. Spell-mastery-gated damage
    /// body is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Fiery Impulse is
    /// cast. Single 1..1 "target creature" request; on resolution the
    /// controller's graveyard is sampled and the damage amount picked based
    /// on whether spell mastery is satisfied (CR 702.137).
    /// </summary>
    /// <param name="controller">Spell controller — the graveyard whose
    /// instant/sorcery count drives spell mastery.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
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
                // CR 115.4 — "target creature": every creature on every
                // battlefield (CR 301). Bot ranks opponent creatures highest
                // via the Removal intent.
                new TargetRequest(
                    "target creature",
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
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Fiery Impulse: spell-mastery-conditional damage", () =>
                    {
                        // CR 608.2b — resolution-time legality re-check: the
                        // target must still be a creature on the battlefield.
                        if (target is not Creature creature) return;
                        if (creature.Zone != ZoneType.Battlefield) return;

                        var amount = IsSpellMasteryActive(controller)
                            ? SpellMasteryDamage
                            : BaseDamage;
                        Fx.DealDamage(creature, amount);
                    }),
                };
            });
    }

    /// <summary>
    /// Sample the controller's graveyard for spell mastery (CR 702.137a):
    /// true iff there are <see cref="SpellMasteryThreshold"/>+ instant and/or
    /// sorcery cards (CR 205.2a) in <paramref name="controller"/>'s
    /// graveyard. Only the controller's own graveyard counts ("in your
    /// graveyard").
    /// </summary>
    public static bool IsSpellMasteryActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var count = 0;
        foreach (var card in controller.Zones.Graveyard.GetCards())
        {
            if (card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery))
                count++;
        }
        return count >= SpellMasteryThreshold;
    }
}
