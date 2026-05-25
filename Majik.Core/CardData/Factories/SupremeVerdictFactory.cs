using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Supreme Verdict (Return to Ravnica,
/// {1}{W}{W}{U}).
///
/// Sorcery. Oracle text:
///   "This spell can't be countered.
///    Destroy all creatures."
///
/// ## Shape
/// Sorcery, mana cost {1}{W}{W}{U} (Bant — colourless / white-white /
/// blue printed pips). Carries a
/// <see cref="KeywordAbility"/>("Uncounterable") marker on the card so
/// <see cref="Majik.Core.Game.SpellCastFlow"/> stamps
/// <see cref="Majik.Core.Spells.ISpell.CannotBeCountered"/> on the
/// resolving spell at cast time (CR 701.5b) — the same wiring used by
/// Emrakul, the Aeons Torn and the rest of the "this spell can't be
/// countered" cycle. <see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>
/// then vetoes any counter-attempt against the resolving spell.
///
/// ## Resolve
/// Multi-player sweep over every supplied player's battlefield —
/// every <see cref="Creature"/> is routed to its owner's graveyard via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/>. Distinct
/// from <see cref="WrathOfGodFactory"/>: Wrath / Damnation pass
/// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
/// because their printed text says "They can't be regenerated."; Supreme
/// Verdict's printed text does NOT carry that rider, so an active
/// regeneration shield (CR 701.15) MUST be honoured in the normal way.
/// Indestructible (CR 702.12b) still cancels the destroy on either path.
///
/// Snapshotting each battlefield up front avoids "collection modified"
/// on the in-place zone mutation (same posture as
/// <see cref="WrathOfGodFactory.BuildResolveEffect"/>).
///
/// Distinct from
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Destroy.DestroyAllCreaturesTemplate"/>
/// — that template's <c>Rehydrate</c> only sees the caster's
/// battlefield. Supreme Verdict reads "Destroy all creatures", which CR
/// 701.7 applies to every creature regardless of controller; the factory
/// carries the multi-player sweep locally.
/// </summary>
[CardName("Supreme Verdict")]
public static class SupremeVerdictFactory
{
    public const string CardName = "Supreme Verdict";
    public const string PrintedManaCost = "{1}{W}{W}{U}";

    /// <summary>
    /// Keyword tag stamped on the card for cast-time discoverability.
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> scans
    /// <see cref="ICard.Abilities"/> for a
    /// <see cref="KeywordAbility"/> whose keyword is "Uncounterable"
    /// (case-insensitive) and lifts the flag onto the resolving spell
    /// (CR 701.5b).
    /// </summary>
    public const string UncounterableMarker = "Uncounterable";

    /// <summary>
    /// Build a Supreme Verdict sorcery owned and controlled by
    /// <paramref name="owner"/>. The cast-time uncounterable marker is
    /// attached here; the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 701.5b — "this spell can't be countered". KeywordAbility
        // marker form keeps the surface symmetric with the rest of the
        // cycle (Emrakul, Apostle's Blessing parity is on a separate
        // path); SpellCastFlow.HasUncounterableMarker reads it at cast
        // time and stamps Spell.CannotBeCountered on the resolving
        // spell so OracleSpellBinder.RemoveFromStack vetoes counters.
        card.AddAbility(new KeywordAbility(UncounterableMarker, card, owner));

        return card;
    }

    /// <summary>
    /// Build Supreme Verdict's resolve effect — destroy every
    /// <see cref="Creature"/> on every supplied player's battlefield.
    /// Each creature is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> so
    /// regeneration (CR 701.15) is honoured — Supreme Verdict's text
    /// does NOT include the "can't be regenerated" rider (contrast
    /// <see cref="WrathOfGodFactory"/>).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"{CardName}: destroy all creatures.", () =>
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
                        // CR 701.7 — Destroy. Regeneration (CR 701.15)
                        // is honoured normally (no printed "can't be
                        // regenerated" rider); Indestructible
                        // (CR 702.12b) still cancels the destroy via
                        // MoveToGraveyard's Destroy-reason gate.
                        OracleSpellBinder.MoveToGraveyard(c, Majik.Core.Zones.ZoneMoveReason.Destroy);
                    }
                }
            }),
        };
    }
}
