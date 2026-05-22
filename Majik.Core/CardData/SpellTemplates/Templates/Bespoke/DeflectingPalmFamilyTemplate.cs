using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Deflecting Palm / Honorable Passage / Intervention Pact / Reverse Damage
/// family — "The next time a source of your choice would deal damage to
/// {you|any target} this turn, prevent that damage." Optional rider:
///
///   - Deflecting Palm:   "If damage is prevented this way, ~ deals that
///                         much damage to that source's controller."
///   - Honorable Passage: "If damage from a red source is prevented this
///                         way, ~ deals that much damage to the source's
///                         controller."
///   - Intervention Pact: "You gain life equal to the damage prevented this way."
///   - Reverse Damage:    "You gain life equal to the damage prevented this way."
///
/// v1 stub:
///   - Beneficiary is always <see cref="SpellBindContext.Caster"/>. The
///     "you" texts match exactly. Honorable Passage's "any target" variant
///     binds but is lossy — the shield still only fires for damage aimed
///     at the caster (real "choose a target" plumbing would require an
///     extra target request + per-target shield).
///   - "Choose a source" is dropped — the shield fires on the FIRST
///     qualifying damage intent. See <see cref="PreventNextDamageFromChosenSourceShield"/>.
///   - Rider semantics:
///       * Deflecting Palm: <c>source's controller</c>.LoseLife(prevented).
///       * Intervention Pact / Reverse Damage: caster.GainLife(prevented).
///       * Honorable Passage's red-source-only rider is dropped (lossy) —
///         the shield still prevents, but the bounce-damage clause is
///         skipped because we can't ask the damage source for its color.
///
/// Requires <see cref="SpellBindContext.Replacements"/> — same gating
/// pattern as <see cref="Templates.Misc.FogTemplate"/>.
///
/// CR 615 (damage prevention), CR 614 (replacement effects).
/// </summary>
public sealed class DeflectingPalmFamilyTemplate : ISpellTemplate
{
    // Matches the lead clause for all four cards, capturing the "you" vs.
    // "any target" beneficiary and the optional rider text after the
    // "prevent that damage." sentence.
    //
    // Tolerates a small amount of whitespace / punctuation variation that
    // OracleTextNormalizer doesn't fully strip. The rider is captured
    // greedy-to-end-of-text so each Rehydrate can classify it.
    private static readonly Regex Pattern = new(
        @"^the\s+next\s+time\s+a\s+source\s+of\s+your\s+choice\s+would\s+deal\s+damage\s+to\s+(?<who>you|any\s+target)\s+this\s+turn,\s+prevent\s+that\s+damage\.?(?<rider>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex BounceToControllerRider = new(
        @"if\s+damage\s+is\s+prevented\s+this\s+way,\s+[^.]*?\s+deals?\s+that\s+much\s+damage\s+to\s+(?:that|the)\s+source'?s\s+controller",
        RegexOptions.IgnoreCase);

    private static readonly Regex RedBounceToControllerRider = new(
        @"if\s+damage\s+from\s+a\s+red\s+source\s+is\s+prevented\s+this\s+way,\s+[^.]*?\s+deals?\s+that\s+much\s+damage\s+to\s+(?:that|the)\s+source'?s\s+controller",
        RegexOptions.IgnoreCase);

    private static readonly Regex GainLifeRider = new(
        @"you\s+gain\s+life\s+equal\s+to\s+the\s+damage\s+prevented\s+this\s+way",
        RegexOptions.IgnoreCase);

    // Rider kinds — persisted as a single string in the param dict so the
    // compiled fast path round-trips them cleanly.
    private const string RiderNone = "none";
    private const string RiderBounce = "bounce";       // Deflecting Palm
    private const string RiderRedBounce = "red_bounce"; // Honorable Passage (lossy)
    private const string RiderGainLife = "gain_life";   // Intervention Pact, Reverse Damage

    public int Priority => 90;
    public string Name => "DeflectingPalmFamily";
    public BotIntent Intent => BotIntent.Protection;

    public bool CanBind(SpellBindContext ctx) => ctx.Replacements is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;

        var rider = m.Groups["rider"].Value ?? string.Empty;
        string riderKind;
        if (RedBounceToControllerRider.IsMatch(rider)) riderKind = RiderRedBounce;
        else if (BounceToControllerRider.IsMatch(rider)) riderKind = RiderBounce;
        else if (GainLifeRider.IsMatch(rider)) riderKind = RiderGainLife;
        else riderKind = RiderNone;

        return new Dictionary<string, string>
        {
            ["who"] = m.Groups["who"].Value.Equals("you", StringComparison.OrdinalIgnoreCase) ? "you" : "any",
            ["rider"] = riderKind,
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var bus = ctx.Replacements!;
        var caster = ctx.Caster;
        var riderKind = @params.TryGetValue("rider", out var r) ? r : RiderNone;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[] { new Effect("deflecting-palm-family", () =>
            {
                var onPrevent = BuildRider(riderKind, caster);
                bus.Register(new PreventNextDamageFromChosenSourceShield(caster, onPrevent));
            }) });
    }

    private static Action<int, DamageIntent>? BuildRider(string riderKind, Player caster) =>
        riderKind switch
        {
            RiderBounce => (amount, intent) =>
            {
                // CR 615 — the "bounce damage" rider is itself a damage
                // event from the spell to the source's controller. v1
                // shortcut: drop the controller's life directly. A future
                // pass should route this through ReplacementBus so it
                // composes with other prevention shields.
                var controller = ResolveSourceController(intent.Source);
                if (controller is not null && amount > 0) controller.LoseLife(amount);
            },
            RiderRedBounce => null, // lossy — needs source-color introspection
            RiderGainLife => (amount, _) =>
            {
                if (amount > 0) caster.GainLife(amount);
            },
            _ => null,
        };

    private static Player? ResolveSourceController(object source) => source switch
    {
        Player p => p,
        ICard card => card.Controller,
        _ => null,
    };
}
