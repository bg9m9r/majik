using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vesperlark (Morningtide, {2}{W}).
///
/// Creature — Elemental 2/1. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature leaves the battlefield, return target creature card
///    with power 1 or less from your graveyard to the battlefield.
///    Evoke {1}{W} (You may cast this spell for its evoke cost. If you do,
///    it's sacrificed when it enters.)"
///
/// Vesperlark is the small sibling of <see cref="ReveillarkFactory"/> — the
/// white Lorwyn/Morningtide evoke creature whose leaves-the-battlefield
/// trigger reanimates from the graveyard. The differences from Reveillark:
///   - 2/1 at {2}{W} (vs 4/3 at {4}{W}).
///   - Evoke {1}{W} (vs {5}{W}) — pure-mana alt-cost (CR 702.74).
///   - LTB returns exactly ONE target creature card (a mandatory single
///     target, not Reveillark's "up to two"), filtered to power 1 or less.
///
/// The base shape (name, Creature, Elemental subtype, {2}{W}, 2/1) is
/// materialised from the embedded JSON definition (<c>vesperlark.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="ReveillarkFactory"/>. The keyword markers + the LTB
/// reanimation trigger are layered on here because the JSON
/// <c>AbilityDefinition</c> schema doesn't express Flying / Evoke / a
/// leaves-the-battlefield reanimation.
///
/// ## Implemented (v1)
/// - 2/1 Creature — Elemental at {2}{W}, owner / controller wired.
/// - <b>Flying</b> (CR 702.9) — <see cref="KeywordAbility"/> marker.
/// - <b>Evoke {1}{W}</b> keyword marker via <see cref="KeywordAbility"/>
///   ("Evoke"). The pure-mana evoke alt-cost is announced at cast time via
///   <see cref="Majik.Core.Costs.EvokeAlternativeCost"/>(ManaCost.Parse("{1}{W}"));
///   the printed "when this enters, if its evoke cost was paid, sacrifice it"
///   trigger (CR 702.74b) is attached via <see cref="EvokeFactory.Build"/>
///   (same shape as Reveillark). The evoke sacrifice IS the trigger for the
///   LTB reanimation below.
/// - <b>LTB triggered ability (CR 603.6c / CR 603.10c)</b>: fires whenever
///   Vesperlark moves OUT of the battlefield to any destination (dies,
///   bounce, exile, evoke-sacrifice, flicker all qualify) — filtered via
///   <see cref="CardMovedEvent"/> with <c>FromZone == Battlefield</c> (same
///   shape as <see cref="ReveillarkFactory"/>). On resolution it returns one
///   target creature card with power 1 or less from the controller's
///   graveyard to the battlefield under the controller's control (CR 701.20),
///   reusing the <see cref="Fx.ReturnFromGraveyardToBattlefield"/> plumbing.
///   The single target slot honours an agent-set selection; absent one it
///   deterministically picks the first legal creature card in the
///   controller's graveyard (CR 608.2b — invalid / missing pick is a clean
///   no-op), mirroring Reveillark's first-card fallback.
///
/// ## "power 1 or less" filter (CR 109.3 / CR 208.2)
/// "creature card with power 1 or less" reads the card's defining values
/// while it is in the graveyard (a card in a zone other than the battlefield
/// has only its characteristics-defining values — CR 208.2), so the candidate
/// filter uses <see cref="Creature.BasePower"/> &lt;= 1.
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt
///   before the trigger resolves (same pattern as Reveillark / Gravedigger).
///   The first-card fallback is the dispatcher-path safety net.
/// - <b>"New object" semantics on return (CR 400.7)</b>: v1 reuses the same
///   <see cref="Card"/> instance (same posture as Reveillark / Reanimate).
/// </summary>
[CardName("Vesperlark")]
public static class VesperlarkFactory
{
    public const string CardName = "Vesperlark";
    public const string Slug = "vesperlark";
    public const string EvokeCost = "{1}{W}";

    /// <summary>Maximum power a graveyard creature card may have to be a
    /// legal reanimation target (CR — "power 1 or less").</summary>
    public const int MaxReturnedPower = 1;

    /// <summary>
    /// Construct Vesperlark with no live wiring. Keyword markers + the evoke
    /// sacrifice trigger + the LTB reanimation trigger are attached for shape
    /// observability; the LTB trigger is NOT registered with a
    /// <see cref="TriggerManager"/> and the reanimation move bypasses
    /// <see cref="ZoneService"/> when the effect is invoked manually.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Vesperlark with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the LTB graveyard → battlefield move
    /// routes through <see cref="ZoneService.MoveCard"/> so the reanimated
    /// creature's ETB triggers fire (CR 603.6a).</param>
    /// <param name="triggers">When supplied, the evoke sacrifice trigger + the
    /// LTB reanimation trigger register with the bus so their events land them
    /// on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental subtype, {2}{W}, 2/1). The JSON carries no abilities —
        // the keyword markers + the LTB reanimation are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.9 (Flying), CR 702.74 (Evoke). Attach
        // inline so the NamedCardFactory path matches the data-driven
        // KeywordBinder result (same shape as Reveillark).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // Pure-mana evoke ({1}{W}) — alt-cost announced at cast time via
        // EvokeAlternativeCost(ManaCost.Parse("{1}{W}")). OnResolved flips
        // Creature.EvokeWasPaid; the intervening-if reads it at queue-time
        // (CR 603.4). The sacrifice it produces is itself a
        // leaves-the-battlefield event, feeding the LTB reanimation below.
        // ----------------------------------------------------------------
        var evokeSac = EvokeFactory.Build(card);
        card.AddAbility(evokeSac);
        triggers?.RegisterTriggeredAbility(evokeSac);

        // ----------------------------------------------------------------
        // LTB reanimation trigger — CR 603.6c / CR 603.10c / CR 701.20.
        //   "When this creature leaves the battlefield, return target creature
        //    card with power 1 or less from your graveyard to the
        //    battlefield."
        // Fires whenever Vesperlark moves OUT of Battlefield (any destination
        // — dies / bounce / exile / evoke-sacrifice / flicker), same
        // FromZone == Battlefield shape as Reveillark's LTB.
        // ----------------------------------------------------------------
        TriggeredAbility? ltb = null;

        var ltbEffect = new Effect(
            $"{CardName}: return target creature card with power 1 or less from your graveyard to the battlefield",
            () => ResolveReanimate(card, owner, ltb, zones));

        ltb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                ReferenceEquals(e.Card, card)
                && e.FromZone == ZoneType.Battlefield),
            effects: new IEffect[] { ltbEffect },
            interveningIf: null,
            // CR 603.6d — leaves-the-battlefield abilities look back in time at
            // the game state just before the event; ActiveZones = Battlefield
            // matches Reveillark.
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card with power 1 or less in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: LegalTargets(owner).Cast<object>().ToList()),
            });

        card.AddAbility(ltb);
        triggers?.RegisterTriggeredAbility(ltb);

        return card;
    }

    /// <summary>
    /// Legal reanimation candidates: creature cards with base power 1 or less
    /// in <paramref name="controller"/>'s graveyard (CR 109.3 / CR 208.2 —
    /// power read off the card's defining values in the graveyard).
    /// </summary>
    private static IReadOnlyList<Creature> LegalTargets(Player controller) =>
        controller.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .Where(c => c.BasePower <= MaxReturnedPower)
            .ToList();

    /// <summary>
    /// Shared LTB resolution. Reads the trigger's
    /// <see cref="TriggeredAbility.ChosenTargets"/> (production path); falls
    /// back to the first legal creature card in the controller's graveyard
    /// when none was set (deterministic dispatcher posture — mirrors
    /// <see cref="ReveillarkFactory"/>'s first-card fallback). The pick is
    /// re-validated at resolution (CR 608.2b — still in the controller's
    /// graveyard, still a creature card, still power 1 or less) before being
    /// returned to the battlefield under the controller's control via
    /// <see cref="Fx.ReturnFromGraveyardToBattlefield"/>.
    /// </summary>
    private static void ResolveReanimate(
        Creature vesperlark,
        Player owner,
        TriggeredAbility? ltb,
        ZoneService? zones)
    {
        // CR 110.2 — "your graveyard" is the controller's graveyard. Snapshot
        // the controller as of the event (control-change edge cases).
        var controller = vesperlark.Controller ?? owner;

        Creature? pick = null;

        // 1) Honour agent-set target if present (production path).
        if (ltb != null && ltb.ChosenTargets.Count > 0)
        {
            foreach (var slot in ltb.ChosenTargets)
            {
                foreach (var obj in slot)
                {
                    if (obj is Creature chosen)
                    {
                        pick = chosen;
                        break;
                    }
                }

                if (pick != null) break;
            }
        }

        // 2) Deterministic fallback — first legal creature card in the
        // controller's graveyard (single-arg dispatcher path).
        pick ??= LegalTargets(controller).FirstOrDefault();

        if (pick == null) return;

        // CR 608.2b illegal-on-resolution re-checks:
        //   (a) still in the controller's graveyard,
        //   (b) still a creature card with power 1 or less.
        if (pick.Zone != ZoneType.Graveyard) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(pick)) return;
        if (pick.BasePower > MaxReturnedPower) return;

        // CR 701.20 — graveyard → battlefield under the controller's control.
        // ZoneService path fires the reanimated creature's ETB triggers
        // (CR 603.6a); raw-zone fallback otherwise.
        Fx.ReturnFromGraveyardToBattlefield(pick, controller, zones);
    }
}
