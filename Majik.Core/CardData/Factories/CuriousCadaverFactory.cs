using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Curious Cadaver (Murders at Karlov Manor,
/// {2}{U}{B}).
///
/// Creature — Zombie Detective 3/1. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "Flying
///    When you sacrifice a Clue, return this card from your graveyard to your
///    hand."
///
/// Curious Cadaver composes shapes already in the engine:
/// - <b>Flying (CR 702.9)</b> — a <see cref="KeywordAbility"/> marker, the same
///   posture as <see cref="FaerieMastermindFactory"/> / every other flier.
/// - <b>Graveyard-resident sacrifice-a-Clue recursion trigger (CR 603.1 /
///   CR 603.6d / CR 701.16)</b> — a <see cref="TriggeredAbility"/> that is
///   active <em>only while Curious Cadaver is in its owner's graveyard</em>
///   (<c>activeZones = {Graveyard}</c>, the same graveyard-resident posture as
///   <see cref="SqueeGoblinNabobFactory"/>). It fires on the dedicated
///   <see cref="PermanentSacrificedEvent"/> (the sacrifice-detection surface
///   published by the bus-aware sacrifice paths) when BOTH:
///   <list type="bullet">
///     <item>the <see cref="PermanentSacrificedEvent.SacrificingPlayer"/> is
///       Curious Cadaver's owner — "<b>you</b> sacrifice" (CR 109.5); and</item>
///     <item>the sacrificed card has the <see cref="CardSubtype.Clue"/> subtype
///       (Clue is an artifact subtype, CR 205.3m) — "a <b>Clue</b>".</item>
///   </list>
///   On resolution the resident zone is re-checked (CR 603.6d) and, if Curious
///   Cadaver is still in its owner's graveyard, it moves Graveyard → Hand. When
///   a <see cref="ZoneService"/> is wired the move goes through
///   <see cref="ZoneService.MoveCard"/> so zone-change events fire; otherwise a
///   raw zone move is performed. The return is NOT optional (printed "return
///   this card", no "may"), so there is no agent prompt — distinct from Squee.
///
/// The base card shape (name / Creature / Zombie Detective subtypes / {2}{U}{B}
/// cost / 3/1 body) is materialised from the embedded JSON definition
/// (<c>curious-cadaver.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the Flying keyword and the
/// graveyard-resident trigger are layered on here because the declarative
/// <c>AbilityDefinition</c> schema exposes neither a "when you sacrifice a
/// [type]" trigger that functions from the graveyard nor a graveyard-resident
/// "return this card to your hand" effect (the same gap documented on
/// <see cref="SqueeGoblinNabobFactory"/>). All underlying engine primitives
/// already exist; this factory composes them.
///
/// ## Implemented (v1)
/// - <b>Creature shape</b> Zombie Detective 3/1 at printed cost {2}{U}{B}.
/// - <b>Flying (CR 702.9)</b> — keyword marker.
/// - <b>"When you sacrifice a Clue, return this card from your graveyard to
///   your hand." (CR 603.1 / CR 603.6d / CR 701.16)</b> — graveyard-resident
///   <see cref="TriggeredAbility"/> on a <see cref="PermanentSacrificedEvent"/>
///   predicate scoped to (owner sacrificed) ∧ (sacrificed card is a Clue). The
///   resolution body re-checks that Curious Cadaver is in its owner's graveyard
///   so a stale activation (already returned / exiled) is no-op-shaped.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Curious Cadaver")]
public static class CuriousCadaverFactory
{
    public const string CardName = "Curious Cadaver";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "curious-cadaver";

    public const string PrintedManaCost = "{2}{U}{B}";
    public const int Power = 3;
    public const int Toughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Curious Cadaver with no live runtime wiring. Flying is attached
    /// and the graveyard-resident sacrifice-a-Clue trigger is attached for
    /// shape inspection, but the trigger is not registered with a
    /// <see cref="TriggerManager"/> (fire it manually in tests) and the return
    /// uses a raw zone move. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Curious Cadaver with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone service used by the recursion trigger to
    /// move Curious Cadaver from graveyard to hand so zone-change events fire.
    /// May be null — a raw zone move is performed instead.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident trigger
    /// registration (CR 603.6d). May be null — the trigger is attached to the
    /// card for shape but not registered with the bus.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Zombie
        // Detective subtypes, {2}{U}{B}, 3/1). The JSON carries no abilities —
        // Flying + the sacrifice-a-Clue trigger are layered on below.
        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 702.9 — Flying. Block restrictions enforced by CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // "When you sacrifice a Clue, return this card from your graveyard to
        //  your hand." — CR 603.1 / CR 603.6d / CR 701.16.
        // Active only while Curious Cadaver is in its owner's Graveyard
        // (activeZones = {Graveyard}). Fires on PermanentSacrificedEvent when
        // the sacrificing player is the owner (CR 109.5 — "you") AND the
        // sacrificed card is a Clue (CR 205.3m — Clue is an artifact subtype).
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return from graveyard to hand (sacrifice-a-Clue trigger)",
            () =>
            {
                // CR 603.6d — re-check zone at resolution. If Curious Cadaver
                // has left the graveyard since the trigger was put on the
                // stack, do nothing.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!ReferenceEquals(card.Owner, owner)) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                if (zoneService != null)
                {
                    // ZoneService.MoveCard fires zone-change events (CR 603.6a)
                    // so portal/log subscribers see the recursion.
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Hand, owner);
                }
                else
                {
                    // Raw zone move — no zone-change event published.
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
            });

        var sacrificeClueCondition = new EventTriggerCondition<PermanentSacrificedEvent>(
            (e, _) =>
                // "you sacrifice" — the sacrificing player must be the owner
                // (CR 109.5). Fires on the controller's own sacrifice only.
                ReferenceEquals(e.SacrificingPlayer, owner)
                // "a Clue" — Clue is an artifact subtype (CR 205.3m). Read off
                // the sacrificed card (already in the graveyard by the time the
                // event publishes, CR 701.16a — subtype membership is a printed
                // characteristic, not zone-dependent).
                && e.SacrificedCard.HasSubtype(CardSubtype.Clue));

        var recursionTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: sacrificeClueCondition,
            effects: new IEffect[] { returnEffect },
            // Functions only from the graveyard (CR 603.6d) — it returns
            // *this card* from the graveyard.
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(recursionTrigger);
        triggers?.RegisterTriggeredAbility(recursionTrigger);

        return card;
    }
}
