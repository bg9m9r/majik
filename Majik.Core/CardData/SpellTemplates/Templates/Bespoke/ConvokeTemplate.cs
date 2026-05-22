using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// CR 702.51 — Convoke. "Your creatures can help cast this spell."
///
/// <para>The <see cref="OracleTextNormalizer"/> already strips the leading
/// "Convoke (...)" reminder before any template sees <see cref="SpellBindContext.Text"/>,
/// so the underlying effect text (e.g. "Destroy target tapped creature.",
/// "Prevent all damage that would be dealt by creatures this turn.")
/// generally binds via the regular effect templates. This bespoke
/// template adds two pieces on top:</para>
///
/// <list type="number">
///   <item>It RECOGNISES the Convoke prefix on <see cref="SpellBindContext.RawText"/>
///         so coverage reports can attribute these cards.</item>
///   <item>It RECURSIVELY rebinds the post-strip effect via the registry,
///         then returns the inner <see cref="SpellDefinition"/> unchanged.
///         This guarantees the inner effect resolves identically whether
///         or not this template ran, while keeping a single entry point
///         that future cost-reduction wiring can hook into.</item>
/// </list>
///
/// <para><b>v1 is intentionally lossy.</b> The Convoke cost reduction
/// machinery lives in <see cref="Majik.Core.Costs.ConvokeAlternativeCost"/>
/// but is NOT yet consulted by <see cref="SpellCastFlow"/>. Casters of
/// Convoke spells still pay the full printed mana cost. Follow-ups will:
///   <list type="bullet">
///     <item>Prompt the agent to choose untapped creatures to tap.</item>
///     <item>Reduce the spell's effective cost per CR 702.51b.</item>
///     <item>Tap the chosen creatures as part of cost payment.</item>
///   </list></para>
///
/// <para>Priority 95: high enough to beat the catch-all
/// <c>ClauseCompositionTemplate</c> (priority 10) and the generic
/// effect templates (50–80) on cards whose post-strip text would
/// otherwise match nothing — without overshooting the modal composer
/// (priority 250) for cards like Artistic Refusal whose body is
/// "Choose one or both —".</para>
/// </summary>
public sealed class ConvokeTemplate : ISpellTemplate
{
    private static readonly Regex ConvokePrefix = new(
        @"^\s*convoke\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private SpellTemplateRegistry? _registry;

    public int Priority => 95;
    public string Name => "Convoke";

    // BotIntent.None: Convoke is a cost modifier, not an effect category.
    // The inner SpellDefinition's intent (stamped by whichever template
    // matched the post-strip text) carries the strategic signal.
    public BotIntent Intent => BotIntent.None;

    public void SetRegistry(SpellTemplateRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public bool CanBind(SpellBindContext ctx) => _registry is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (_registry == null) return null;
        if (!ConvokePrefix.IsMatch(ctx.RawText)) return null;

        // The post-strip effect text lives in ctx.Text (Normalize already
        // drops the Convoke reminder). Recursively bind it against the
        // registry — skip this template to avoid recursion loops on cards
        // whose oracle text contains "Convoke" twice (unheard of but
        // defensive).
        var subEntity = new CardEntity
        {
            Name = ctx.Entity.Name,
            OracleText = ctx.Text,
            ManaCost = ctx.Entity.ManaCost,
            TypeLine = ctx.Entity.TypeLine,
        };
        var subCtx = new SpellBindContext(
            subEntity, ctx.Caster, ctx.Resolver,
            ctx.Effects, ctx.Stack, ctx.Replacements,
            ctx.Triggers, ctx.EventBus, ctx.Zones);

        foreach (var t in _registry.OrderedTemplates)
        {
            if (ReferenceEquals(t, this)) continue;
            var def = t.TryBind(subCtx);
            if (def != null)
            {
                return def.WithIntentStamp(t.Intent);
            }
        }

        // No inner template matched — return a no-op shell so the card
        // still casts (full mana, no effect). Better than returning null,
        // which would cause TurnDriver.DispatchCast to RotateHand and the
        // bot to never try this card.
        return SpellDefinition.Vanilla(_ => Array.Empty<IEffect>());
    }

    /// <summary>
    /// Context-aware compile-time detection — anchors on
    /// <see cref="SpellBindContext.RawText"/> because the
    /// <see cref="OracleTextNormalizer"/> strips the Convoke reminder from
    /// <see cref="SpellBindContext.Text"/>. The string overload (default
    /// null) keeps the legacy path unchanged.
    /// </summary>
    public IReadOnlyDictionary<string, string>? TryExtractParams(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ConvokePrefix.IsMatch(ctx.RawText) ? EmptyParams.Instance : null;
    }

    /// <summary>
    /// Rehydrate from the compiled-template fast path. Inner binding
    /// requires the live registry + caster, so we delegate to
    /// <see cref="TryBind"/>. Throws when TryBind returns null so a
    /// mis-wired fast path surfaces immediately.
    /// </summary>
    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TryBind(ctx) ?? throw new InvalidOperationException(
            $"Convoke Rehydrate could not bind '{ctx.Entity.Name}' — TryBind returned null.");
}
