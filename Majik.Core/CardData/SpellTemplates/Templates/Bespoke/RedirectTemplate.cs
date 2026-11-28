using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Services;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Redirect family — "Change the target of target spell with a single
/// target."
///
/// Cards bound: Deflection, Imp's Mischief, Shunt, Swerve.
///
/// ## Implemented (v1)
/// - Pattern match anchored at the start of the (normalized) oracle text.
/// - Single <see cref="TargetRequest"/> for the new target. The agent
///   picks any object; the redirector writes that back to the top
///   single-target spell's
///   <see cref="Majik.Core.Spells.Spell.ChosenTargets"/>.
/// - At resolution, calls <see cref="SpellRedirector.RedirectTopSpellSingleTarget"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"target spell with a single target" choice</b>: in MTG the caster
///   first picks WHICH spell to redirect, then picks the new target. v1
///   collapses to "top single-target spell" — see
///   <see cref="SpellRedirector"/> remarks. Most real play has at most
///   one spell on the stack when a redirect spell is cast, so the
///   collapse is usually invisible.
/// - <b>Imp's Mischief life-loss rider</b> ("You lose life equal to that
///   spell's mana value"): dropped at v1 — the redirector doesn't surface
///   the spell that was redirected, and the rider doesn't matter for the
///   stub-level effect (which doesn't actually change resolution).

/// CR 114.6 (changing the target of a spell or ability).
/// </summary>
public sealed class RedirectTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*change\s+the\s+target\s+of\s+target\s+spell\s+with\s+a\s+single\s+target\.?",
        RegexOptions.IgnoreCase);

    public int Priority => 90;
    public string Name => "Redirect";

    // Redirecting a hostile spell is part Protection (saving your own
    // permanent / yourself) and part Removal (forcing a removal spell to
    // hit a different target). BotIntent is a hint for the heuristic
    // picker — Protection|Removal lines up with the family's actual use.
    public BotIntent Intent => BotIntent.Protection | BotIntent.Removal;

    /// <summary>
    /// Requires <see cref="SpellBindContext.Stack"/> — the redirector
    /// can't function without a stack to scan.
    /// </summary>
    public bool CanBind(SpellBindContext ctx) => ctx.Stack is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var stack = ctx.Stack!;
        var resolver = ctx.Resolver;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            // CR 115.1 / 115.7 — the spell targets the SPELL it changes ("target
            // spell with a single target"); the new target is chosen as it
            // resolves.
            TargetRequests: new[]
            {
                new TargetRequest("target spell with a single target", 1, 1, Array.Empty<object>(),
                    BotIntent.Protection | BotIntent.Removal,
                    CandidateGatherer: g => g.Stack.GetAll()
                        .OfType<Majik.Core.Spells.Spell>()
                        .Where(s => s.ChosenTargets.Count == 1)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var picked = resolver(p.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("change the target of target spell with a single target", async rc =>
                    {
                        // CR 608.2b — the spell must still be on the stack with a single target.
                        if (picked is not Majik.Core.Spells.Spell spell
                            || !stack.GetAll().Contains(spell)
                            || spell.ChosenTargets.Count != 1) return;
                        if (rc.Agent is null || rc.Game is null) return;

                        // CR 115.7 — only to a legal target for that spell;
                        // with none, the target stays.
                        var pool = spell.CandidatesForTarget(0, rc.Game);
                        var legal = pool.Where(c => spell.IsLegalNewTarget(0, c, rc.Game)).ToList();
                        if (legal.Count == 0) return;
                        var request = new TargetRequest(
                            $"new target for {spell.Card.Name}", 1, 1, legal,
                            BotIntent.Protection | BotIntent.Removal);
                        var chosen = await rc.Agent.ChooseTargetsAsync(rc.Game, request, rc.Ct).ConfigureAwait(false);
                        if (chosen.Count > 0) spell.TryRedirectTarget(0, chosen[0], rc.Game);
                    }),
                };
            });
    }
}