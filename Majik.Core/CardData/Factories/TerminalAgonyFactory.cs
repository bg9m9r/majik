using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Terminal Agony (Modern Horizons 3, {2}{B}{R}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Destroy target creature.
///    Madness {B}{R} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// The plain unconditional creature kill — same body as
/// <see cref="DarkWitheringFactory"/> minus the nonblack filter.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}{R}, owner / controller (CardDef DSL).
/// - <b>Destroy target creature</b> — <see cref="BuildDefinition"/> declares a
///   single <see cref="DestroyTargetEffectDef"/>(TargetFilter: "creature") and
///   hands it to <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>
///   (the shared declarative <c>destroy_target</c> verb). The target request +
///   CR 608.2b illegal-target fizzle come from the declarative filter; the
///   destroy goes through <c>MoveToGraveyard</c> with
///   <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
///   indestructible (CR 702.12) / regeneration (CR 701.15) shields are honoured
///   (Terminal Agony does NOT print "can't be regenerated"). In PROD the cast
///   path binds the oracle text via <see cref="OracleSpellBinder"/>
///   (DestroyCreature template).
///
/// ## Madness {B}{R} (CR 702.35) — intrinsic, NOT wired here
/// "Terminal Agony" = {B}{R} is catalogued in
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/>; the central discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> routes the discarded card
/// to exile + offers it for its madness cost. No factory code needed.
/// </summary>
[CardName("Terminal Agony")]
public static class TerminalAgonyFactory
{
    public const string CardName = "Terminal Agony";
    public const string PrintedManaCost = "{2}{B}{R}";

    /// <summary>CardDef DSL — card shape only. The destroy SpellDefinition lives
    /// in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target creature" <see cref="SpellDefinition"/>
    /// declaratively (the <c>destroy_target</c> verb on a <c>creature</c>
    /// filter). CR 608.2b illegal-target fizzle is enforced by the filter;
    /// CR 701.7 destroy honours indestructible / regeneration.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new DestroyTargetEffectDef { TargetFilter = "creature" },
            });
}
