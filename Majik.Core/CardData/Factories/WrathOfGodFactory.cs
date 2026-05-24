using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wrath of God (Limited Edition Alpha and many
/// reprints, {2}{W}{W}) and its functional reprints.
///
/// Sorcery. Oracle text:
///   "Destroy all creatures. They can't be regenerated."
///
/// ## Functional reprints served here
/// <list type="bullet">
///   <item><b>Wrath of God</b> — {2}{W}{W}, Limited Edition Alpha.</item>
///   <item><b>Damnation</b> — {2}{B}{B}, Planar Chaos. Black functional
///     reprint with the same resolve body (CR 701.7 sweep, no regen,
///     no indestructible bypass at v1).</item>
/// </list>
/// The two printed names are surfaced on the <see cref="NamedCardFactory"/>
/// dispatcher via the two <c>[CardName]</c> attributes below; the source
/// generator routes both names through <see cref="Create(Player, string)"/>
/// so the produced sorcery carries the right printed name + cost per
/// reprint.
///
/// ## Implementation
///
/// Sorcery shape only on the dispatcher path; the resolve effect is
/// built on demand via <see cref="BuildResolveEffect"/>. The effect
/// iterates every player supplied by the caller-provided list (typically
/// <c>Game.Players</c>), snapshots each player's battlefield, and routes
/// every <see cref="Creature"/> to its owner's graveyard via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/>. Snapshotting up
/// front avoids "collection modified" on the in-place zone mutation.
///
/// Distinct from <see cref="Majik.Core.CardData.SpellTemplates.Templates.Destroy.DestroyAllCreaturesTemplate"/>
/// because the template's <c>Rehydrate</c> only sees the caster's
/// battlefield (CR 701.7 — destroy applies to ALL creatures, regardless
/// of controller). The factory carries the multi-player sweep locally.
///
/// ## v1 simplifications
/// - "They can't be regenerated" is a no-op rider — the engine doesn't
///   honour CR 701.15 (Regenerate) yet, so destruction is unconditional
///   regardless of the clause. Same gap as
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Destroy.DestroyAllCreaturesTemplate"/>.
/// - <see cref="Majik.Core.Abilities.Keywords.KeywordType.Indestructible"/>
///   bypass is also lossy at v1 — <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   doesn't yet consult indestructibility (CR 702.12). Same gap as the
///   sweep template.
/// </summary>
[CardName("Wrath of God")]
[CardName("Damnation")]
public static class WrathOfGodFactory
{
    public const string CardName = "Wrath of God";
    public const string PrintedManaCost = "{2}{W}{W}";

    /// <summary>Printed name for the <b>Damnation</b> reprint (Planar Chaos).</summary>
    public const string DamnationCardName = "Damnation";

    /// <summary>Printed mana cost for the <b>Damnation</b> reprint.</summary>
    public const string DamnationPrintedManaCost = "{2}{B}{B}";

    /// <summary>
    /// Build a Wrath of God sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// effect via <see cref="BuildResolveEffect"/>. Equivalent to
    /// <c>Create(owner, "Wrath of God")</c>; kept so callers that don't
    /// care about the reprint variant don't need to spell the name.
    /// </summary>
    public static Sorcery Create(Player owner) => Create(owner, CardName);

    /// <summary>
    /// Build the sorcery for the requested printed name. Supports the
    /// canonical <c>"Wrath of God"</c> and the functional reprint
    /// <c>"Damnation"</c> (different cost, same resolve body). Any other
    /// name is rejected — the source-generated dispatcher routes only
    /// declared <c>[CardName]</c>s here.
    /// </summary>
    public static Sorcery Create(Player owner, string cardName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardName);

        var (printed, cost) = cardName switch
        {
            CardName => (CardName, PrintedManaCost),
            DamnationCardName => (DamnationCardName, DamnationPrintedManaCost),
            _ => throw new ArgumentException(
                $"WrathOfGodFactory does not serve card name '{cardName}'.",
                nameof(cardName)),
        };

        var card = new Sorcery(printed, cost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Wrath of God's resolve effect — destroy every
    /// <see cref="Creature"/> on every supplied player's battlefield.
    /// Each creature is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/>. The
    /// "can't be regenerated" rider is lossy at v1 (no regen plumbing).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep (matches the existing sweep template).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"{CardName} / {DamnationCardName}: destroy all creatures (no regen).", () =>
            {
                // Snapshot every battlefield up front — MoveToGraveyard
                // mutates the source zone in place.
                foreach (var pl in allPlayers)
                {
                    var creatures = pl.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .ToList();
                    foreach (var c in creatures)
                    {
                        OracleSpellBinder.MoveToGraveyard(c);
                    }
                }
            }),
        };
    }
}
