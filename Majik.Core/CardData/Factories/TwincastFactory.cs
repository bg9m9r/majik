using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Twincast (Time Spiral, {U}{U}).
///
/// Instant. Oracle text:
///   "Copy target instant or sorcery spell. You may choose new targets for
///    the copy."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}{U}. <see cref="CardName"/> flips
///   <c>IsImplemented</c>.
/// - The resolution effect is supplied at cast time by
///   <see cref="SpellTemplates.Templates.Copy.CopyTargetSpellTemplate"/>
///   (matched on the oracle text via <see cref="OracleSpellBinder.Bind"/>) —
///   the same binder-driven path that drives Counterspell's "counter target
///   spell". It declares a 1..1 "target instant or sorcery spell" request and,
///   on resolution, puts a distinct copy of the targeted spell on the stack
///   above it via <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpell"/>
///   (CR 707.10 / 706.10a). The copy resolves first and then ceases to exist
///   (CR 707.10c).
///
/// "You may choose new targets for the copy" (CR 707.10a) is honoured — at
/// resolution the copy effect re-prompts the copier for new targets via
/// <see cref="Majik.Core.Services.SpellCopier.PushCopyOfTopSpellAsync"/> using
/// the targeted spell's retained per-slot requests; declining keeps the
/// original target.
/// </summary>
[CardName("Twincast")]
public static class TwincastFactory
{
    public const string CardName = "Twincast";
    public const string PrintedManaCost = "{U}{U}";

    /// <summary>CardDef DSL — Instant shape only. The copy SpellDefinition is
    /// bound at cast time by the spell-template registry (CR 707.10).</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);
}
