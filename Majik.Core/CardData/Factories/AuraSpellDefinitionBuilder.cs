using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Helper that builds a <see cref="SpellDefinition"/> for an Aura's
/// cast-time targeting + auto-attach-on-resolution flow (CR 303.4f / 601.2c).
///
/// ## Why
///
/// Auras differ from typical permanent spells: at cast they MUST declare
/// a single legal target ("Enchant X"), and on resolution they enter the
/// battlefield ALREADY attached to that target (CR 303.4f) rather than as
/// a free-floating permanent. The generic <see cref="SpellCastFlow"/>
/// already prompts for targets via <see cref="TargetRequest"/>, and the
/// generic <see cref="Majik.Core.Services.StackResolver"/> moves a
/// permanent spell to the battlefield post-resolve. The remaining wiring
/// is the attach-on-resolve effect, which this builder produces.
///
/// ## What it does
///
/// Given an Aura permanent and a legality predicate for the target type
/// ("target land", "target creature", etc.), returns a
/// <see cref="SpellDefinition"/> with:
///   - A single <see cref="TargetRequest"/> (cardinality 1) whose
///     <see cref="TargetRequest.LegalCandidates"/> are the supplied list
///     of legal candidates (typically "all permanents matching the
///     predicate that are currently on the battlefield").
///   - An <see cref="IEffect"/> that calls
///     <see cref="Permanent.AttachTo"/> on the chosen target BEFORE the
///     <see cref="Majik.Core.Services.StackResolver"/> moves the aura to
///     the battlefield. Order matters: the
///     <see cref="Majik.Core.Effects.AttachedAuraRetypeStaticEffect"/>
///     subscribes to <see cref="Majik.Core.Events.CardMovedEvent"/> and
///     reads <see cref="Permanent.AttachedTo"/> at that moment to scope
///     its Layer 4 effect — so the attach must be set first.
/// </summary>
public static class AuraSpellDefinitionBuilder
{
    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for an Aura.
    /// </summary>
    /// <param name="aura">The Aura permanent being cast.</param>
    /// <param name="targetDescription">Human-readable description for
    /// the target request (e.g. "target land").</param>
    /// <param name="legalCandidates">Candidates (Permanent instances) the
    /// agent will choose from. Typically all current-battlefield
    /// permanents that match the aura's enchanted-noun predicate.</param>
    /// <param name="intent">Optional bot-intent label.</param>
    public static SpellDefinition ForAura(
        Permanent aura,
        string targetDescription,
        IReadOnlyList<object> legalCandidates,
        BotIntent intent = BotIntent.None)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(targetDescription);
        ArgumentNullException.ThrowIfNull(legalCandidates);

        var request = new TargetRequest(
            Description: targetDescription,
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: legalCandidates,
            Intent: intent);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { request },
            EffectFactory: chosen =>
            {
                // CR 303.4f — on resolution, the Aura enters the
                // battlefield attached to the chosen target. We do the
                // attach as a spell effect (executed inside
                // Spell.Resolve()) BEFORE StackResolver.HandleSpellResolution
                // moves the card to the battlefield zone. Then when the
                // CardMovedEvent fires for the aura entering the
                // battlefield, AttachedAuraRetypeStaticEffect sees
                // AttachedTo populated and correctly scopes the layer
                // effect.
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    // No legal target — nothing to do; CR 608.2b will
                    // counter the spell. We still return an empty effect
                    // list so resolution doesn't crash.
                    return Array.Empty<IEffect>();
                }

                var raw = chosen.Targets[0][0];
                if (raw is not Permanent target)
                {
                    return Array.Empty<IEffect>();
                }

                return new IEffect[]
                {
                    new Effect(
                        $"{aura.Name} — attach to chosen target",
                        () => aura.AttachTo(target)),
                };
            });
    }

    /// <summary>
    /// Convenience overload: filter <paramref name="battlefield"/> by
    /// <paramref name="predicate"/> to produce the candidate list, then
    /// build the Aura spell definition.
    /// </summary>
    public static SpellDefinition ForAura(
        Permanent aura,
        string targetDescription,
        IEnumerable<Permanent> battlefield,
        Func<Permanent, bool> predicate,
        BotIntent intent = BotIntent.None)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(predicate);

        var candidates = battlefield
            .Where(predicate)
            .Cast<object>()
            .ToList();
        return ForAura(aura, targetDescription, candidates, intent);
    }

    /// <summary>
    /// Oracle-text overload: parse the "Enchant X" clause out of
    /// <paramref name="oracleText"/> via <see cref="AuraEnchantClauseParser"/>
    /// and use that as the legality predicate.
    ///
    /// CR 702.5b — the printed "Enchant X" line is the canonical source for
    /// an Aura's target type. Use this overload when a card factory just
    /// wants to defer to the printed oracle rather than re-encoding the
    /// predicate.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the parser
    /// can't recognise an "Enchant X" clause in <paramref name="oracleText"/>.
    /// Callers with bespoke clauses ("Enchant nonbasic land", etc.) should
    /// use the explicit-predicate overload above.</exception>
    public static SpellDefinition ForAuraFromOracle(
        Permanent aura,
        string oracleText,
        IEnumerable<Permanent> battlefield,
        BotIntent intent = BotIntent.None)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(oracleText);
        ArgumentNullException.ThrowIfNull(battlefield);

        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(oracleText)
            ?? throw new InvalidOperationException(
                $"AuraEnchantClauseParser could not derive a target " +
                $"predicate from oracle text: \"{oracleText}\". Pass an " +
                $"explicit predicate via the other ForAura overload.");

        // Derive the human-readable description from the matched noun so
        // bots/UIs see "target land" rather than "target permanent".
        var description = DescribeFromOracle(oracleText);

        return ForAura(aura, description, battlefield, predicate, intent);
    }

    private static string DescribeFromOracle(string oracleText)
    {
        // Lightweight echo of the parser's regex — surfaces the noun for
        // a friendly target description. Falls back to "target permanent"
        // if the noun can't be re-derived (shouldn't happen because the
        // caller already validated the clause).
        var match = System.Text.RegularExpressions.Regex.Match(
            oracleText,
            @"(?im)^\s*[*_]?\s*Enchant\s+(?<noun>[a-z]+)\s*[*_]?\s*(?:[.\r\n]|$)");
        if (!match.Success) return "target permanent";
        var noun = match.Groups["noun"].Value.ToLowerInvariant();
        if (noun.EndsWith('s') && noun.Length > 1) noun = noun[..^1];
        return $"target {noun}";
    }
}
