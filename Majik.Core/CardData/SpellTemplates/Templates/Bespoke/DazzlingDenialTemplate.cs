using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Bespoke spell template for Dazzling Denial (Bloomburrow, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Counter target spell unless its controller pays {2}. If you control a
///    Bird, counter that spell unless its controller pays {4} instead."
///
/// ## Why a bespoke template
/// The first sentence alone is already covered by
/// <see cref="Counter.CounterUnlessPayTemplate"/> (Priority 100) — but that
/// generic template would silently DROP the Bird-conditional "pay {4} instead"
/// rider, leaving a flat "pay {2}" counter regardless of board state (same
/// drop-the-extra hazard <see cref="GleefulDemolitionTemplate"/> guards
/// against). This template owns BOTH halves and runs at a higher priority so it
/// wins the registry race for this exact oracle text — beating the catch-all
/// counter template AND the clause composer (Priority 200), which would
/// otherwise split the two sentences and re-bind the second on its own.
///
/// ## Behaviour
/// - <b>Target</b> (CR 115.1): one "target spell" on the stack.
/// - <b>Resolution</b> (CR 608.2): a single "counter target spell unless its
///   controller pays {N}" rider (the Mana Leak / Mana Tithe family — see
///   <see cref="Majik.Core.Primitives.PayUnlessCounterRider"/>). The tax {N} is
///   chosen AT RESOLUTION (CR 118.4 — the "If you control a Bird" intervening-if
///   is checked as the spell resolves, not at cast): {4} if the caster controls
///   a Bird (<see cref="Card.HasSubtype"/>(<see cref="CardSubtype.Bird"/>)),
///   otherwise {2}. The target spell's controller is then asked whether to pay
///   {N} to keep their spell; on "yes" + affordable the counter no-ops, on "no"
///   / can't afford the spell is countered (CR 701.5; uncounterable spells
///   survive, CR 701.5b).
///
/// CR 608.2b — resolution-time legality re-check: if the target has already
/// left the stack the rider is a clean no-op.
/// </summary>
public sealed class DazzlingDenialTemplate : ISpellTemplate
{
    /// <summary>CR 118.4 — base tax when the caster controls no Bird.</summary>
    public const int BaseUnlessPay = 2;

    /// <summary>CR 118.4 — raised tax when the caster controls a Bird.</summary>
    public const int BirdUnlessPay = 4;

    // Anchor on the full oracle: the Bird-conditional "pay {4} instead" rider is
    // distinctive enough that no other card matches; high priority so it beats
    // the generic CounterUnlessPayTemplate and the clause composer.
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+spell\s+unless\s+its\s+controller\s+pays\s+\{?2\}?.*if\s+you\s+control\s+a\s+bird.*pays\s+\{?4\}?\s+instead",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public int Priority => 300;
    public string Name => "DazzlingDenial";
    public BotIntent Intent => BotIntent.Counter;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        oracleText != null && Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Build(ctx.Caster, ctx.Resolver, ctx.Stack);
    }

    /// <summary>
    /// Build the runnable <see cref="SpellDefinition"/>. Shared with
    /// <see cref="Factories.DazzlingDenialFactory.BuildSpellDefinition"/> so the
    /// prod binder path and the factory test path stay one source of truth.
    /// </summary>
    /// <param name="caster">The player who cast Dazzling Denial — whose board is
    /// scanned for a Bird at resolution (CR 118.4).</param>
    /// <param name="resolver">Maps the agent-supplied raw target token to the
    /// live engine object (pass-through in tests).</param>
    /// <param name="stack">Active stack; required to remove the countered spell.
    /// Null ⇒ the rider is a clean no-op (shape-only build).</param>
    public static SpellDefinition Build(
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>(), Intent: BotIntent.Counter),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];

                // CR 118.4 — "If you control a Bird … pays {4} instead." The
                // intervening-if is evaluated AS the spell resolves, so the tax
                // is computed inside the resolution closure (not captured at
                // cast / bind time) — a Bird that entered after Dazzling Denial
                // was cast still raises the tax.
                return new IEffect[]
                {
                    Majik.Core.Primitives.PayUnlessCounterRider.Build(
                        "Dazzling Denial — counter target spell unless its controller pays {2} ({4} if you control a Bird)",
                        stack,
                        () => resolver(raw) as ISpell,
                        unlessPayN: ControlsBird(caster) ? BirdUnlessPay : BaseUnlessPay),
                };
            });
    }

    /// <summary>
    /// CR 118.4 — true when <paramref name="caster"/> controls at least one
    /// Bird (<see cref="CardSubtype.Bird"/>) on the battlefield.
    /// </summary>
    private static bool ControlsBird(Player caster)
    {
        foreach (var permanent in caster.Zones.Battlefield.GetCards())
        {
            if (permanent.HasSubtype(CardSubtype.Bird)) return true;
        }
        return false;
    }
}
