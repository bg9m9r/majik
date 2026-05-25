using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Disrupting Shoal (Betrayers of Kamigawa,
/// {X}{U}{U}).
///
/// Instant. Oracle text:
///   "You may exile a blue card with mana value X from your hand rather
///    than pay this spell's mana cost.
///    Counter target spell if its mana value is X."
///
/// ## Implemented (v1)
/// - Instant shape, printed cost <c>{X}{U}{U}</c>, blue. The DSL builds the
///   card; <see cref="SpellDefinition.HasVariableX"/> is true so the engine
///   prompts for X on the regular cast path (or reads X from the pitch alt
///   cost — see below).
/// - 1..1 "target spell" <see cref="TargetRequest"/>. On resolution the
///   engine checks the target's printed mana value against the cast-time X;
///   if equal, the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + zone-move to graveyard
///   (CR 701.5; CR 701.5b — uncounterable spells survive). If not equal,
///   the effect does nothing (CR 608.2b mv mismatch posture).
/// - <b>Shoal-cycle pitch alt cost</b> — <see cref="PitchAlternativeCost"/>
///   with <c>requiredManaValue</c> wired to the desired X. The pitched card
///   must be blue and have mana value exactly X (CR 118.9 + CR 107.3b).
///   When this alt cost is supplied at cast time,
///   <see cref="SpellCastFlow"/> consults
///   <see cref="IAlternativeCost.OverrideX"/> and uses it as X without
///   re-prompting the agent and without adding X-generic to the total mana
///   cost (the pitch IS the entire cost). On the regular cast path (no alt
///   cost) the agent picks X and pays {X}{U}{U} normally.
/// - Bot probe — <see cref="PitchAltCostProbe.DefaultLookup"/> recognizes
///   "Disrupting Shoal" and surfaces blue hand-card candidates (the bot
///   filters down to mv-matched candidates when bidding).
///
/// ## Deferred (v1 gaps)
/// - <b>Target mv computed off the printed cost</b>. <see cref="ICard.ManaCostValue"/>
///   reflects the printed cost; spells on the stack with no
///   cost-modifying static effects show the same mv they print. Floating-X
///   targets (e.g. an X spell on the stack) return their cast-time X via
///   <see cref="Card.PendingCastX"/>, but we don't read that here — Shoal's
///   "mv is X" check is against the printed mv only in v1 (consistent with
///   how oracle wording references "mana value" for stack objects per
///   CR 202.3c). Refine when X-cost target inspection is needed.
/// - <b>Bot mv-aware bidding</b>. The probe surfaces all blue pitch
///   candidates without filtering by Shoal's X; the alt cost
///   <see cref="PitchAlternativeCost.CanCastFor"/> mv-gate rejects
///   mismatched picks at cast time. Tighten the probe later when bot
///   evaluation grows mv-aware filtering.
/// </summary>
[CardName("Disrupting Shoal")]
public static class DisruptingShoalFactory
{
    public const string CardName = "Disrupting Shoal";
    public const string PrintedManaCost = "{X}{U}{U}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour is built
    /// via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Convenience constructor for the Shoal-cycle pitch alt cost. The
    /// caller supplies the chosen blue pitch card; <paramref name="x"/>
    /// becomes both the required mana value of the pitched card AND the
    /// spell's X at resolution.
    /// </summary>
    public static PitchAlternativeCost BuildPitchAltCost(ICard pitchCard, int x)
    {
        ArgumentNullException.ThrowIfNull(pitchCard);
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        return new PitchAlternativeCost(
            ManaColor.Blue,
            pitchCard,
            lifeCost: 0,
            requiredManaValue: x);
    }

    /// <summary>
    /// Build the "counter target spell if its mana value is X"
    /// SpellDefinition. <see cref="SpellDefinition.HasVariableX"/> is true so
    /// the engine prompts (or the alt cost supplies X). At resolution the
    /// effect reads <c>ChosenSpellParams.X</c> and compares to the target
    /// spell's <see cref="ICard.ManaCostValue"/>.<see cref="ManaCost.TotalValue"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by
    /// the caster to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                var x = p.X ?? 0;
                return new IEffect[]
                {
                    new Effect(
                        $"Disrupting Shoal — counter target spell if its mana value is {x}",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 202.3 — mana value is computed from the
                            // printed cost. Compare against X chosen at cast.
                            if (ManaCost.Parse(spell.Card.ManaCost).TotalValue != x) return;

                            // CR 701.5 / 701.5b — counter, respecting
                            // uncounterability.
                            if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }),
                };
            });
    }
}
