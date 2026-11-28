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
/// CR 700.2 — the header's pick word sets how many modes the caster
/// chooses (<see cref="SpellDefinition.MinModes"/> /
/// <see cref="SpellDefinition.MaxModes"/>): "one" 1/1, "two" 2/2, "three"
/// 3/3, "one or both" 1/2, "one or more" 1/N. No mode twice (CR 700.2d).
/// Only the chosen modes' targets are chosen (SpellCastFlow) and the chosen
/// modes resolve in printed order (CR 608.2c). Escalate (CR 702.121) with a
/// mana cost is charged per extra mode; a spell whose escalate cost is not
/// mana (Collective Brutality's "Discard a card") cannot be charged here, so it
/// stays at one mode rather than granting extra modes for free.
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

    // Bullet marker is the canonical mode separator. Match the bullet
    // anywhere (start-of-line OR mid-text), because the
    // <see cref="OracleTextNormalizer"/> whitespace pass collapses Scryfall's
    // line breaks before bullets to spaces.
    private static readonly Regex BulletSplit = new(
        @"\s*[•·]\s*",
        RegexOptions.Compiled);

    private static readonly Regex ReminderText = new(@"\([^)]*\)", RegexOptions.Compiled);

    // CR 702.121 — "Escalate {1}" (mana) or "Escalate—Discard a card" (other).
    private static readonly Regex EscalateRegex = new(
        @"\bescalate\s*(?:(?<mana>(?:\{[^}]+\})+)|[—-]\s*(?<other>[^(.]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var result = new Dictionary<string, string>
        {
            ["modes"] = JsonSerializer.Serialize(clauses),
            ["pick"] = header.Groups["pick"].Value.ToLowerInvariant().Replace("  ", " "),
        };
        var escalate = EscalateRegex.Match(oracleText);
        if (escalate.Success)
        {
            result["escalate"] = escalate.Groups["mana"].Success
                ? escalate.Groups["mana"].Value
                : "other:" + escalate.Groups["other"].Value.Trim();
        }
        return result;
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
        var (minModes, maxModes) = ModeBounds(pick, clauses.Count);

        // CR 702.121 — escalate: a mana cost per mode beyond the first.
        EscalateSpec? escalateSpec = null;
        if (@params.TryGetValue("escalate", out var esc))
        {
            if (esc.StartsWith("other:", StringComparison.Ordinal))
            {
                // Not a mana cost: it can't be charged from here, so don't let
                // the caster choose the extra modes it pays for.
                maxModes = minModes;
            }
            else
            {
                // CR 601.2f-g — no probe: SpellCastFlow checks the base cost
                // plus the escalate mana with the cast path's affordability
                // check (floating pool, else untapped sources).
                escalateSpec = new EscalateSpec(
                    Description: esc,
                    BuildPerModeCost: _ => new Majik.Core.Costs.EscalateManaAdditionalCost(
                        Majik.Core.ValueObjects.ManaCost.Parse(esc)));
            }
        }

        // CR 700.2d / 601.2c — each mode's body must read ITS OWN target
        // slots, and only the chosen modes' targets are chosen. Stamp every
        // request with its mode index: SpellCastFlow then prompts only the
        // chosen modes and fills the ModalTargetLayout (mode by mode, one slot
        // per request), and mode m's body gets its slice of that layout. Before
        // this, a mode body read the first slots of the whole list (Drown in the
        // Loch's destroy mode threw), and a mode with two requests (Bushwhack's
        // fight) fell onto the index-aligned path and read the wrong slots.
        var requests = new List<TargetRequest>();
        for (var m = 0; m < modeDefs.Count; m++)
        {
            if (modeDefs[m] is not { } md) continue;
            requests.AddRange(md.TargetRequests.Select(r => r with { ModeIndex = m }));
        }
        var offsets = Majik.Core.Game.ModalTargetLayout.Offsets(modeDefs.Count, requests, out _);

        IReadOnlyList<IReadOnlyList<object>> SlotsFor(int mode, ChosenSpellParams p)
        {
            var count = modeDefs[mode]!.TargetRequests.Count;
            if (count == 0) return Array.Empty<IReadOnlyList<object>>();
            var start = offsets[mode];
            var slice = new IReadOnlyList<object>[count];
            for (var k = 0; k < count; k++)
            {
                slice[k] = start + k < p.Targets.Count ? p.Targets[start + k] : Array.Empty<object>();
            }
            return slice;
        }

        return new SpellDefinition(
            Modes: clauses,
            HasVariableX: modeDefs.Any(d => d?.HasVariableX == true),
            TargetRequests: requests,
            ModeIntents: modeIntents,
            MinModes: minModes,
            MaxModes: maxModes,
            Escalate: escalateSpec,
            EffectFactory: p =>
            {
                // CR 700.2 — the chosen modes (ModeIndexes; the scalar
                // ModeIndex for a single-mode spell), each at most once
                // (CR 700.2d), at most MaxModes, resolved in PRINTED order
                // (CR 608.2c) whatever order they were announced in.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : new[] { p.ModeIndex ?? 0 };
                var chosen = ChosenModes.InPrintedOrder(indices, modeDefs.Count, maxModes);
                var effects = new List<Majik.Core.Abilities.IEffect>();
                foreach (var idx in chosen)
                {
                    var picked = modeDefs[idx];
                    if (picked != null) effects.AddRange(picked.EffectFactory(p with { Targets = SlotsFor(idx, p) }));
                }
                return effects;
            });
    }

    /// <summary>CR 700.2 — the number of modes the pick word lets the caster
    /// choose, bounded by the printed mode count.</summary>
    internal static (int Min, int Max) ModeBounds(string pick, int modeCount)
    {
        var (min, max) = pick switch
        {
            "one" => (1, 1),
            "two" => (2, 2),
            "three" => (3, 3),
            "one or both" => (1, 2),
            "one or more" => (1, modeCount),
            _ => (1, 1),
        };
        max = Math.Clamp(max, 1, Math.Max(1, modeCount));
        return (Math.Min(min, max), max);
    }
}
