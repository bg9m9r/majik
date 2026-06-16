using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Psychotic Episode pattern: "Target player reveals their hand and the top
/// card of their library. You choose a card revealed this way. That player puts
/// the chosen card on the bottom of their library."
///
/// Distinct from the Duress family (<see cref="RevealHandThenDiscardTemplate"/>)
/// in three ways the deferral
/// <c>reveal-hand-opponent-bottoms-chosen-card</c> called out:
/// <list type="bullet">
///   <item>the reveal pile includes the TOP CARD OF THE LIBRARY, not just the
///   hand;</item>
///   <item>the CHOOSER is the spell's controller (the target player's
///   OPPONENT, CR 608.2g), resolved here through the controller's agent
///   (<see cref="AgentRegistry.Get"/> over <see cref="SpellBindContext.Caster"/>),
///   not the target player's;</item>
///   <item>the chosen card moves to the BOTTOM OF THE LIBRARY (CR 701.21),
///   not the graveyard.</item>
/// </list>
///
/// Resolution is delegated to <see cref="PsychoticEpisodeFactory.Resolve"/> so
/// the prod (template) path and the unit-test (factory) path share one body.
/// </summary>
public sealed class RevealHandTopBottomChosenTemplate : ISpellTemplate
{
    // The normalized oracle text keeps a trailing " Madness {1}{B}" keyword
    // line (the parenthesized reminder is stripped, the keyword is not), so the
    // pattern is NOT anchored at end-of-string.
    private static readonly Regex Pattern = new(
        @"^target\s+player\s+reveals\s+their\s+hand\s+and\s+the\s+top\s+card\s+of\s+their\s+library\.\s*you\s+choose\s+(?:a|an)\s+card\s+revealed\s+this\s+way\.\s*that\s+player\s+puts\s+the\s+chosen\s+card\s+on\s+the\s+bottom\s+of\s+their\s+library\.",
        RegexOptions.IgnoreCase);

    public int Priority => 95;
    public string Name => "RevealHandTopBottomChosen";
    public BotIntent Intent => BotIntent.Discard | BotIntent.HandHate;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success ? new Dictionary<string, string>() : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var resolver = ctx.Resolver;
        var caster = ctx.Caster;
        var eventBus = ctx.EventBus;
        return new SpellDefinition(
            Modes: Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("reveal-hand-top-bottom-chosen", () =>
                {
                    // CR 608.2g — "you" = the spell's CONTROLLER. The chooser is
                    // the controller's agent (the target player's opponent),
                    // resolved off AgentRegistry on the prod path.
                    var chooserAgent = AgentRegistry.Get(caster);
                    PsychoticEpisodeFactory.Resolve(target, chooserAgent, eventBus);
                }) };
            });
    }
}
