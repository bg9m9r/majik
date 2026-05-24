using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Meltdown (Urza's Destiny, {X}{R}).
///
/// Sorcery. Oracle text:
///   "Destroy each artifact with mana value X or less."
///
/// ## Implementation
///
/// Card shape only on the dispatcher path; the resolve effect is built
/// on demand via <see cref="BuildResolveEffect"/>. The effect iterates
/// every player supplied by the caller-provided list (typically
/// <c>Game.Players</c>), snapshots each player's battlefield, and routes
/// every <see cref="Card"/> on it that <c>HasType(CardType.Artifact)</c>
/// AND whose <see cref="Card.ManaCostValue"/>'s <c>TotalValue ≤ X</c> to
/// its owner's graveyard via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7). A
/// <see cref="HashSet{Card}"/> de-dupes the victim pile so a token that
/// somehow surfaces under multiple players' battlefield iterators
/// (shouldn't, but cheap belt-and-braces) only resolves once. Mirrors
/// the multi-player-iteration shape of
/// <see cref="WrathOfGodFactory.BuildResolveEffect"/> with the
/// Pernicious-Deed-style <c>mv ≤ X</c> predicate.
///
/// X provenance: callers pass the resolved X (the printed pip on the
/// stack) directly into <see cref="BuildResolveEffect"/>; the engine
/// has no per-cast X ledger yet (same v1 simplification as Pernicious
/// Deed / Engineered Explosives). The single-arg dispatcher path
/// produces card shape only — no resolve effect is wired, since X is
/// unknown until cast.
///
/// ## v1 simplifications
/// - <see cref="Majik.Core.Abilities.Keywords.KeywordType.Indestructible"/>
///   bypass is lossy — <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   doesn't yet consult indestructibility (CR 702.12). Same gap as
///   <see cref="SlaughterPactFactory"/> and the rest of the destroy
///   family.
/// - "Can't be regenerated" is not a printed rider on Meltdown; CR
///   701.15 (Regenerate) is therefore moot here.
/// </summary>
public static class MeltdownFactory
{
    public const string CardName = "Meltdown";
    public const string PrintedManaCost = "{X}{R}";

    /// <summary>
    /// Build a Meltdown sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// effect via <see cref="BuildResolveEffect"/> once X is known.
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
    /// Build Meltdown's resolve effect — destroy every artifact on every
    /// supplied player's battlefield whose mana value is ≤
    /// <paramref name="x"/>. Each victim is routed to its owner's
    /// graveyard via <see cref="OracleSpellBinder.MoveToGraveyard"/>
    /// (CR 701.7); HashSet-dedupe guards against double-resolution if
    /// the same card somehow surfaces twice in the iterator.
    /// </summary>
    /// <param name="caster">The Meltdown spell's controller. Reserved
    /// for parity with the factory family; the printed effect has no
    /// controller-only scoping, so the resolve currently doesn't gate on
    /// it (kept in the signature for future indestructible / hexproof
    /// riders that might).</param>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep.</param>
    /// <param name="x">The resolved X value for this Meltdown cast.
    /// Cards with <c>ManaCostValue.TotalValue ≤ x</c> are destroyed.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        int x)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy each artifact with mv ≤ {x}.",
                () =>
                {
                    // Snapshot every battlefield up front — MoveToGraveyard
                    // mutates the source zone in place. HashSet dedupe so
                    // the same card never gets routed twice if the same
                    // player surfaces in `allPlayers` more than once.
                    var victims = new HashSet<Card>(ReferenceEqualityComparer.Instance);
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards()
                                            .OfType<Card>()
                                            .Where(c => c.HasType(CardType.Artifact))
                                            .Where(c => c.ManaCostValue.TotalValue <= x)
                                            .ToList())
                        {
                            victims.Add(c);
                        }
                    }

                    foreach (var v in victims)
                    {
                        OracleSpellBinder.MoveToGraveyard(v);
                    }
                }),
        };
    }
}
