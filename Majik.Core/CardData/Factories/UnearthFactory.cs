using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unearth (Coldsnap, {B}).
///
/// Sorcery. Oracle text (Coldsnap/Scryfall canonical — no haste/EOT rider):
///   "Return target creature card with mana value 3 or less from your
///    graveyard to the battlefield."
///   Cycling {2} ({2}, Discard this card: Draw a card.)
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}, black.
/// - <b>Resolve effect</b> via <see cref="BuildResolveEffect"/>:
///     1. Scan caster's graveyard for creature cards with printed mana
///        value (CR 202.3b) ≤ 3. v1 deterministic: first match wins
///        (same posture as <see cref="ReanimateFactory"/> / <see cref="ExhumeFactory"/>).
///     2. Move that card graveyard → caster's battlefield under caster's
///        control (CR 701.20). Routes through <see cref="ZoneService.MoveCard"/>
///        when supplied so ETB triggers fire (CR 603.6a).
///     3. No life-loss rider (Unearth does not have Reanimate's life-loss).
///     4. No haste/EOT exile (Coldsnap oracle has neither).
/// - <b>Cycling {2}</b> (CR 702.29) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with a {2} generic cost.
///   Attaches the <see cref="ActivatedAbility"/> +
///   <see cref="KeywordAbility"/> "Cycling" marker. Discard + draw CR 702.29a;
///   <see cref="CardCycledEvent"/> published when an event bus is supplied
///   (CR 702.29d).
///
/// ## MV filter (CR 202.3b)
/// The printed mana cost's total generic+coloured value is compared ≤ 3.
/// X costs count as 0 at time of check (creatures typically have no X in
/// their printed cost so this is moot for the target pool).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + cycling, no event bus (shape tests).
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling publishes
///   <see cref="CardCycledEvent"/> for CR 702.29d subscribers.
/// </summary>
[CardName("Unearth")]
public static class UnearthFactory
{
    public const string CardName = "Unearth";
    public const string PrintedManaCost = "{B}";
    public const string CyclingCost = "{2}";

    /// <summary>Maximum mana value of the target creature card (CR 202.3b).</summary>
    public const int MaxManaValue = 3;

    /// <summary>
    /// Construct Unearth with no event bus. Cycling activated ability is
    /// attached; activation is gated to the controller's hand by
    /// <see cref="DiscardSelfCost.CanPay"/>. No <see cref="CardCycledEvent"/>
    /// publication (shape-only path).
    /// </summary>
    public static Sorcery Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Unearth. When <paramref name="eventBus"/> is supplied the
    /// cycling resolve body publishes <see cref="CardCycledEvent"/> so
    /// CR 702.29d "Whenever a player cycles a card" triggers fire.
    /// </summary>
    public static Sorcery Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cycling {2} — CR 702.29. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.29a) and the
        // CardCycledEvent publish (CR 702.29d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }

    /// <summary>
    /// Build Unearth's resolve effect — return target creature card with
    /// mana value ≤ <see cref="MaxManaValue"/> from the caster's graveyard
    /// to the battlefield under the caster's control. No life loss, no
    /// haste/EOT exile (Coldsnap oracle).
    /// </summary>
    /// <param name="caster">Spell controller — source graveyard + destination
    /// battlefield.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers fire on the reanimated creature (CR 603.6a).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: return target creature card with mana value ≤ {MaxManaValue} from caster's graveyard to battlefield",
                () => Resolve(caster, zoneService)),
        };
    }

    /// <summary>
    /// Resolution helper — picks the first creature card in the caster's
    /// graveyard whose mana value (CR 202.3b) is ≤ <see cref="MaxManaValue"/>
    /// and moves it to the caster's battlefield under the caster's control
    /// (CR 701.20). No-op if no legal target exists (CR 117.x).
    /// </summary>
    private static void Resolve(Player caster, ZoneService? zoneService)
    {
        // CR 202.3b — mana value = sum of the printed mana cost's
        // individual mana symbols (X = 0 by default).
        var pick = caster.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.ManaCostValue.TotalValue <= MaxManaValue);

        if (pick == null) return; // CR 117.x — no legal target: no-op

        // CR 701.20 — graveyard → caster's battlefield. Fx routes through
        // ZoneService when supplied so ETB triggers fire (CR 603.6a).
        Fx.ReturnFromGraveyardToBattlefield(pick, caster, zoneService);
    }
}
