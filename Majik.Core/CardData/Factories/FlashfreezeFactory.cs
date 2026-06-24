using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flashfreeze (Coldsnap / various, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Counter target red or green spell."
///
/// Flashfreeze is the colour-filtered sibling of <see cref="NegateFactory"/>
/// / <see cref="EssenceScatterFactory"/>: instead of a type gate (noncreature
/// / creature) the legal target pool is restricted to spells whose colour
/// identity includes red or green (CR 105.1 colours; CR 700-style "red or
/// green spell" characteristic predicate). Same removal mechanics as the rest
/// of the counter family — <see cref="OracleSpellBinder.RemoveFromStack"/>
/// (CR 701.5, honouring uncounterable spells per CR 701.5b) + a graveyard
/// zone-move for the card.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{U} (blue). The base shape
///   (name / Instant type / cost) is materialised from the embedded JSON
///   definition (<c>flashfreeze.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="CounterfluxFactory"/>. Resolve behaviour is built on demand
///   via <see cref="BuildSpellDefinition"/> because the JSON
///   <c>SpellDefinition</c> schema does not yet express a colour-filtered
///   counter request.
/// - <b>Counter target red or green spell</b> —
///   <see cref="BuildSpellDefinition"/> returns a <see cref="SpellDefinition"/>
///   with one 1..1 "target red or green spell" <see cref="TargetRequest"/>.
///   The candidate gatherer walks the stack and yields spells whose colours
///   include Red or Green (CR 105.1). On resolution the colour predicate is
///   re-checked (CR 608.2b — illegal target if it is no longer red or green;
///   the effect does nothing) and the target is removed from the stack via
///   <see cref="OracleSpellBinder.RemoveFromStack"/>, its card moving to the
///   graveyard (CR 701.5).
///
/// ## CR notes
/// - CR 105.1 — the five colours; a spell is "red or green" when its colours
///   include red and/or green.
/// - CR 701.5 / 701.5b — counter a spell; an uncounterable spell can't be
///   countered (RemoveFromStack returns false → no graveyard move).
/// - CR 608.2b — resolution-time legality re-check; an illegal target means
///   the effect does nothing and the spell stays on the stack.
/// </summary>
[CardName("Flashfreeze")]
public static class FlashfreezeFactory
{
    public const string CardName = "Flashfreeze";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "flashfreeze";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {1}{U}) from the
    /// embedded JSON definition. Resolve behaviour (counter target red or
    /// green spell) is built on demand via <see cref="BuildSpellDefinition"/>,
    /// mirroring <see cref="CounterfluxFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "counter target red or green spell" <see cref="SpellDefinition"/>.
    /// CR 608.2b: if the chosen target is not a red or green spell at
    /// resolution time, the effect does nothing (illegal target — the spell
    /// remains on the stack).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// spells directly.</param>
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
                new TargetRequest(
                    Description: "target red or green spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // Agent-prompt: walk the stack, yield spells whose colours
                    // include Red or Green (CR 105.1).
                    CandidateGatherer: ctx => ctx.Stack.GetAll()
                        .OfType<ISpell>()
                        .Where(IsRedOrGreen)
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
                        $"{CardName}: counter target red or green spell.",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 608.2b — only red or green spells are legal
                            // targets; re-checked at resolution.
                            if (!IsRedOrGreen(spell)) return;

                            // CR 701.5 / 701.5b — RemoveFromStack returns false
                            // for an uncounterable target; only graveyard the
                            // card when it was actually removed.
                            if (OracleSpellBinder.RemoveFromStack(stack, spell))
                            {
                                spell.Card.SetZone(ZoneType.Graveyard);
                            }
                        }),
                };
            });
    }

    /// <summary>
    /// CR 105.1 — a spell is "red or green" when its colours include red
    /// and/or green.
    /// </summary>
    private static bool IsRedOrGreen(ISpell spell)
    {
        var colors = CardColors.GetColors(spell.Card);
        return colors.Contains(ManaColor.Red) || colors.Contains(ManaColor.Green);
    }
}
