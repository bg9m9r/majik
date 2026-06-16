using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ichor Slick (Modern Horizons 3, {2}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-16):
///   "Target creature gets -3/-3 until end of turn."
///   "Cycling {2} ({2}, Discard this card: Draw a card.)"
///   "Madness {3}{B} (If you discard this card, discard it into exile.
///    When you do, cast it for its madness cost or put it into your
///    graveyard.)"
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}.
/// - <b>Target creature gets -3/-3 until end of turn</b> —
///   <see cref="Define"/> declares the spell body in the CardDef DSL as
///   <c>PumpUntilEndOfTurn(-3, -3).To(TargetKind.Creature)</c>. The shared
///   <see cref="PumpUntilEndOfTurnEffect"/> primitive handles either sign of
///   the delta (CR 611 — a negative Layer-7c modifier), so the same verb that
///   powers Giant Growth's +3/+3 powers Ichor Slick's -3/-3 (the
///   <see cref="GiantGrowthFactory"/> sibling). On resolution the chosen
///   creature is validated (CR 608.2b — off-battlefield / non-creature target
///   fizzles), then a -3/-3 modifier is registered on its
///   <see cref="Creature.ActiveEffects"/>; it expires in the cleanup step
///   (CR 514.2).
///
/// ## Cycling {2} / Madness {3}{B} — intrinsic, NOT wired here
/// Both riders are engine-intrinsic:
/// - Cycling (CR 702.29) is recognised by the keyword binders / catalog from
///   the printed "Cycling {2}" line — no per-card factory code.
/// - Madness (CR 702.35) is catalogued in
///   <see cref="Majik.Core.Keywords.MadnessCatalog"/> ("Ichor Slick" → {3}{B})
///   and routed through the discard funnel + replacement bus
///   (<see cref="Majik.Core.Effects.MadnessReplacement"/>).
/// This factory only supplies the -3/-3 spell body.
/// </summary>
[CardName("Ichor Slick")]
public static class IchorSlickFactory
{
    public const string CardName = "Ichor Slick";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>Layer 7c stat delta (CR 613.1g) — negative for the -3/-3
    /// debuff. The shared <see cref="PumpUntilEndOfTurnEffect"/> adds the
    /// signed value, so a negative magnitude shrinks the target.</summary>
    public const int Delta = -3;

    /// <summary>
    /// CardDef DSL — the full spell in one fluent declaration. "Target creature
    /// gets -3/-3 until end of turn." compiles to a 1..1 "target creature"
    /// <see cref="Majik.Core.Players.Agents.TargetRequest"/> + a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(-3, -3) resolve step via
    /// <see cref="CardDefRuntime.BuildSpellDefinition"/> — the negated mirror of
    /// <see cref="GiantGrowthFactory"/>.
    /// </summary>
    public static CardDef Define() => CardDef
        .Sorcery(CardName, PrintedManaCost)
        .Resolve(c => c.PumpUntilEndOfTurn(Delta, Delta).To(TargetKind.Creature));

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "target creature gets -3/-3 until end of turn"
    /// <see cref="SpellDefinition"/>. Delegates entirely to the fluent
    /// <c>.Resolve(...)</c> body via
    /// <see cref="CardDefRuntime.BuildSpellDefinition"/>.
    ///
    /// On resolve: the materializer validates the target is still a
    /// <see cref="Creature"/> on the Battlefield (CR 608.2b — illegal target →
    /// no-op), then registers a <see cref="PumpUntilEndOfTurnEffect"/>(-3, -3)
    /// on its <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires in
    /// cleanup). When ActiveEffects is null (shape-only tests) the registration
    /// is a no-op.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinition(Define(), resolver: o => o);
}
