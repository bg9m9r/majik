using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vaultborn Tyrant (The Lost Caverns of Ixalan,
/// {5}{G}{G}).
///
/// Creature — Dinosaur 6/6. Oracle text (verified against Scryfall):
///   "Trample
///    Whenever this creature or another creature you control with power 4 or
///    greater enters, you gain 3 life and draw a card.
///    When this creature dies, if it's not a token, create a token that's a
///    copy of it, except it's an artifact in addition to its other types."
///
/// ## Implemented (v1)
///
/// - <b>Identity / shape</b>: Creature — Dinosaur, {5}{G}{G}, 6/6, green.
///   Materialised from the embedded JSON definition (<c>vaultborn-tyrant.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON <c>keywords</c> array
///   carries <b>Trample (CR 702.19)</b> as a <see cref="KeywordAbility"/>
///   marker the combat pipeline reads — no engine work needed for the keyword.
///
/// - <b>Power-4+ enters → gain 3 life + draw a card (CR 603.6a / 603.2)</b>:
///   "Whenever this creature OR another creature you control with power 4 or
///    greater enters …" — note it fires on its OWN ETB too (this card is a
///    6/6, power ≥ 4), so the predicate intentionally does NOT exclude self
///    (contrast Mentor of the Meek's "another"). The predicate gates on
///   (a) entering the battlefield, (b) card type Creature, (c) controller =
///   Vaultborn's controller (CR 109.5 — "you control"), and (d) effective
///   power ≥ 4 read at ETB (CR 603.6d — the look-back game state). Resolution
///   gains 3 life via <see cref="Fx.GainLife"/> and draws a card via
///   <see cref="Fx.DrawCards"/> (routes the draw through the replacement bus +
///   empty-library SBA flag, CR 120.3 / 704.5b).
///
/// - <b>Dies (nontoken) → artifact self-copy token (CR 700.4 / 706.2 / 111)</b>:
///   "When this creature dies, if it's not a token, create a token that's a
///    copy of it, except it's an artifact in addition to its other types."
///   Wired via <see cref="Triggers.OnDies"/>. The intervening "if it's not a
///   token" (CR 603.4) is enforced both structurally (a token built by this
///   factory has no dies-trigger registered — see below) and defensively at
///   resolution. The copy is a fresh Vaultborn Tyrant rebuilt through this
///   same factory (so it carries Vaultborn's OWN power-4+ ETB and dies
///   triggers — CR 706.2 copies abilities), flagged
///   <see cref="Permanent.IsToken"/>, additively stamped
///   <see cref="CardType.Artifact"/> (CR 706.2 "except" clause), and put onto
///   the battlefield under the dead Vaultborn's controller.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both triggers attached for
///   structural / dispatcher inspection; not bus-registered.
/// - <see cref="Create(Player, ZoneService, TriggerManager)"/> — fully wired.
///   Triggers register for bus-driven firing; the death-copy routes through
///   the <see cref="ZoneService"/> so the copy's ETB fires (its own power-4+
///   trigger sees itself enter — it's a 6/6) and its triggers bind.
///
/// ## Notes / v1 fidelity
/// - <b>Self-copy is rebuilt from the printed definition</b>, not a true
///   CR 706.2 snapshot of the dying Vaultborn's current characteristics
///   (counters / external pumps on the original are not carried). Aligns with
///   the v1 lossy posture used by <see cref="ScuteSwarmFactory"/>'s self-copy.
///   The "+ artifact" rider IS faithfully applied via
///   <see cref="Card.AddCardType"/>.
/// - <b>Token guard</b>: the death-copy branch only registers its dies trigger
///   on the NONTOKEN build path. A copy minted on death is a token, so its own
///   "if it's not a token" clause never fires — no infinite token chain.
/// </summary>
[CardName("Vaultborn Tyrant")]
public static class VaultbornTyrantFactory
{
    public const string CardName = "Vaultborn Tyrant";
    public const string Slug = "vaultborn-tyrant";
    public const string PrintedManaCost = "{5}{G}{G}";

    /// <summary>CR — "power 4 or greater" enters threshold.</summary>
    public const int PowerThreshold = 4;

    /// <summary>Life gained on the power-4+ ETB trigger.</summary>
    public const int LifeGain = 3;

    /// <summary>
    /// Construct Vaultborn Tyrant with no live wiring. Both triggers are
    /// attached to the card shape for dispatcher / structural inspection but
    /// not registered with any <see cref="TriggerManager"/>, and the death
    /// copy has no <see cref="ZoneService"/> to route through. Suitable for
    /// shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null, isToken: false);

    /// <summary>
    /// Construct Vaultborn Tyrant with optional runtime services. When
    /// <paramref name="triggers"/> is supplied both the power-4+ ETB trigger
    /// and (for the nontoken build) the dies trigger register for bus-driven
    /// firing. When <paramref name="zones"/> is supplied the death-copy token
    /// routes through <see cref="ZoneService"/> so its battlefield entry
    /// publishes <see cref="CardMovedEvent"/> (downstream ETB listeners — and
    /// the copy's own 6/6 power-4+ ETB trigger — fire).
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zones, TriggerManager? triggers) =>
        Create(owner, zones, triggers, isToken: false);

    /// <summary>
    /// Core builder. <paramref name="isToken"/> distinguishes the printed card
    /// (false) from a death-copy token (true): the dies trigger is only wired
    /// on the NONTOKEN path, enforcing the printed "if it's not a token" clause
    /// structurally (CR 603.4) so a death-copy never spawns a further copy.
    /// </summary>
    private static Creature Create(
        Player owner, ZoneService? zones, TriggerManager? triggers, bool isToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Card shape (Creature — Dinosaur, {5}{G}{G}, 6/6, green, Trample
        // keyword marker) comes from the embedded JSON definition.
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);

        // ----------------------------------------------------------------
        // ETB power-4+ trigger — CR 603.6a / 603.2.
        //   "Whenever this creature OR another creature you control with
        //    power 4 or greater enters, you gain 3 life and draw a card."
        // Fires on its OWN ETB too (it's a 6/6), so NO self-exclusion. The
        // power read is the entering creature's effective power at ETB
        // (CR 603.6d look-back state) via Creature.Power.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (!ReferenceEquals(e.Card.Controller, card.Controller ?? owner)) return false;
            if (e.Card is not Creature entering) return false;
            return entering.Power >= PowerThreshold;
        });

        var etbEffect = new Effect(
            $"{CardName}: gain {LifeGain} life and draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 119.3 — gain 3 life.
                Fx.GainLife(controller, LifeGain);
                // CR 121.1 — draw a card (routes through the replacement bus +
                // empty-library SBA flag, CR 120.3 / 704.5b).
                Fx.DrawCards(controller, 1);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Dies trigger — CR 700.4 / 706.2 / 111.
        //   "When this creature dies, if it's not a token, create a token
        //    that's a copy of it, except it's an artifact in addition to its
        //    other types."
        // Only wired on the NONTOKEN path — a death-copy is a token, so its
        // own "if it's not a token" clause never fires (CR 603.4), preventing
        // an infinite token chain. The copy is rebuilt through this factory
        // (carrying Vaultborn's OWN abilities — CR 706.2) and additively
        // stamped Artifact.
        // ----------------------------------------------------------------
        if (!isToken)
        {
            var diesEffect = new Effect(
                $"{CardName}: create an artifact token copy of it",
                () => CreateArtifactCopyToken(card.Controller ?? owner, zones, triggers));

            var diesTrigger = new TriggeredAbility(
                source: card,
                controller: owner,
                condition: Triggers.OnDies(card),
                effects: new IEffect[] { diesEffect },
                // CR 603.10 — leaves-the-battlefield ability; it must be able
                // to fire as the source moves off the battlefield, so the
                // graveyard is an active zone alongside the battlefield.
                activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

            card.AddAbility(diesTrigger);
            triggers?.RegisterTriggeredAbility(diesTrigger);
        }

        return card;
    }

    /// <summary>
    /// CR 706.2 — create a token that's a copy of Vaultborn Tyrant, "except
    /// it's an artifact in addition to its other types." Rebuilt through this
    /// factory (token path — no dies trigger) so the copy carries Vaultborn's
    /// power-4+ ETB trigger but cannot itself spawn a further copy. Flagged
    /// <see cref="Permanent.IsToken"/>, summoning-sick (CR 302.6), additively
    /// stamped <see cref="CardType.Artifact"/>, and put onto the battlefield
    /// under <paramref name="controller"/>'s control. When a live
    /// <see cref="ZoneService"/> is supplied the entry publishes
    /// <see cref="CardMovedEvent"/> (so the copy's own 6/6 power-4+ ETB trigger
    /// fires) and a live <see cref="TriggerManager"/> binds the copy's trigger.
    /// </summary>
    private static Creature CreateArtifactCopyToken(
        Player controller, ZoneService? zones, TriggerManager? triggers)
    {
        var copy = Create(controller, zones, triggers, isToken: true);
        copy.IsToken = true;                 // CR 111.1 — it's a token.
        copy.HasSummoningSickness = true;    // CR 302.6 — entered this turn.

        // CR 706.2 "except" clause — artifact in addition to its other types.
        copy.AddCardType(CardType.Artifact);

        // CR 111.6 — tokens enter the battlefield directly. Use the
        // sentinel-library pattern ZoneService.MoveCardTo validates against so
        // CardMovedEvent fires for downstream ETB listeners (incl. the copy's
        // own power-4+ ETB trigger — it's a 6/6).
        copy.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(copy);
        if (zones != null)
        {
            zones.MoveCardTo(copy, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(copy);
            copy.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(copy);
        }

        // CR 603.6a — bind the copy so its power-4+ ETB trigger observes future
        // creature ETBs (no-op without a live TriggerManager).
        triggers?.BindCard(copy);

        return copy;
    }
}
