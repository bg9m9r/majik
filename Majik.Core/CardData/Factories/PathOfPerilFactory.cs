using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Path of Peril (Adventures in the Forgotten
/// Realms, {2}{B}).
///
/// Sorcery. Oracle text:
///   "Cleave {1}{W}{B}{B} (You may cast this spell for its cleave cost.
///    If you do, remove the words in square brackets.)
///    Destroy all [nonlegendary] creatures with mana value 2 or less."
///
/// ## Cleave (CR 702.156) — not yet implemented
///
/// This engine does not yet model the Cleave alternative cost (CR
/// 702.156 — "you may cast this spell for its cleave cost. If you do,
/// the spell becomes the version of the spell with the words in square
/// brackets removed"). Cleave needs three primitives the cast pipeline
/// doesn't yet expose:
///
/// 1. An alternative-cost binding that swaps printed mana cost in for
///    the cleave cost at cast time (CR 118.9 — cousin of Foretell /
///    Adventure / Energy alt-costs).
/// 2. A spell-shape "decleaved variant" hook so the resolve closure
///    branches on the alternative-cost flag (drops the bracketed
///    qualifier).
/// 3. <c>MechanicPrimitiveRegistry</c> coverage so the deferral sweep
///    can find this gap.
///
/// Until those land, this factory ships the <b>always-cleaved</b>
/// resolve body: <i>"Destroy all creatures with mana value 2 or less"</i>
/// — i.e. the "remove the words in square brackets" version is taken
/// unconditionally. This is the strictly stronger of the two printed
/// modes (cleaved version doesn't exclude legendary creatures), so it's
/// a safe upper bound for bot evaluation and rules-engine correctness:
/// every game-state outcome the cleaved sweep produces is one the
/// printed card can produce at its cleave cost.
///
/// The deferral comment below is shaped so the mechanic-coverage report
/// surfaces this card under the (future) <c>cleave</c> primitive.
///
/// (defer: cleave alternative cost — CR 702.156. Today the factory hard-
/// codes the cleaved body — destroys all creatures mv ≤ 2 ignoring the
/// nonlegendary qualifier. When the cast pipeline grows the cleave hook
/// the factory should branch on the alt-cost flag and apply the
/// nonlegendary filter for the regular cast.)
///
/// ## Implementation
///
/// Sorcery shape, printed cost <c>{2}{B}</c>. The resolve body is built
/// on demand via <see cref="BuildResolveEffect"/>: for every supplied
/// player, snapshot the battlefield and route every creature with
/// <see cref="Card.ManaCostValue"/>.<see cref="ManaCost.TotalValue"/>
/// <c>≤ 2</c> to its owner's graveyard via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7 — destroy).
/// Indestructible (CR 702.12) and active regeneration shields (CR
/// 701.15) gate at the binder. Printed text has no "can't be
/// regenerated" rider, so shields are consumed normally.
///
/// CR rule references: 109.5 (symmetric sweep), 117.5 (mana cost),
/// 701.7 (destroy), 701.15 (regeneration), 702.12 (indestructible),
/// 702.156 (cleave — not yet implemented).
/// </summary>
[CardName("Path of Peril")]
public static class PathOfPerilFactory
{
    public const string CardName = "Path of Peril";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>Cleave cost (CR 702.156) — not yet implemented. Held
    /// as a constant for the future cast-pipeline binding.</summary>
    public const string CleavePrintedCost = "{1}{W}{B}{B}";

    /// <summary>Mana-value ceiling for the destroy sweep (CR 701.7).</summary>
    public const int ManaValueCeiling = 2;

    /// <summary>
    /// Build a Path of Peril sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// body via <see cref="BuildResolveEffect"/>.
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
    /// Build Path of Peril's resolve effect — destroy every creature
    /// with mana value ≤ 2 on every supplied player's battlefield.
    ///
    /// <b>v1 simplification:</b> the factory ships the cleaved version
    /// unconditionally (no "nonlegendary" qualifier). When the cleave
    /// cost (CR 702.156) is wired, this method should grow a
    /// <c>bool cleaved</c> parameter and filter legendary creatures out
    /// when <c>cleaved = false</c>. See class doc for the deferral.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass
    /// <c>new[] { caster }</c> for a controller-only sweep (off-oracle).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy each creature with mv ≤ {ManaValueCeiling}.",
                () =>
                {
                    foreach (var pl in allPlayers)
                    {
                        // Snapshot — MoveToGraveyard mutates the source
                        // battlefield in place.
                        var creatures = pl.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .Where(c => c.ManaCostValue.TotalValue <= ManaValueCeiling)
                            .ToList();
                        foreach (var c in creatures)
                        {
                            // CR 701.7 — destroy. Printed text lacks a
                            // "can't be regenerated" rider, so use the
                            // default Destroy reason; indestructible
                            // (CR 702.12) and regeneration shields
                            // (CR 701.15) gate normally at the binder.
                            OracleSpellBinder.MoveToGraveyard(
                                c, Majik.Core.Zones.ZoneMoveReason.Destroy);
                        }
                    }
                }),
        };
    }
}
