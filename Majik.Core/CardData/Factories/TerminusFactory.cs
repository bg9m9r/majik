using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Terminus (Avacyn Restored, {4}{W}{W}).
///
/// Sorcery. Oracle text:
///   "Put all creatures on the bottom of their owners' libraries.
///    Miracle {W} (You may cast this card for its miracle cost when
///    you draw it if it's the first card you drew this turn.)"
///
/// ## Implemented (v1)
///
/// - <b>Sorcery</b> at <c>{4}{W}{W}</c> (MV 6), owner/controller wired.
/// - <b>Tuck-to-bottom sweep</b>: every <see cref="Creature"/> on every
///   supplied player's battlefield is moved to the BOTTOM of its
///   <em>owner's</em> library. This is the tuck-to-bottom analogue of the
///   <see cref="WrathOfGodFactory"/> destroy-all-creatures sweep — same
///   snapshot-then-iterate shape, but the destination is the owner's
///   library bottom rather than the owner's graveyard.
/// - <b>Miracle {W} (CR 702.94)</b> — wired as a real alternative cost.
///   The factory stamps the printed miracle cost via
///   <see cref="Card.SetMiracleCost"/>; the draw hook in
///   <see cref="Majik.Core.Game.TurnDriver"/> opens the one-shot window
///   (<see cref="Card.RuntimeMiracleCost"/>) when Terminus is the first
///   card its controller drew this turn (CR 702.94b), and the card may then
///   be cast from hand for {W} via
///   <see cref="Majik.Core.Costs.MiracleAlternativeCost"/> (surfaced to the
///   bot by <see cref="Majik.Core.Players.Agents.MiracleAltCostProbe"/>).
///   The <see cref="KeywordAbility"/>("Miracle") marker is retained for
///   keyword scanners — same posture as <see cref="ReforgeTheSoulFactory"/>
///   and <see cref="BonfireOfTheDamnedFactory"/>.
///
/// ## CR notes
///
/// - <b>"Put all creatures on the bottom of their owners' libraries."</b>
///   A creature is a permanent moved from the battlefield to a library;
///   the destination library is its OWNER's, not its controller's
///   (CR 400.3 / CR 400.7e — when an object leaves the battlefield it goes
///   to a zone owned by its owner). So a creature an opponent has stolen
///   returns to its true owner's library.
/// - <b>"on the bottom"</b>: the library is index-0 == top (the draw step
///   pulls <c>Library.GetCards().First()</c>); appending via
///   <see cref="IZone.AddCard"/> places the card at the end, i.e. the
///   bottom. No shuffle, no order choice between the tucked creatures
///   beyond iteration order (which is unobservable to opponents once
///   tucked face-down under the rest of the library).
/// - This is NOT a destroy: indestructible, regeneration, and "dies"
///   triggers do not apply — the creatures simply change zones (CR 701.x
///   "put … into … library" is a zone change, not destruction).
/// </summary>
[CardName("Terminus")]
public static class TerminusFactory
{
    public const string CardName = "Terminus";
    public const string PrintedManaCost = "{4}{W}{W}";
    public const string MiracleCostText = "{W}";

    /// <summary>
    /// Build a Terminus sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// effect via <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.94 — Miracle. Keyword marker (combat/keyword scanners) +
        // the printed miracle cost stamp the draw hook reads to open the
        // first-card-drawn-this-turn window (see MiracleAlternativeCost).
        card.AddAbility(new KeywordAbility("Miracle", card, owner));
        card.SetMiracleCost(Majik.Core.ValueObjects.ManaCost.Parse(MiracleCostText));

        return card;
    }

    /// <summary>
    /// Build Terminus's resolve effect — put every <see cref="Creature"/>
    /// on every supplied player's battlefield on the bottom of its
    /// <em>owner's</em> library. Single <see cref="IEffect"/> entry so
    /// callers can splice it into a
    /// <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only scope.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"{CardName}: put all creatures on the bottom of their owners' libraries.", () =>
            {
                // Snapshot every battlefield up front — the zone moves
                // below mutate the source list in place.
                foreach (var pl in allPlayers)
                {
                    var creatures = pl.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .ToList();

                    foreach (var c in creatures)
                    {
                        // Leave the battlefield from wherever it currently
                        // sits (controller's battlefield in this scope).
                        pl.Zones.Battlefield.RemoveCard(c);

                        // CR 400.3 / 400.7 — a permanent that leaves the
                        // battlefield goes to a zone owned by its OWNER.
                        // AddCard appends to the end of the library, i.e.
                        // the bottom (index 0 == top by draw convention).
                        var owner = c.Owner ?? pl;
                        owner.Zones.Library.AddCard(c);
                        c.SetZone(ZoneType.Library);
                    }
                }
            }),
        };
    }
}
