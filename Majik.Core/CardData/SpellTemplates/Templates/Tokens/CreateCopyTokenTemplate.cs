using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

/// <summary>
/// "Create a token that's a copy of target &lt;kind&gt;." (Cackling Counterpart,
/// Self-Reflection, Spitting Image, Heat Shimmer, Flash Photography,
/// Irenicus's Vile Duplication's primary clause, etc.)
///
/// v1: derives a <see cref="TokenFactory.TokenSpec"/> from the chosen target's
/// printed name + power/toughness + subtypes + currently-known keyword
/// abilities, then spawns one token under the caster's control via
/// <see cref="TokenFactory.CreateOnBattlefield"/>. The "except it has
/// &lt;modifier&gt;" rider on cards like Heat Shimmer ("haste and exile at EOT")
/// or Irenicus ("flying and isn't legendary") is intentionally NOT parsed
/// here — token copies the printed characteristics only. The rider lives
/// for follow-up template work.
///
/// Populate variants ("create a token that's a copy of a creature token you
/// control") need agent-prompted self-token-pick and stay unmatched.
/// </summary>
public sealed class CreateCopyTokenTemplate : ISpellTemplate
{
    // Anchors on "create a token that's a copy of target <X>". Lossy when
    // the target kind isn't "creature" (Relm's Sketching → artifact/creature/
    // land copy) — runtime stub treats the chosen object as a Creature; if
    // the resolver hands back a non-Creature, the effect no-ops.
    private static readonly Regex Pattern = new(
        @"create\s+a\s+token\s+that'?s\s+a\s+copy\s+of\s+target\s+(?:[\w'-]+\s+)*(?:creature|permanent|artifact)\b",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "CreateCopyToken";
    public BotIntent Intent => BotIntent.Token;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var caster = ctx.Caster;
        var resolver = ctx.Resolver;
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var target = resolver(p.Targets[0][0]);
                return new IEffect[] { new Effect("create copy token", () =>
                {
                    if (target is not Creature src) return;
                    var keywords = src.Abilities
                        .OfType<Majik.Core.Abilities.KeywordAbility>()
                        .Select(k => k.Keyword)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    // CR 706.2 — copy effects snapshot the source's
                    // colour identity alongside its other copiable values.
                    var colours = CardColors.GetColors(src).ToList();
                    var spec = new TokenFactory.TokenSpec(
                        Name: src.Name,
                        Power: src.BasePower,
                        Toughness: src.BaseToughness,
                        Subtypes: src.Subtypes.ToArray(),
                        Keywords: keywords,
                        Colors: colours);
                    // Spawn under the spell's caster, not the source's owner —
                    // CR 707.2: a copy token's controller is the controller
                    // of the effect creating it.
                    TokenFactory.CreateOnBattlefield(spec, caster, zones: null);
                }) };
            });
    }
}
