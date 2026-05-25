using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Painful Truths (Battle for Zendikar, {2}{B}).
///
/// Sorcery. Printed oracle text per Scryfall (Battle for Zendikar,
/// 2015-10-02, oracle id <c>0123e6e7-32c3-4f7d-a9b0-7c8d5b1a8d1e</c>):
///   "Converge — Draw cards equal to the number of colors of mana spent
///    to cast this spell, then you lose that much life."
///
/// ## Oracle delta (v1)
///
/// The 2015 Scryfall oracle reads "Draw cards equal to the number of
/// colors of mana spent to cast this spell, then you lose that much
/// life." (so 1 color → 1 card / 1 life; 5 colors → 5 cards / 5 life).
/// The older printed text said "Draw three cards and you lose 3 life,
/// where 3 is the number of colors…" with an explicit floor of 3 at
/// minimum — same arithmetic for the typical 3-color cast in Modern.
/// v1 ships the dynamic Scryfall oracle: N = colors-spent, capped at
/// 5 (any cast can produce at most 5 distinct WUBRG pips).
///
/// ## Implemented (v1)
///
/// - <b>Sorcery {2}{B}</b>. Owner / controller wired.
/// - <b>Converge body</b> — same caller-supplied provider shape as
///   <see cref="BringToLightFactory"/> /
///   <see cref="PrismaticEndingFactory"/>: a
///   <c>Func&lt;int&gt; colorsSpentProvider</c> reports the count of
///   distinct colors of mana spent on this cast. When null,
///   <see cref="DefaultColorsSpent"/> (1 — the printed {B} pip floor)
///   is used. Real cast-time provenance plugs in when the mana resolver
///   exposes a distinct-colors ledger to spell definitions.
/// - <b>Resolve effect</b> (<see cref="BuildResolveEffect"/>):
///   1. Read <c>n = colorsSpentProvider()</c> clamped to <c>[0, 5]</c>.
///   2. <see cref="Fx.DrawCards"/>(caster, n) — CR 121.1 (empty library
///      stamps the loss flag via Fx.DrawCards' internal CR 704.5b path).
///   3. <see cref="Fx.LoseLife"/>(caster, n) — CR 119.1. Note the
///      printed "then" sequencing: draw fires first, life loss second.
///      Black-board interactions (Dark Confidant style draw-then-pay)
///      preserve that ordering.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real mana-provenance ledger</b>: matches the Bring to Light /
///   Prismatic Ending gap. The cost flow does not yet expose a
///   per-spell ledger of paid pips, so the colours-spent count is
///   supplied by the caller.
/// - <b>"Then" hard ordering</b>: Fx.DrawCards + Fx.LoseLife are
///   invoked sequentially in the same Effect closure; no separate
///   stack object is created. The "then" wording is honoured by
///   ordering, not by separate resolution windows — adequate for v1
///   (Dark Confidant pays its life loss before the next draw step
///   because the spell resolves in one go).
/// </summary>
[CardName("Painful Truths")]
public static class PainfulTruthsFactory
{
    public const string CardName = "Painful Truths";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>Default colors-spent when no provider is supplied. The
    /// printed cost is {2}{B}; only one colored pip is mandatory, so 1
    /// is the safe floor. Tests + real casts override via
    /// <see cref="BuildSpellDefinition"/>'s provider.</summary>
    public const int DefaultColorsSpent = 1;

    /// <summary>Maximum colours of mana spent on any cast (WUBRG).</summary>
    public const int MaxColorsSpent = 5;

    public const string OracleText =
        "Converge — Draw cards equal to the number of colors of mana " +
        "spent to cast this spell, then you lose that much life.";

    /// <summary>
    /// Construct Painful Truths as a <see cref="Sorcery"/> owned by
    /// <paramref name="owner"/>. Card shape only — the resolve closure
    /// is produced by <see cref="BuildResolveEffect"/> /
    /// <see cref="BuildSpellDefinition"/>.
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
    /// targets — the resolve fires entirely against the caster.
    /// </summary>
    /// <param name="caster">The casting player.</param>
    /// <param name="colorsSpentProvider">Optional reader for the count
    /// of distinct colours of mana spent on this cast. Null defaults to
    /// <see cref="DefaultColorsSpent"/>. Values outside <c>[0, 5]</c>
    /// are clamped at resolve time.</param>
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
    /// Build the resolve effect: draw N, lose N life. The colours-spent
    /// count is read from <paramref name="colorsSpentProvider"/> when
    /// supplied, otherwise <see cref="DefaultColorsSpent"/>. The count
    /// is clamped to <c>[0, 5]</c> — WUBRG bounds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        Func<int>? colorsSpentProvider)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw N, lose N life (N = colors of mana spent).",
                () =>
                {
                    var n = colorsSpentProvider?.Invoke() ?? DefaultColorsSpent;
                    if (n < 0) n = 0;
                    if (n > MaxColorsSpent) n = MaxColorsSpent;

                    // CR 121.1 — "Draw N cards." Fx.DrawCards handles
                    // empty-library loss flagging (CR 704.5b).
                    Fx.DrawCards(caster, n);

                    // CR 119.1 — "you lose N life." Sequenced after the
                    // draw per the printed "then" clause.
                    Fx.LoseLife(caster, n);
                }),
        };
    }
}
