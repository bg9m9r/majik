using System.Text.Json;
using System.Text.RegularExpressions;
using Majik.Core.CardData;
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
    // Header captures the "pick word" so Rehydrate can decide single-mode
    // vs multi-mode evaluation. Compile-templates stashes the parsed pick
    // alongside the modes JSON; Rehydrate restores it.
    private static readonly Regex HeaderRegex = new(
        @"choose\s+(?<pick>one\s+or\s+more|one\s+or\s+both|one|two|three)\s*[—-]\s*",
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

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public bool CanBind(SpellBindContext ctx) => _registry is not null;

    /// <summary>
    /// Pure-parse path: serialize each bullet body as its own oracle string
    /// in a JSON array under the "modes" key. Rehydrate later rebinds each
    /// mode against the live registry — this keeps the live-binding tree
    /// intact while letting compile-templates record the modal card as
    /// bound (rather than being invisible to the pure-parse compile loop).
    /// </summary>
    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        if (string.IsNullOrWhiteSpace(oracleText)) return null;
        var header = HeaderRegex.Match(oracleText);
        if (!header.Success) return null;

        var tail = oracleText.Substring(header.Index + header.Length);
        var clauses = BulletSplit.Split(tail)
            .Select(c => ReminderText.Replace(c, "").Trim().TrimEnd('.').Trim())
            .Where(c => c.Length > 0)
            .ToList();

        if (clauses.Count < 2) return null;

        return new Dictionary<string, string>
        {
            ["modes"] = JsonSerializer.Serialize(clauses),
            ["pick"] = header.Groups["pick"].Value.ToLowerInvariant().Replace("  ", " "),
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        if (_registry == null)
            throw new InvalidOperationException(
                "ModalChooseOne.Rehydrate called before SetRegistry.");
        if (!@params.TryGetValue("modes", out var json) || string.IsNullOrEmpty(json))
            throw new InvalidOperationException(
                "ModalChooseOne params missing 'modes' key.");

        var clauses = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new InvalidOperationException(
                "ModalChooseOne: failed to deserialize 'modes' payload.");

        var modeDefs = new List<SpellDefinition?>();
        var modeIntents = new List<Majik.Core.Cards.BotIntent>();
        foreach (var clause in clauses)
        {
            var sub = new CardEntity { Name = ctx.Entity.Name, OracleText = clause };
            var subCtx = new SpellBindContext(sub, ctx.Caster, ctx.Resolver,
                ctx.Effects, ctx.Stack, ctx.Replacements);
            SpellDefinition? def = null;
            var clauseIntent = Majik.Core.Cards.BotIntent.None;
            foreach (var t in _registry.OrderedTemplates)
            {
                if (t is ModalChooseOneTemplate) continue; // avoid recursion
                def = t.TryBind(subCtx);
                if (def != null)
                {
                    def = def.WithIntentStamp(t.Intent);
                    clauseIntent = t.Intent;
                    // Composer matched: its own Intent is None by design, but
                    // each composed sub-template stamped its Intent onto the
                    // sub-clause's TargetRequests. Mode intent = union of
                    // those, so the modal's per-mode signal reflects the
                    // full clause's effect set.
                    if (clauseIntent == Majik.Core.Cards.BotIntent.None)
                    {
                        foreach (var req in def.TargetRequests)
                            clauseIntent |= req.Intent;
                    }
                    break;
                }
            }
            modeDefs.Add(def);
            modeIntents.Add(clauseIntent);
        }

        var pick = @params.TryGetValue("pick", out var pk) ? pk : "one";
        var pickCount = PickCount(pick);

        return new SpellDefinition(
            Modes: clauses,
            HasVariableX: modeDefs.Any(d => d?.HasVariableX == true),
            TargetRequests: modeDefs.Where(d => d != null)
                .SelectMany(d => d!.TargetRequests).ToList(),
            ModeIntents: modeIntents,
            EffectFactory: p =>
            {
                // Multi-mode: prefer ModeIndexes when set. Caller supplies a
                // list of distinct mode indices; each chosen mode's effects
                // run in declaration order (CR 700.2d). Falls back to
                // scalar ModeIndex for legacy single-mode callers and for
                // Choose-one cards (pickCount == 1).
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : new[] { p.ModeIndex ?? 0 };
                var effects = new List<Majik.Core.Abilities.IEffect>();
                var seen = new HashSet<int>();
                foreach (var idx in indices)
                {
                    if (idx < 0 || idx >= modeDefs.Count) continue;
                    if (!seen.Add(idx)) continue; // CR 700.2d — each mode at most once
                    if (seen.Count > pickCount) break; // honor printed pick count
                    var picked = modeDefs[idx];
                    if (picked != null) effects.AddRange(picked.EffectFactory(p));
                }
                return effects;
            });
    }

    // Maps the header pick word to the max distinct mode count.
    // "one or more" caps at modeDefs.Count at the caller; here treat as
    // "all" by returning int.MaxValue so the runtime list bounds it.
    private static int PickCount(string pick) => pick switch
    {
        "one" => 1,
        "two" => 2,
        "three" => 3,
        "one or both" => 2,
        "one or more" => int.MaxValue,
        _ => 1,
    };
}
