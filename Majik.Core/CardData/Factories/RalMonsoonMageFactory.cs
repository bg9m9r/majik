using Majik.Core.Abilities;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ral, Monsoon Mage // Ral, Leyline Prodigy — Transform
/// DFC front face (Modern Horizons 3 era, {1}{R}).
///
/// Front face — Ral, Monsoon Mage. Legendary Creature — Human Wizard, 1/3.
/// Oracle text:
///   "Instant and sorcery spells you cast cost {1} less to cast.
///    Whenever you cast an instant or sorcery spell during your turn, flip a
///    coin. If you lose the flip, Ral deals 1 damage to you. If you win the
///    flip, you may exile Ral. If you do, return him to the battlefield
///    transformed under his owner's control."
///
/// Back face — Ral, Leyline Prodigy. Legendary Planeswalker — Ral, loyalty 2.
/// The back-face loyalty abilities are NOT modelled by this factory — only the
/// DFC plumbing (front-face shape + the cost-reduction static + the cast-trigger
/// coin flip + the transform flip). This mirrors the
/// <see cref="TamiyoInquisitiveStudentFactory"/> /
/// <see cref="AjaniNacatlPariahFactory"/> posture exactly (a creature front
/// face transforming into a planeswalker back face).
///
/// ## Implemented (v1)
/// - 1/3 Legendary Creature — Human Wizard at {1}{R}, owner / controller set.
/// - <see cref="MdfcState"/> attached, starting on the front face. Winning the
///   coin flip flips it to the back face (Ral, Leyline Prodigy) — same
///   observation surface as Tamiyo / Ajani.
/// - <b>Cost reduction static</b> (CR 117.7): "Instant and sorcery spells you
///   cast cost {1} less to cast." via <see cref="SpellCostReductionAbility"/>,
///   the exact Baral, Chief of Compliance / Goblin Electromancer shape. The
///   reducer is scoped to the controller's battlefield by
///   <see cref="CostReduction.GetEffectiveCost"/>; coloured pips untouched.
/// - <b>Cast-trigger coin flip → damage-or-transform</b> (CR 603.1 trigger +
///   coin flip): a <see cref="TriggeredAbility"/> over
///   <see cref="SpellCastEvent"/> filtered to (controller-cast instant/sorcery,
///   AND it being the controller's own turn). The "during your turn" window is
///   driven from the live <see cref="EventBus"/> — a per-card flag is set true
///   on <see cref="TurnStartedEvent"/> for the controller and false otherwise.
///   On resolution the injected coin source decides: lose ⇒ Ral deals 1 damage
///   to its controller (via <see cref="Player.LoseLife"/>, same route as
///   <see cref="ManaCryptFactory"/>); win ⇒ flip the <see cref="MdfcState"/> to
///   the back face (CR 701.28). The "you may exile Ral" choice on a win is
///   auto-accepted (deterministic v1 — same posture as the Ajani / Tamiyo
///   "you may" deferrals).
///
/// ## Deferred (v1 gaps)
/// - <b>Exile-then-return-transformed.</b> The printed text exiles Ral and
///   returns him transformed (a zone round-trip that resets him as a "new
///   object" — CR 701.28b). v1 flips the MdfcState in place (no exile/return),
///   matching the Ajani / Tamiyo transform posture. A true exile + return would
///   require the same Layer-0 / per-face hot-swap that DFC permanents still
///   lack (see Ajani deferral note).
/// - <b>Back-face loyalty abilities + planeswalker body (deferral #19
///   residual).</b> The CR 711/712 Layer-0 face-replacement seed now in
///   <see cref="Majik.Core.Effects.ContinuousEffectsService"/> swaps in a back
///   face's CREATURE body (Delver, all MID/VOW Werewolves). A PLANESWALKER
///   back is the remaining residual: a creature-front instance can't carry a
///   loyalty body, and Ral, Leyline Prodigy's enters-with-extra-loyalty rider
///   and [+1] / [-2] / [-8] abilities aren't wired. The back face stays
///   shape-only tracked through <see cref="MdfcState.BackFaceName"/> —
///   identical to Ajani, Nacatl Avenger / Tamiyo, Seasoned Scholar. Lower
///   value: the played face is the front (creature).
/// - <b>"You may exile Ral" prompt.</b> A win auto-transforms rather than
///   prompting whether to exile/return. Same deterministic posture as every
///   other v1 "you may" (Sun Titan / Stoneforge Mystic / Ajani).
/// - <b>Full <see cref="DamageDealtEvent"/> route.</b> The 1 damage goes
///   through <see cref="Player.LoseLife"/>; damage-prevention subscribers won't
///   see Ral's ping. Same scope decision as Mana Crypt.
/// </summary>
[CardName("Ral, Monsoon Mage // Ral, Leyline Prodigy")]
public static class RalMonsoonMageFactory
{
    public const string FrontName = "Ral, Monsoon Mage";
    public const string BackName = "Ral, Leyline Prodigy";
    public const string FrontCost = "{1}{R}";

    /// <summary>
    /// Construct Ral with no live wiring (shape / dispatcher path). The
    /// cost-reduction static and the cast-trigger are attached to the card so
    /// structural assertions still see them; the trigger is not registered with
    /// a manager and uses a default <see cref="System.Random.Shared"/>-backed
    /// coin flip. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, coinLoses: null);

    /// <summary>
    /// Construct Ral, Monsoon Mage with optional live wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the cast trigger is registered so
    /// the runtime queues it on qualifying <see cref="SpellCastEvent"/>s.</param>
    /// <param name="eventBus">When supplied, drives the "during your turn"
    /// window — the trigger only fires while it is the controller's own turn,
    /// tracked from <see cref="TurnStartedEvent"/>. When null the window
    /// defaults open (shape path), so the predicate gates purely on the
    /// instant/sorcery-you-cast clause.</param>
    /// <param name="coinLoses">Coin-flip seam — return <c>true</c> to model
    /// "you lose the flip" (1 damage), <c>false</c> to model "you win the flip"
    /// (transform). Defaults to a <see cref="System.Random.Shared"/> 50/50.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        EventBus? eventBus,
        Func<bool>? coinLoses = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: FrontName,
            manaCost: FrontCost,
            power: 1,
            toughness: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 — DFC face tracker. Starts on the front face (Ral, Monsoon
        // Mage); winning the coin flip flips IsBackFace.
        card.MdfcState = new MdfcState(FrontName, BackName);

        // CR 117.7 — "Instant and sorcery spells you cast cost {1} less to
        // cast." Exact Baral / Goblin Electromancer shape; scoped to the
        // controller's battlefield by CostReduction.GetEffectiveCost.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery),
            reduction: (_, _) => 1,
            description: "Instant and sorcery spells you cast cost {1} less to cast."));

        AttachCastCoinFlipTrigger(card, owner, triggers, eventBus, coinLoses);

        return card;
    }

    /// <summary>
    /// "Whenever you cast an instant or sorcery spell during your turn, flip a
    /// coin. If you lose the flip, Ral deals 1 damage to you. If you win the
    /// flip, you may exile Ral. If you do, return him to the battlefield
    /// transformed under his owner's control." (CR 603.1 + coin flip +
    /// CR 701.28 transform.)
    /// </summary>
    private static void AttachCastCoinFlipTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        EventBus? eventBus,
        Func<bool>? coinLoses)
    {
        // Default flip: System.Random.Shared 50/50 — true means "you lose".
        // Same seam shape as Mana Crypt (no GameRandom threaded through the
        // factory dispatch path yet).
        var flipLoses = coinLoses ?? (() => System.Random.Shared.Next(2) == 0);

        // "during your turn" window. Boxed in a single-element array so the bus
        // subscription and the trigger predicate share one mutable cell. When a
        // live bus drives it, the flag tracks whether the most recent
        // TurnStartedEvent belongs to the controller. With no bus (shape path)
        // the window defaults open so the predicate gates purely on the
        // instant/sorcery-you-cast clause.
        var isYourTurn = new bool[1];
        isYourTurn[0] = eventBus == null;
        eventBus?.Subscribe<TurnStartedEvent>(e =>
            isYourTurn[0] = ReferenceEquals(e.Player, card.Controller ?? owner));

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)) return false;
            if (!e.Spell.Card.HasType(CardType.Instant)
                && !e.Spell.Card.HasType(CardType.Sorcery)) return false;
            return isYourTurn[0];
        });

        var resolveEffect = new Effect(
            $"{FrontName}: cast instant/sorcery → coin flip (lose: 1 damage; win: transform)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.MdfcState == null || card.MdfcState.IsBackFace) return;

                var controller = card.Controller ?? owner;
                if (flipLoses())
                {
                    // "If you lose the flip, Ral deals 1 damage to you."
                    controller.LoseLife(1);
                }
                else
                {
                    // "If you win the flip, you may exile Ral. If you do, return
                    // him transformed." v1 auto-accepts the may + flips the face
                    // in place (Ajani / Tamiyo posture); exile/return round-trip
                    // deferred. CR 701.28.
                    card.MdfcState.Transform();
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { resolveEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }
}
