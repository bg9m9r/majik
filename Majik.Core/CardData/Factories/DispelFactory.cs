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
/// Named-card factory for Dispel (Rise of the Eldrazi, {U}).
///
/// Instant. Oracle text:
///   "Counter target instant spell."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, blue.
/// - 1..1 "target instant spell" <see cref="TargetRequest"/>. At resolution
///   the engine confirms the target is still an <see cref="ISpell"/> whose
///   card has <see cref="CardType.Instant"/>; if not, the effect does
///   nothing (CR 608.2b — illegal target at resolution).
/// - Counter via <see cref="OracleSpellBinder.RemoveFromStack"/> + zone-move
///   to graveyard (CR 701.5). Uncounterable spells short-circuit (CR 701.5b)
///   via <see cref="OracleSpellBinder.RemoveFromStack"/>'s false return.
///
/// Defensive resolve-time creature/instant gate mirrors
/// <see cref="NegateFactory"/> / <see cref="ForceOfNegationFactory"/> — the
/// <see cref="TargetRequest.LegalCandidates"/> list is left empty (agent
/// resolves freely) and the type filter applies on resolution.
/// </summary>
[CardName("Dispel")]
public static class DispelFactory
{
    public const string CardName = "Dispel";
    public const string PrintedManaCost = "{U}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour is built via
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target instant spell" SpellDefinition.
    /// CR 608.2b: if the chosen target is not an instant spell at resolution
    /// time, the effect does nothing (illegal target — the spell remains on
    /// the stack).
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by
    /// the caster to a live engine object (typically pass-through; production
    /// callers route via a TargetResolver service).</param>
    /// <param name="stack">Active stack; required to remove the countered
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
                new TargetRequest("target instant spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Dispel — counter target instant spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — if the target has become a non-instant
                        // spell by resolution time (e.g. type-changing effect
                        // — uncommon for spells on the stack but kept
                        // defensive), the effect does nothing for it.
                        if (!spell.Card.HasType(CardType.Instant)) return;

                        // CR 701.5b — RemoveFromStack returns false on
                        // uncounterable spells; in that case leave the spell
                        // intact (no zone move).
                        if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
