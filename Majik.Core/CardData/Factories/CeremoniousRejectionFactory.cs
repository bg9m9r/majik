using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ceremonious Rejection (Aether Revolt, {U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-01):
///   "Counter target colorless spell."
///
/// Ceremonious Rejection is the colorless-spell-filter sibling of
/// <see cref="DispelFactory"/> ("counter target instant spell") and
/// <see cref="NegateFactory"/> ("counter target noncreature spell"): the same
/// single-target resolve-time counter shape, with the type filter swapped for
/// a colorless filter. Note the card itself is <i>blue</i> ({U}) — only its
/// <b>target</b> must be colorless.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {U}, blue. The base shape
///   (name / Instant type / {U} cost) is materialised from the embedded JSON
///   definition (<c>ceremonious-rejection.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (mirrors
///   <see cref="EchoingTruthFactory"/> — the JSON <c>SpellDefinition</c>
///   schema does not yet express a "target colorless spell" request, so the
///   counter behaviour is layered on here via <see cref="BuildSpellDefinition"/>).
/// - Resolve-time <see cref="SpellDefinition"/> declares one 1..1 "target
///   colorless spell" request. On resolution the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard zone-move
///   (CR 701.5).
/// - Colorless gate: same defensive posture as <see cref="DispelFactory"/>,
///   but restricted to colorless spells. CR 105.2c — an object is colorless
///   when it has no color; the check is
///   <c>CardColors.GetColors(spell.Card).Count == 0</c> (the canonical
///   colorless predicate used by Ugin / Ancient Stirrings / World Breaker).
///   If the chosen target is NOT colorless at resolution (CR 608.2b) the
///   effect does nothing and the spell remains on the stack.
/// </summary>
[CardName("Ceremonious Rejection")]
public static class CeremoniousRejectionFactory
{
    public const string CardName = "Ceremonious Rejection";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "ceremonious-rejection";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {U}) from the
    /// embedded JSON definition. Resolve behaviour (counter target colorless
    /// spell) is built on demand via <see cref="BuildSpellDefinition"/>,
    /// mirroring <see cref="EchoingTruthFactory"/>.
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
    /// Build the "counter target colorless spell" SpellDefinition. CR 608.2b:
    /// if the chosen target is not a colorless spell at resolution time, the
    /// effect does nothing (illegal target — the spell remains on the stack).
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live
    /// engine object (chosen → live stack object).</param>
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
                new TargetRequest("target colorless spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Ceremonious Rejection — counter target colorless spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 105.2c / CR 608.2b — only colorless spells are
                        // legal targets. A spell is colorless when its card
                        // has no color (no colored pips, no color indicator).
                        if (CardColors.GetColors(spell.Card).Count != 0) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
