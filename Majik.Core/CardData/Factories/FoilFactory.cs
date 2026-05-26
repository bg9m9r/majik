using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Foil (Prophecy, {1}{U}{U}).
///
/// Instant. Oracle text:
///   "You may exile a blue card from your hand and discard a card rather
///    than pay this spell's mana cost.
///    Counter target spell."
///
/// ## Implemented (v1)
/// - Instant card shape ({1}{U}{U}, Blue) — built via the fluent
///   <see cref="CardDef"/> DSL.
/// - Pitch + discard alternative cost via
///   <see cref="Majik.Core.Costs.PitchAndDiscardAlternativeCost"/>
///   (<c>RequiredColor = Blue</c>) — exiles a blue card from hand AND
///   discards a card; no mana paid. Foil's printed pitch carries NO
///   "if it's not your turn" restriction (unlike the Force-of-Will
///   cycle), so this no-timing-gate primitive is correct (mirrors
///   Snapback / Pyrokinesis posture).
/// - Resolve effect (<see cref="BuildDefinition"/>): "counter target
///   spell" — single-target counter via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard move.
///   CR 701.5b — uncounterable spells survive the attempt.
///
/// ## Deferred (v1 gaps)
/// - <b>Bot probe</b>: not surfaced through
///   <see cref="PitchAltCostProbe.DefaultLookup"/> — that probe is keyed
///   by <see cref="Majik.Core.Costs.PitchAlternativeCost"/>'s not-your-turn
///   shape. A bot probe that enumerates (exile, discard) pairs is deferred
///   until the bot shows it cares about Foil at the EV layer (mirrors
///   Snapback / Pyrokinesis / Soul Spike).
/// </summary>
[CardName("Foil")]
public static class FoilFactory
{
    public const string CardName = "Foil";
    public const string PrintedManaCost = "{1}{U}{U}";

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell" SpellDefinition. Mirrors
    /// <c>CounterSpellFactory.CounterTargetSpell</c> — inlined here so the
    /// named-card factory is fully self-contained (same posture as
    /// Force of Negation / Force of Will).
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>(), BotIntent.Counter) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Foil — counter target spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;
                        // CR 701.5b — uncounterable spells survive the attempt.
                        if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
}
