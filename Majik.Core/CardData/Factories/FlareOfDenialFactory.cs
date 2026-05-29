using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flare of Denial (Modern Horizons 3, {1}{U}{U}).
///
/// Instant. Oracle text:
///   "You may sacrifice a nontoken blue creature rather than pay this spell's
///    mana cost. Counter target spell."
///
/// ## Implemented (v1)
///
/// - Instant shape, mana cost {1}{U}{U}, blue identity (MV 3).
/// - NamedCardFactory dispatch via <c>[CardName]</c> source-generator.
/// - Alternative cost (<see cref="SacrificeNontokenBlueCreatureAlternativeCost"/>):
///   the caster may sacrifice a nontoken blue creature they control on the
///   battlefield instead of paying {1}{U}{U}. No timing restriction applies
///   (unlike Force-of-Will's not-your-turn gate — CR 118.9 applies, but
///   Flare of Denial has no printed timing clause).
/// - Resolve: counter target spell (any spell, CR 701.5 — same logic as
///   <see cref="CounterspellFactory"/>).
///
/// Bot probe: <see cref="FlareOfDenialAltCostProbe"/> surfaces sacrifice
/// candidates for the heuristic bot agent; it must be registered in
/// <see cref="Majik.Core.Players.Agents.AlternativeCostProbeRegistry.CreateDefault"/>
/// for the bot to use the alt cost automatically.
///
/// CR citations:
///   CR 118.9  — alternative cost
///   CR 701.18 — sacrifice
///   CR 701.5  — counter a spell
/// </summary>
[CardName("Flare of Denial")]
public static class FlareOfDenialFactory
{
    public const string CardName = "Flare of Denial";
    public const string PrintedManaCost = "{1}{U}{U}";

    /// <summary>CardDef DSL — card shape only. The counter SpellDefinition
    /// is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell" SpellDefinition for Flare of Denial.
    /// Identical in effect to <see cref="CounterspellFactory.BuildSpellDefinition"/>
    /// — any spell is a legal target; no filter at resolution time.
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Flare of Denial — counter target spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}

/// <summary>
/// Bot probe: surfaces <see cref="SacrificeNontokenBlueCreatureAlternativeCost"/>
/// candidates for Flare of Denial during the heuristic bot's spell-cast
/// enumeration.
///
/// For each nontoken blue creature the caster controls on the battlefield,
/// yields one <see cref="SacrificeNontokenBlueCreatureAlternativeCost"/>
/// instance (one per eligible sacrifice candidate). The bot picks the bid it
/// values least among the affordability-filtered set.
///
/// No timing restriction is emitted — unlike <see cref="PitchAltCostProbe"/>
/// this probe does NOT filter on active player. Flare of Denial's alt cost has
/// no printed timing gate.
/// </summary>
public sealed class FlareOfDenialAltCostProbe : Majik.Core.Players.Agents.IAlternativeCostProbe
{
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        if (card.Name != FlareOfDenialFactory.CardName) yield break;
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        foreach (var battlefield in caster.Zones.Battlefield.GetCards())
        {
            if (battlefield is not Permanent perm) continue;
            if (!perm.HasType(CardType.Creature)) continue;
            if (perm.IsToken) continue;
            if (!CardColors.GetColors(perm).Contains(ManaColor.Blue)) continue;
            yield return new SacrificeNontokenBlueCreatureAlternativeCost(perm);
        }
    }
}
