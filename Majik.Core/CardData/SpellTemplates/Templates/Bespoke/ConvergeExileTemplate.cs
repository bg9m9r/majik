using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// Bespoke template for Prismatic Ending's Converge-gated exile (CR 202.2 /
/// CR 106.4):
///
///   "Converge — Exile target nonland permanent if its mana value is less
///    than or equal to the number of colors of mana spent to cast this
///    spell."
///
/// <para>
/// The plain <see cref="Control.ExileTargetTemplate"/> (priority 50) ALSO
/// matches "Exile target nonland permanent", but it ignores the Converge
/// rider entirely — it would exile any nonland permanent regardless of mana
/// value, which is wrong. This template carries a HIGHER priority so it wins
/// the registry race for the Converge family, and reads the real
/// <em>colors-of-mana-spent</em> count off the live mana-provenance ledger
/// (<see cref="Card.PendingCastColors"/>, stamped by
/// <see cref="Majik.Core.Game.TurnDriver"/> right after the mana resolver
/// commits payment) via <see cref="ResolutionContext.SourceCard"/>.
/// </para>
///
/// <para>
/// CR 608.2b — the legality of the target (mv ≤ colors spent, still a nonland
/// permanent on the battlefield) is rechecked AT RESOLUTION, so a target
/// whose mana value rose above the cap, or that became a land / left the
/// battlefield, fizzles with no effect. Colorless / generic mana is not a
/// color and never contributes to the count (CR 106.4); hybrid pips count the
/// color actually paid; Phyrexian pips paid with life count nothing — all of
/// which fall out for free because the ledger records the per-color pool delta
/// of the spend.
/// </para>
/// </summary>
public sealed class ConvergeExileTemplate : ISpellTemplate
{
    // Anchor on the Converge marker + the colors-of-mana-spent rider so this
    // only ever binds the Prismatic Ending family, never a plain "exile target
    // nonland permanent" spell.
    private static readonly Regex Pattern = new(
        @"exile\s+target\s+nonland\s+permanent\b.{0,40}\bmana\s+value\b.{0,80}\bnumber\s+of\s+colors\s+of\s+mana\s+spent\s+to\s+cast\s+this\s+spell",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // Must beat ExileTargetTemplate (priority 50) and the generic clause
    // composer so the Converge gate is honoured.
    public int Priority => 120;
    public string Name => "ConvergeExile";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText)
            ? new Dictionary<string, string>()
            : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var resolver = ctx.Resolver;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target nonland permanent",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        "Prismatic Ending: exile target nonland permanent with mv ≤ colors of mana spent (CR 202.2).",
                        rc =>
                        {
                            ConvergeColorsSpent.ExileIfWithinCap(raw, rc);
                            return ValueTask.CompletedTask;
                        }),
                };
            });
    }
}

/// <summary>
/// Shared helper for "the number of colors of mana spent to cast this spell"
/// (CR 202.2 / CR 106.4 — Converge). Reads the distinct-colors-spent count off
/// the live mana-provenance ledger stamped on the resolving spell's card
/// (<see cref="Card.PendingCastColors"/>, surfaced via
/// <see cref="ResolutionContext.SourceCard"/>). Used by both the
/// Prismatic Ending exile family and the Bring to Light Converge tutor so the
/// ledger read lives in exactly one place.
/// </summary>
public static class ConvergeColorsSpent
{
    /// <summary>
    /// CR 202.2 — the number of DISTINCT colors of mana spent to cast the
    /// resolving spell, read off <see cref="ResolutionContext.SourceCard"/>'s
    /// <see cref="Card.PendingCastColors"/> ledger. Returns 0 when no card is
    /// surfaced or no colored mana was spent (e.g. an {X}{W} cast paid with
    /// only white + colorless mana spent on the generic still records 1 — the
    /// white pip — while a five-color payment records 5).
    /// </summary>
    public static int From(ResolutionContext rc)
    {
        ArgumentNullException.ThrowIfNull(rc);
        return rc.SourceCard is Card concrete
            ? concrete.PendingCastColors?.Count ?? 0
            : 0;
    }

    /// <summary>
    /// Exile <paramref name="target"/> iff it is still a nonland permanent on
    /// the battlefield whose mana value is ≤ the colors-of-mana-spent count
    /// (CR 608.2b — resolution-time legality recheck; CR 701.21 — exile).
    /// </summary>
    public static void ExileIfWithinCap(object? target, ResolutionContext rc)
    {
        ArgumentNullException.ThrowIfNull(rc);
        if (target is not Permanent permanent) return;

        // CR 608.2b — resolution-time legality.
        if (permanent.Zone != ZoneType.Battlefield) return;
        if (permanent.HasType(CardType.Land)) return;

        var cap = From(rc);
        if (permanent.ManaCostValue.TotalValue > cap) return;

        OracleSpellBinder.MoveToExile(permanent);
    }
}
