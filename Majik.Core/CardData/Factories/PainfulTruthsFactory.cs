using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Painful Truths (Battle for Zendikar, {1}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Converge — You draw X cards and lose X life, where X is the number
///    of colors of mana spent to cast this spell."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}{B}.
/// - <b>Converge bound</b> via a caller-supplied
///   <c>Func&lt;int&gt; colorsSpentProvider</c> — same mana-provenance
///   shape as <see cref="BringToLightFactory"/> / <see cref="PrismaticEndingFactory"/>.
///   When null, X defaults to <see cref="DefaultColorsSpent"/> (1 — the
///   printed colored-pip floor, two black pips both count as a single
///   distinct color). Real cast-time provenance plugs in once the mana
///   resolver exposes a distinct-colors ledger to spell definitions.
/// - Resolve effect via <see cref="BuildResolveEffect"/>:
///     1. Read X via <paramref name="colorsSpentProvider"/> (clamped ≥ 0).
///     2. <see cref="Fx.DrawCards"/> — caster draws X cards (CR 121.1 /
///        CR 614 — replacement bus gets a shot per draw). Empty library
///        stamps the standard "tried to draw from empty" loss flag
///        (CR 704.5b).
///     3. <see cref="Fx.LoseLife"/> — caster loses X life (CR 119.3).
///        Negative / zero X no-ops cleanly.
///
/// ## Order matters
/// The printed wording is a single sentence — "you draw X cards AND lose
/// X life" — which CR 101.4 / 700.2 treats as one event. The engine
/// sequences draw before life-loss because the draws need to fire
/// individually (replacement bus, empty-library flag) before the single
/// life-loss tick — this is the standard treatment for Read the Bones /
/// Sign in Blood / Night's Whisper.
///
/// ## Deferred (v1 gaps)
/// - <b>Mana provenance ledger</b>: matches Bring to Light / Prismatic
///   Ending — distinct-colors-spent provenance requires the cost flow to
///   expose a per-spell ledger. Until then callers supply
///   <c>colorsSpentProvider</c> explicitly (tests do; dispatcher path
///   uses <see cref="DefaultColorsSpent"/>).
/// </summary>
[CardName("Painful Truths")]
public static class PainfulTruthsFactory
{
    public const string CardName = "Painful Truths";
    public const string PrintedManaCost = "{1}{B}{B}";

    /// <summary>
    /// Default X when no <c>colorsSpentProvider</c> is supplied. The
    /// printed cost has 1 distinct colored pip ({B} — the two black pips
    /// are the same color), so 1 is the floor any legal cast must reach.
    /// </summary>
    public const int DefaultColorsSpent = 1;

    /// <summary>
    /// Construct Painful Truths as a Sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve closure is produced by
    /// <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Painful Truths. No
    /// target requests — the converge body resolves entirely on the
    /// caster (draw + life-loss).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<int>? colorsSpentProvider = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, colorsSpentProvider));
    }

    /// <summary>
    /// Build the resolve effect: caster draws X cards and loses X life.
    /// X is read from <paramref name="colorsSpentProvider"/> when
    /// supplied, otherwise <see cref="DefaultColorsSpent"/>.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        Func<int>? colorsSpentProvider)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: Converge — draw X cards and lose X life.",
                () =>
                {
                    var x = colorsSpentProvider?.Invoke() ?? DefaultColorsSpent;
                    if (x < 0) x = 0;
                    if (x == 0) return;

                    // CR 121.1 — draw X. Routes through Fx.DrawCards so
                    // replacement bus (Dredge etc.) gets a shot per draw,
                    // and empty-library stamps the SBA loss flag
                    // (CR 704.5b) without throwing.
                    Fx.DrawCards(caster, x);

                    // CR 119.3 — lose X life. Single life-loss event for
                    // the resolved X (matches "lose X life" not "lose 1
                    // life X times" — important for Blood Artist / Cruel
                    // Celebrant triggers that count loss events, not
                    // points).
                    Fx.LoseLife(caster, x);
                }),
        };
    }
}
