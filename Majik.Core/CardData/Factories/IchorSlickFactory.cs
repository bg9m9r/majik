using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ichor Slick (Modern Horizons 3, {2}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Target creature gets -3/-3 until end of turn.
///    Cycling {2} ({2}, Discard this card: Draw a card.)
///    Madness {3}{B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}, owner / controller (CardDef DSL).
/// - <b>-3/-3 until end of turn</b> — <see cref="BuildDefinition"/> declares a
///   single <see cref="PumpTargetEffectDef"/>(Power: -3, Toughness: -3,
///   TargetFilter: "creature") and hands it to
///   <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> (the same
///   declarative <c>pump_target</c> verb the JSON ability schema uses, here on
///   the instant/sorcery cast path). The target request + CR 608.2b
///   illegal-target fizzle come from the shared declarative filter; the −3/−3
///   registers as a Layer-7c <c>PumpUntilEndOfTurnEffect</c> that expires at EOT
///   (CR 514.2 / CR 611). Same removal shape as <see cref="LastGaspFactory"/> /
///   <see cref="DisfigureFactory"/>.
///
/// In PROD the cast path binds the oracle text via
/// <see cref="OracleSpellBinder"/> — "Target creature gets -3/-3 until end of
/// turn." matches the negative-pump template
/// (<see cref="Majik.Core.CardData.SpellTemplates.Templates.Counters.DebuffCreatureTemplate"/>);
/// <see cref="BuildDefinition"/> mirrors that bind for the factory-direct seam.
///
/// ## Cycling {2} (CR 702.29) and Madness {3}{B} (CR 702.35) — intrinsic
/// Cycling is handled by the engine's keyword-cycling surface (the discard-draw
/// activated ability bound from the printed "Cycling {2}" line); Madness {3}{B}
/// is catalogued in <see cref="Majik.Core.Keywords.MadnessCatalog"/> ("Ichor
/// Slick" = {3}{B}) and fires via the central discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> — including on the cycling
/// discard (CR 702.35e: a card with both madness and cycling discarded to its
/// own cycling ability still goes to exile and may be cast for its madness
/// cost). Neither needs factory code; the body alone is supplied here.
/// </summary>
[CardName("Ichor Slick")]
public static class IchorSlickFactory
{
    public const string CardName = "Ichor Slick";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>CardDef DSL — card shape only. The -3/-3 SpellDefinition lives
    /// in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "target creature gets -3/-3 until end of turn"
    /// <see cref="SpellDefinition"/> declaratively (the <c>pump_target</c> verb
    /// with negative deltas on a <c>creature</c> target filter). CR 608.2b
    /// illegal-target fizzle is enforced by the shared filter.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new PumpTargetEffectDef
                {
                    Power = -3,
                    Toughness = -3,
                    TargetFilter = "creature",
                },
            });
}
