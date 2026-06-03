using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boon Satyr (Theros, {1}{G}{G}).
///
/// Enchantment Creature — Satyr 4/2. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Flash
///    Bestow {3}{G}{G} (If you cast this card for its bestow cost, it's an
///    Aura spell with enchant creature. It becomes a creature again if it's
///    not attached.)
///    Enchanted creature gets +4/+2."
///
/// The cleanest Theros bestow enchantment-creature: Flash plus a flat +4/+2
/// bestow boost, no extra granted keyword. Used as the unblocking card for the
/// new <see cref="BestowKeyword"/> primitive (CR 702.103).
///
/// ## Shape source
/// Card identity (name, {1}{G}{G}, 4/2, Enchantment Creature — Satyr, green
/// colour indicator) is loaded from
/// <c>Majik.Core/CardData/Cards/boon-satyr.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The Flash keyword + bestow effects are
/// attached in code below.
///
/// ## Implemented
/// - <b>Flash</b> (CR 702.8): a <see cref="KeywordAbility"/> marker the cast
///   flow consults to allow casting at instant speed. Mirrors
///   <see cref="BrineborneCutthroatFactory"/>'s Flash posture.
/// - <b>Bestow</b> (CR 702.103): wired through
///   <see cref="BestowKeyword.RegisterBestowEffects"/> when a live
///   <see cref="ContinuousEffectsService"/> is supplied —
///     * Layer 4 "not a creature while attached as an Aura" / "becomes a
///       creature again when unattached" (CR 702.103e / 702.103f), and
///     * Layer 7c "Enchanted creature gets +4/+2" (CR 613).
///   <see cref="BuildBestowSpellDefinition"/> exposes the bestow-cast Aura
///   spell shape (single creature target, auto-attach on resolve), and
///   <see cref="BestowCost"/> the alternative {3}{G}{G} cost (CR 702.103b).
/// </summary>
[CardName("Boon Satyr")]
public static class BoonSatyrFactory
{
    public const string CardName = "Boon Satyr";
    public const string Slug = "boon-satyr";

    /// <summary>CR 702.103b — the alternative bestow cost.</summary>
    public const string BestowCost = "{3}{G}{G}";

    /// <summary>The +P component of "Enchanted creature gets +4/+2".</summary>
    public const int BestowPowerBoost = 4;

    /// <summary>The +T component of "Enchanted creature gets +4/+2".</summary>
    public const int BestowToughnessBoost = 2;

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Flash\n" +
        "Bestow {3}{G}{G} (If you cast this card for its bestow cost, it's an " +
        "Aura spell with enchant creature. It becomes a creature again if " +
        "it's not attached.)\n" +
        "Enchanted creature gets +4/+2.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Boon Satyr with card identity + Flash marker only (no live
    /// continuous effects). Suitable for shape / dispatch tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct Boon Satyr. When <paramref name="continuousEffects"/> is
    /// supplied, the bestow effects (Layer-4 type strip + Layer-7c +4/+2
    /// boost) are registered via <see cref="BestowKeyword.RegisterBestowEffects"/>;
    /// they stay inert until the card is on the battlefield AND attached to a
    /// creature (cast for its bestow cost, CR 702.103b/e).
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.8 — Flash. KeywordAbility marker; the cast flow consults it
        // to allow casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        if (continuousEffects != null)
        {
            // CR 702.103 — bestow: Layer-4 "not a creature while attached" +
            // Layer-7c "+4/+2 to enchanted creature".
            BestowKeyword.RegisterBestowEffects(
                card,
                continuousEffects,
                power: BestowPowerBoost,
                toughness: BestowToughnessBoost);
        }

        return card;
    }

    /// <summary>
    /// CR 702.103b — build the bestow-cast Aura <see cref="SpellDefinition"/>
    /// for Boon Satyr cast for its bestow cost: single creature target,
    /// auto-attach on resolution.
    /// </summary>
    public static SpellDefinition BuildBestowSpellDefinition(
        Permanent card,
        IEnumerable<Permanent> battlefield) =>
        BestowKeyword.BuildBestowSpellDefinition(card, battlefield);

    /// <summary>CR 702.103b — the alternative bestow cost as a
    /// <see cref="ManaCost"/>.</summary>
    public static ManaCost ParseBestowCost() =>
        BestowKeyword.ParseBestowCost(BestowCost);
}
