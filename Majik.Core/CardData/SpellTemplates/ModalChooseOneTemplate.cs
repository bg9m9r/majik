using System.Text.Json;
using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Modal "Choose one —" composer. Detects oracle text that opens with a
/// "Choose one —" (or "Choose two —", "Choose one or both —") header and
/// splits the following bullet-prefixed clauses (• marker) into a list of
/// modes. Each mode's body is bound via the rest of the registry; the
/// resulting <see cref="SpellDefinition"/> exposes the mode labels via
/// <see cref="SpellDefinition.Modes"/> and switches its
/// <see cref="SpellDefinition.EffectFactory"/> on
/// <see cref="ChosenSpellParams.ModeIndex"/>.
///
/// MVP supports "Choose one" (single mode picked). Multi-pick variants
/// ("Choose two", "Choose one or both") still bind the modes but only
/// the first ModeIndex is honored — full multi-mode runs are a follow-up
/// (engine needs ChosenSpellParams.ModeIndexes list rather than scalar).
///
/// Priority 250 so the composer runs BEFORE single-template binds AND
/// before ClauseCompositionTemplate (which would otherwise eat the
/// header + bullets as separate clauses).
///
/// Cards that bind through this template: Boros Charm, Thraben Charm,
/// Bant Charm, every modular Charm/Cryptic/etc. cycle. Their per-mode
/// bodies must themselves be patterns the engine knows — e.g. Boros
/// Charm's "deals 4 damage to target player or planeswalker" hits
/// DamagePlayerTemplate, "Permanents you control gain indestructible
/// until end of turn" currently has no template so that mode is a no-op
/// shell at v1.
/// </summary>
public sealed class ModalChooseOneTemplate : ISpellTemplate
{
    private static readonly Regex HeaderRegex = new(
        @"choose\s+(?:one|two|one\s+or\s+both)\s*[—-]\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BulletSplit = new(
        @"^\s*[•·]\s*",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ReminderText = new(@"\([^)]*\)", RegexOptions.Compiled);

    private SpellTemplateRegistry? _registry;

    public int Priority => 250;
    public string Name => "ModalChooseOne";

    public void SetRegistry(SpellTemplateRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (_registry == null) return null;

        var text = ctx.Text;
        var header = HeaderRegex.Match(text);
        if (!header.Success) return null;

        // Tail after the "Choose one —" marker; bullets are the per-mode bodies.
        var tail = text.Substring(header.Index + header.Length);
        var clauses = BulletSplit.Split(tail)
            .Select(c => ReminderText.Replace(c, "").Trim().TrimEnd('.').Trim())
            .Where(c => c.Length > 0)
            .ToList();

        if (clauses.Count < 2) return null;

        // Bind each clause as its own oracle text — synthesize a sub-context
        // by cloning the entity with the clause text. Drops sub-defs that
        // fail to bind, but requires at least one mode binds, else the
        // composer aborts so the card falls back to a vanilla shell.
        var modeDefs = new List<SpellDefinition?>();
        foreach (var clause in clauses)
        {
            var sub = new CardEntity { Name = ctx.Entity.Name, OracleText = clause };
            var subCtx = new SpellBindContext(sub, ctx.Caster, ctx.Resolver,
                ctx.Effects, ctx.Stack, ctx.Replacements);
            SpellDefinition? def = null;
            foreach (var t in _registry.OrderedTemplates)
            {
                if (t is ModalChooseOneTemplate) continue; // avoid recursion
                def = t.TryBind(subCtx);
                if (def != null) break;
            }
            modeDefs.Add(def);
        }

        if (modeDefs.All(d => d is null)) return null;

        return new SpellDefinition(
            Modes: clauses,
            HasVariableX: modeDefs.Any(d => d?.HasVariableX == true),
            // v1: union all sub-target-requests so cast-flow collects targets
            // for every mode. Refinement: scope target collection to chosen
            // mode (needs cast-flow restructure).
            TargetRequests: modeDefs.Where(d => d != null)
                .SelectMany(d => d!.TargetRequests).ToList(),
            EffectFactory: p =>
            {
                var idx = p.ModeIndex ?? 0;
                if (idx < 0 || idx >= modeDefs.Count) return Array.Empty<Majik.Core.Abilities.IEffect>();
                var picked = modeDefs[idx];
                return picked?.EffectFactory(p) ?? Array.Empty<Majik.Core.Abilities.IEffect>();
            });
    }
}
