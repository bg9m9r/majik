using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Terrarion (Time Spiral, {1}).
///
/// Artifact. Oracle text:
///   "This artifact enters tapped.
///    {2}, {T}, Sacrifice this artifact: Add two mana in any combination of colors.
///    When this artifact is put into a graveyard from the battlefield, draw a card."
///
/// Same family as <see cref="ChromaticStarFactory"/> — a sacrifice-for-mana
/// activated ability plus a leaves-the-battlefield "draw a card" trigger. The
/// three differences from the Star:
///   1. An <b>enters-tapped replacement</b> (CR 614.1c) — crib of
///      <see cref="SunscorchedDesertFactory"/>'s wiring.
///   2. The activation cost includes <b>{2}</b> (the Star is free to activate).
///   3. It produces <b>two</b> mana in any combination of colours (the Star adds
///      one of any colour).
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional
///   "this artifact enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>, mirroring Sunscorched Desert. The
///   single-arg dispatcher path omits the replacement when no
///   <see cref="ReplacementBus"/> is available — Terrarion enters untapped
///   on shape-only paths, matching every other always-tapped factory's
///   posture.
/// - <b>{2}, {T}, Sacrifice this: Add two mana in any combination of colors</b>
///   — fifteen <see cref="ManaAbility"/> instances, one per two-pip WUBRG
///   multiset (the five mono-colour doubles WW/UU/BB/RR/GG plus the ten
///   distinct pairs WU/WB/…/RG). Same modal fan-out posture as
///   <see cref="CascadingCataractsFactory"/>'s "five mana in any combination"
///   (which ships representative slots; Terrarion's two-pip space is small
///   enough to enumerate exhaustively). Each uses the
///   (source, controller, manaGenerated, canActivateCheck, additionalCostPayer)
///   overload:
///     - <c>canActivateCheck</c> = <c>!IsTapped AND Zone == Battlefield AND
///       controller can pay {2}</c> (gates the once-only activation and the
///       {2} affordability — mirrors <see cref="ChromaticSphereFactory"/>'s
///       {1} gate).
///     - <c>additionalCostPayer</c> pays {2} from the pool (CR 601.2h) and
///       performs the sacrifice (CR 701.16) inline — battlefield → owner's
///       graveyard. The bus-driven LTB trigger fires from
///       <see cref="ZoneManager"/>'s <see cref="CardMovedEvent"/>.
///   CR 605.1 — these are still mana abilities (they don't use the stack);
///   the {2} extra cost is paid as part of activation, atomically with {T}.
/// - <b>LTB draw trigger</b> — <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnDies"/> (CR 700.4 / 603.6 — Battlefield →
///   Graveyard self-move; <c>OnDies</c> is permanent-agnostic despite the
///   creature-flavoured name). <c>activeZones = {Battlefield, Graveyard}</c>
///   so the trigger still matches whether the engine evaluates the zone gate
///   just-before or just-after the move. Resolves to
///   <see cref="Fx.DrawCards"/>(controller, 1). Mirrors Chromatic Star.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: mirrors Chromatic Star / Lotus
///   Petal — the engine's generic <see cref="Majik.Core.Costs.AdditionalCost"/>
///   sacrifice path is a no-op stub today, so the activation closure performs
///   the zone move directly. When the broader sacrifice-cost plumbing lands,
///   the inline move-to-graveyard can drop; the LTB trigger will still fire
///   via the centralised <see cref="CardMovedEvent"/> publication.
/// - <b>{2} auto-fixing</b>: activation requires {2} to already be in the
///   mana pool; the engine doesn't auto-tap other sources to feed the cost.
///   Same posture as Chromatic Sphere / Cascading Cataracts / the filter-land
///   cycle and every other additional-mana-cost activated ability.
/// - <b>Single modal-colour mana ability</b>: "two mana in any combination of
///   colors" is bound as fifteen separate <see cref="ManaAbility"/> instances —
///   the bot's source-picker selects the slot matching the colours it needs.
///   A future modal-mana-ability primitive would collapse these into one
///   (same future note Cascading Cataracts carries).
/// </summary>
[CardName("Terrarion")]
public static class TerrarionFactory
{
    public const string CardName = "Terrarion";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// All fifteen two-pip WUBRG multisets — the five mono-colour doubles
    /// plus the ten distinct colour pairs. "Two mana in any combination of
    /// colors" enumerated exhaustively (CR 605.1 modal mana ability).
    /// </summary>
    private static readonly string[] TwoColorCombos =
    {
        "WW", "UU", "BB", "RR", "GG",
        "WU", "WB", "WR", "WG",
        "UB", "UR", "UG",
        "BR", "BG",
        "RG",
    };

    /// <summary>
    /// Construct Terrarion with no live trigger / replacement wiring. The LTB
    /// trigger is attached to <see cref="Card.Abilities"/> for shape
    /// inspection; the enters-tapped replacement is omitted (no
    /// <see cref="ReplacementBus"/> available), so Terrarion enters untapped
    /// on this path — matching every other always-tapped factory's shape-only
    /// posture.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Terrarion with optional trigger / replacement wiring. When
    /// <paramref name="triggers"/> is supplied the LTB draw trigger is
    /// registered so the bus surfaces it automatically. When
    /// <paramref name="replacements"/> is supplied the enters-tapped
    /// restriction is registered so Terrarion enters tapped (CR 614.1c).
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Enters-tapped replacement — CR 614.1c.
        //   "This artifact enters tapped."
        // Unconditional; no gate. Shape-only path (no ReplacementBus) skips
        // registration and Terrarion enters untapped, matching Sunscorched
        // Desert / Creeping Tar Pit's posture.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(card));
        }

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice Terrarion: Add two mana in any combination of
        // colors.
        // Fifteen ManaAbility instances (one per two-pip WUBRG multiset) —
        // same modal fan-out as Chromatic Star (one colour) and Cascading
        // Cataracts (five-mana combination). Each is gated on:
        //   (1) Terrarion is untapped,
        //   (2) Terrarion is still on the battlefield, AND
        //   (3) the controller can pay {2} from their mana pool.
        // The additionalCostPayer pays {2} (CR 601.2h) and sacrifices
        // Terrarion (CR 701.16) inline; the LTB trigger fires off the
        // ZoneManager-published CardMovedEvent.
        // ----------------------------------------------------------------
        var twoGeneric = ManaCost.Parse("2");

        foreach (var combo in TwoColorCombos)
        {
            card.AddAbility(new ManaAbility(
                source: card,
                controller: owner,
                manaGenerated: ManaCost.Parse(combo),
                canActivateCheck: () => !card.IsTapped
                                        && card.Zone == ZoneType.Battlefield
                                        && owner.ManaPool.CanPay(twoGeneric),
                additionalCostPayer: payer => PayCostAndSacrifice(card, owner, payer, twoGeneric)));
        }

        // ----------------------------------------------------------------
        // When Terrarion is put into a graveyard from the battlefield, draw
        // a card. CR 700.4 / 603.6 — battlefield → graveyard self-move.
        // Triggers.OnDies despite the name is shape-generic over
        // CardMovedEvent (FromZone=Battlefield → ToZone=Graveyard for the
        // source card). activeZones={Battlefield, Graveyard} so the gate
        // matches whether the engine evaluates pre- or post-move (mirrors
        // Chromatic Star / Wurmcoil Engine).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card on LTB battlefield->graveyard",
            () => Fx.DrawCards(owner, 1));

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }

    /// <summary>
    /// Pay the non-{T} portion of the activation cost:
    /// <list type="number">
    ///   <item>Pay {2} from the activator's mana pool (CR 601.2h).</item>
    ///   <item>Sacrifice Terrarion — battlefield → owner's graveyard
    ///     (CR 701.16). Idempotent against double-execution (the
    ///     canActivateCheck gate makes a sibling-ability re-entry within the
    ///     same step unreachable in practice).</item>
    /// </list>
    /// The LTB draw trigger fires off the ZoneManager-published
    /// <see cref="CardMovedEvent"/> the sacrifice produces.
    /// </summary>
    private static void PayCostAndSacrifice(
        Artifact card, Player owner, Player payer, ManaCost twoGeneric)
    {
        // CR 601.2h — pay the {2} activation cost.
        payer.PayMana(twoGeneric);

        // CR 701.16 — sacrifice: controller moves Terrarion from the
        // battlefield to its owner's graveyard. Idempotent.
        if (card.Zone != ZoneType.Battlefield) return;

        var controller = card.Controller ?? owner;
        controller.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
