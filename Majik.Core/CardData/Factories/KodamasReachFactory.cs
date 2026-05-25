using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kodama's Reach (Champions of Kamigawa, {2}{G}).
///
/// Sorcery — Arcane. Oracle text:
///   "Search your library for up to two basic land cards, reveal those
///    cards, put one onto the battlefield tapped and the other into your
///    hand, then shuffle."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{G}, subtype <see cref="CardSubtype.Arcane"/>
///   stamped via the CardDef DSL so the printed Arcane line (CR 205.3k)
///   is observable and so future Splice onto Arcane riders
///   (<see cref="DesperateRitualFactory"/> et al.) can target this card
///   when it's on the stack.
/// - Resolve effect: identical to <see cref="CultivateFactory"/> —
///   delegates to
///   <see cref="SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell"/>.
///   Kodama's Reach is a functional reprint of Cultivate; the only
///   printed difference is the Arcane subtype.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: same gap as
///   <see cref="CultivateFactory"/>.
/// - <b>Spliced rider</b>: any other Arcane spell may have Splice onto
///   Arcane riders that copy their effect onto this spell on cast
///   (CR 702.46). The Splice primitive currently requires an explicit
///   <c>SpliceOntoArcaneCost</c> wired into <c>SpellCastFlow</c>'s
///   additional-cost list — see <see cref="DesperateRitualFactory"/>.
///   Once SpliceFlow auto-detects Arcane targets at cast time the
///   subtype on Kodama's Reach lights up that surface automatically.
/// </summary>
[CardName("Kodama's Reach")]
public static class KodamasReachFactory
{
    public const string CardName = "Kodama's Reach";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>CardDef DSL — card shape only. Stamps the Arcane subtype
    /// (CR 205.3k) so Splice onto Arcane can target this card on the
    /// stack. Resolve-time tutor body is built via
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost)
        .WithSubtype(CardSubtype.Arcane);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Kodama's Reach uses on
    /// resolution. Delegates to
    /// <see cref="SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell"/>
    /// — functional reprint of Cultivate.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell(caster, CardName);
    }
}
