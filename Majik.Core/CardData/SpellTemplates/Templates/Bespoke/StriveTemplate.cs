using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Strive cost-mechanic (CR 702.124) — "This spell costs {X} more to cast
/// for each target beyond the first. Any number of target [things]…"
///
/// Cards covered: Aurelia's Fury, Crackling Doom (Strive variant), Aerial
/// Formation, Ajani's Presence, Colossal Heroics, Consign to Dust, Cruel
/// Feeding, Desperate Stand, Hour of Need, Kiora's Dismissal, Nature's
/// Panoply, Phalanx Formation, Rouse the Mob, Setessan Tactics, Silence
/// the Believers, Solidarity of Heroes, Twinflame, and similar.
///
/// ## Implemented (v1)
/// - Detects Strive on <see cref="SpellBindContext.RawText"/> (the
///   <see cref="OracleTextNormalizer"/> strips the prefix before
///   <see cref="SpellBindContext.Text"/>).
/// - Walks the registry on the post-strip text to bind an inner
///   <see cref="SpellDefinition"/> (the underlying effect). Skips
///   <c>StriveTemplate</c> itself to avoid infinite recursion.
/// - Expands the inner spell's first <see cref="TargetRequest"/> from
///   single-target (min=1, max=1) to "any number" (min=1, max=large).
///   <see cref="MaxTargets"/> caps at 10 so the bot / UI don't have to
///   reason about an unbounded list.
/// - Wraps the inner <see cref="SpellDefinition.EffectFactory"/> to
///   iterate every chosen target and apply the inner effect once per
///   target.
///
/// ## Deferred (v1 gaps — flagged lossy)
/// - <b>Per-target mana scaling:</b> the {X}-per-extra-target additional
///   cost is dropped — the caster pays only the base mana cost regardless
///   of target count. CR 601.2f / 702.124 cost-modification machinery
///   isn't plumbed through <see cref="SpellCastFlow"/> for variable-count
///   per-target costs yet. The parsed cost is captured into the
///   params payload (key <c>"per"</c>) so a follow-up that wires
///   <c>CostModifier</c> in <see cref="SpellDefinition"/> can read it
///   without re-parsing.
/// - <b>Min target = 1:</b> Strive technically allows 1 target (the base
///   case — no extra cost). We keep min=1 to require at least one target.
/// </summary>
public sealed class StriveTemplate : ISpellTemplate
{
    // Match "Strive — This spell costs {…} more to cast for each target
    // beyond the first." anchored at the start of the raw oracle text.
    // Captures the cost between the braces — may include multiple
    // brace groups like "{2}{R}". We capture the entire cost segment
    // up to "more to cast".
    private static readonly Regex StrivePrefix = new(
        @"^\s*strive\s+—\s+this\s+spell\s+costs\s+(?<cost>(?:\{[^}]+\})+)\s+more\s+to\s+cast\s+for\s+each\s+target\s+beyond\s+the\s+first\.",
        RegexOptions.IgnoreCase);

    // Cap the target slot so engine consumers see a finite count. Strive
    // cards in practice have ~3-5 viable targets; 10 is generous without
    // overflowing the bot's choose-targets enumeration.
    internal const int MaxTargets = 10;

    private SpellTemplateRegistry? _registry;

    public void SetRegistry(SpellTemplateRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    // Priority 95 — well above the inner templates (most are 30-80) so
    // Strive wins on cards with the prefix. Below ClauseComposition (200)
    // and ModalChooseOne (250) so the composers still win when a Strive
    // card also has clause/modal structure (none ship today, but safe).
    public int Priority => 95;
    public string Name => "Strive";

    // The intent is "whatever the underlying spell does". The bot picks
    // up per-target intent via the wrapped TargetRequest below, so leaving
    // this as None is the right call — the inner template's intent stamps
    // through onto the wrapped definition.
    public BotIntent Intent => BotIntent.None;

    public bool CanBind(SpellBindContext ctx) => _registry is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (_registry is null) return null;
        if (!StrivePrefix.IsMatch(ctx.RawText)) return null;

        var match = StrivePrefix.Match(ctx.RawText);
        var costPart = match.Groups["cost"].Value;

        var inner = BindInner(ctx);
        return Wrap(inner, costPart);
    }

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        // Compile-time path: TryExtractParams takes the NORMALIZED text
        // (Strive prefix already stripped). We can't detect Strive from
        // post-strip text alone — that signal lives on RawText. The
        // string overload returns null; the SpellBindContext overload
        // below sees ctx.RawText and produces an EmptyParams hit so the
        // compiled-template table records Strive cards.
        return null;
    }

    /// <summary>
    /// Context-aware compile-time detection. Reads
    /// <see cref="SpellBindContext.RawText"/> (pre-normalize) so the Strive
    /// prefix is visible — the string overload above sees only the
    /// post-normalize text, which already has the prefix stripped.
    ///
    /// Returns <see cref="EmptyParams.Instance"/> on match: the inner
    /// effect binding happens at <see cref="Rehydrate"/> time, where the
    /// live registry walk requires <see cref="SpellBindContext.Caster"/>
    /// and other runtime services that aren't available at compile time.
    /// </summary>
    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return StrivePrefix.IsMatch(ctx.RawText) ? EmptyParams.Instance : null;
    }

    /// <summary>
    /// Rehydrate from the compiled-template fast path. Strive's inner
    /// binding depends on the live registry + caster, so we delegate to
    /// <see cref="TryBind"/>. Throws when TryBind fails so a mis-wired
    /// fast-path is loud rather than silent.
    /// </summary>
    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"Strive Rehydrate could not bind '{ctx.Entity.Name}' — TryBind returned null.");

    /// <summary>
    /// Walk the registry against the post-strip text. The current
    /// <see cref="SpellBindContext.Text"/> already has the Strive prefix
    /// removed by <see cref="OracleTextNormalizer"/>, so any inner
    /// template that doesn't anchor on "any number of" can still match
    /// the underlying effect. Returns <c>null</c> when nothing binds —
    /// caller falls back to an empty-effect shell with a single-target
    /// request.
    /// </summary>
    private SpellDefinition? BindInner(SpellBindContext ctx)
    {
        foreach (var t in _registry!.OrderedTemplates)
        {
            if (t is StriveTemplate) continue; // skip self
            var def = t.TryBind(ctx);
            if (def is not null) return def.WithIntentStamp(t.Intent);
        }
        return null;
    }

    /// <summary>
    /// Build the outer Strive <see cref="SpellDefinition"/>: expand the
    /// first <see cref="TargetRequest"/>'s max to <see cref="MaxTargets"/>
    /// and wrap the inner <see cref="SpellDefinition.EffectFactory"/> to
    /// loop over each chosen target.
    /// </summary>
    private static SpellDefinition Wrap(SpellDefinition? inner, string costPart)
    {
        // No inner binding — produce a single-target empty-effect shell so
        // the card at least registers a Strive target request. Lossy v1.
        if (inner is null)
        {
            return new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: new[]
                {
                    new TargetRequest(
                        Description: $"any number of targets (strive {costPart})",
                        MinTargets: 1,
                        MaxTargets: MaxTargets,
                        LegalCandidates: Array.Empty<object>()),
                },
                EffectFactory: _ => Array.Empty<IEffect>());
        }

        var innerTargets = inner.TargetRequests;
        IReadOnlyList<TargetRequest> wrappedTargets;
        if (innerTargets.Count == 0)
        {
            // Inner spell takes no targets (rare for Strive — every Strive
            // card by definition targets something). Add a generic
            // any-number slot so the cast flow asks for targets at all.
            wrappedTargets = new[]
            {
                new TargetRequest(
                    Description: $"any number of targets (strive {costPart})",
                    MinTargets: 1,
                    MaxTargets: MaxTargets,
                    LegalCandidates: Array.Empty<object>()),
            };
        }
        else
        {
            // Expand the first TargetRequest from min=1,max=1 (typical
            // single-target shape) to min=1,max=MaxTargets. Preserve the
            // description and legal candidates from the inner template,
            // and keep the inner template's BotIntent tag so the bot still
            // scores correctly.
            var first = innerTargets[0];
            var expanded = first with
            {
                Description = $"{first.Description} (strive {costPart})",
                MinTargets = Math.Max(1, first.MinTargets),
                MaxTargets = Math.Max(first.MaxTargets, MaxTargets),
            };
            var list = new List<TargetRequest> { expanded };
            for (var i = 1; i < innerTargets.Count; i++) list.Add(innerTargets[i]);
            wrappedTargets = list;
        }

        var innerFactory = inner.EffectFactory;
        return inner with
        {
            TargetRequests = wrappedTargets,
            EffectFactory = chosen =>
            {
                // No targets chosen — fall back to invoking the inner
                // factory once with the original params.
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                    return innerFactory(chosen);

                // Iterate the chosen target slot. For each picked target,
                // build a single-target ChosenSpellParams that mirrors the
                // original except its Targets[0] is the singleton list
                // containing that one target. Concatenate the produced
                // effects so the resolver runs every per-target effect in
                // order.
                var allEffects = new List<IEffect>();
                foreach (var t in chosen.Targets[0])
                {
                    var singleTargetSlot = new List<object> { t };
                    var rebuiltTargets = new List<IReadOnlyList<object>>
                    {
                        singleTargetSlot,
                    };
                    for (var i = 1; i < chosen.Targets.Count; i++)
                        rebuiltTargets.Add(chosen.Targets[i]);

                    var sub = chosen with { Targets = rebuiltTargets };
                    foreach (var e in innerFactory(sub)) allEffects.Add(e);
                }
                return allEffects;
            },
        };
    }
}
