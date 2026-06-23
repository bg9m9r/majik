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
/// Bespoke spell template for Grapple with the Past (Eldritch Moon, {1}{G}).
///
/// Oracle text (reminder text stripped by <see cref="OracleTextNormalizer"/>):
///   "Mill three cards, then you may return a creature or land card from your
///    graveyard to your hand."
///
/// This pattern — <i>self-mill, then optionally recur a typed card from the
/// graveyard</i> — is not covered by the generic reveal-and-choose
/// (<c>ImpulseMayRevealFilterTemplate</c>) or the targeted reanimation
/// (<c>ReanimateFromGraveyardTemplate</c>) templates: Grapple mills first (so
/// the milled cards are themselves eligible) and the return is a "you may"
/// resolution-time choice from the whole graveyard, not a "target … card"
/// cast-time target. It is therefore a dedicated bespoke template, mirroring
/// <see cref="MalevolentRumblePatternTemplate"/>.
///
/// The resolution body delegates to
/// <see cref="GrappleWithThePastFactory.MillThreeThenMayReturnAsync"/> — the
/// same core the factory's unit tests exercise — so the live cast and the
/// tests share one source of truth (CR 701.13 mill + CR 117.x "you may").
/// </summary>
public sealed class GrappleWithThePastPatternTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"mill\s+three\s+cards.*then\s+you\s+may\s+return\s+a\s+creature\s+or\s+land\s+card\s+from\s+your\s+graveyard\s+to\s+your\s+hand",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 100;
    public string Name => "GrappleWithThePastPattern";
    public BotIntent Intent => BotIntent.Reanimate;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        GrappleSpell(ctx.Caster);

    private static SpellDefinition GrappleSpell(Player caster) => new(
        Modes: Array.Empty<string>(),
        HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[]
        {
            new Effect(
                "Grapple with the Past: mill three, then may return a creature " +
                "or land card from your graveyard to your hand.",
                // The registered agent drives the "you may" decision via
                // ChooseFromPileAsync (no test selector at the live cast).
                ctx => GrappleWithThePastFactory.MillThreeThenMayReturnAsync(
                    caster, returnSelector: null, ctx)),
        });
}
