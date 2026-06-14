using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reverberate (Magic 2011, {R}{R}).
///
/// Instant. Oracle text:
///   "Copy target instant or sorcery spell. You may choose new targets for
///    the copy."
///
/// Identical effect to <see cref="TwincastFactory"/> (the red half of the
/// spell-copy pair). The resolution effect is supplied at cast time by
/// <see cref="SpellTemplates.Templates.Copy.CopyTargetSpellTemplate"/> — at
/// resolution it puts a distinct copy of the targeted instant/sorcery spell on
/// the stack above it (CR 707.10 / 706.10a), which resolves first then ceases
/// to exist (CR 707.10c).
///
/// ## Deferred (v1 gap)
/// - <b>"You may choose new targets for the copy"</b> (CR 707.10a): the copy
///   reuses the original spell's chosen targets verbatim — tracked in
///   <see cref="Majik.Core.Services.SpellCopier"/>.
/// </summary>
[CardName("Reverberate")]
public static class ReverberateFactory
{
    public const string CardName = "Reverberate";
    public const string PrintedManaCost = "{R}{R}";

    /// <summary>CardDef DSL — Instant shape only. The copy SpellDefinition is
    /// bound at cast time by the spell-template registry (CR 707.10).</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);
}
