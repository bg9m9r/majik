using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sea Gate Wreckage (Battle for Zendikar).
///
/// Land. Oracle text:
///   "Sea Gate Wreckage enters tapped.
///    {T}: Add {C}.
///    {2}, {T}: Draw a card. Activate only if you have no cards in hand."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtype, no supertype).
/// - <b>Unconditional ETB-tapped (CR 614.1c)</b> — registered as an
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Single-arg dispatcher path omits the
///   replacement (mirrors every other always-tapped factory).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{2}, {T}: Draw a card. Activate only if you have no cards in
///   hand.</b> — <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost({2}), AdditionalCost.Tap(self)]</c>. The
///   activation-only restriction is enforced via a closure that gates
///   the ability on <c>controller.Zones.Hand.Count == 0</c>; v1 exposes
///   the gate as <see cref="HasNoCardsInHand"/> for activator / bot
///   policy probing. Resolution routes a single draw through
///   <see cref="Fx.DrawCards"/> so any future
///   <see cref="DrawCardIntent"/> replacements (Dredge, etc.) participate.
///
/// ## Deferred (v1 gaps)
/// - The "Activate only if you have no cards in hand" gate is exposed
///   via a public predicate but is not yet wired into the
///   ActivatedAbility's CanActivate gate — the engine's
///   <see cref="ActivatedAbility"/> does not yet have a generic legality
///   closure (same posture as Magmatic Channeler's "four or more
///   instant/sorcery cards in graveyard" gate, which surfaces the check
///   for bot probes pending the activation-legality surface landing).
///   The empty-hand sampling and bot/test policy hooks are in place;
///   when the activation-legality surface ships the predicate will be
///   wired into <see cref="ActivatedAbility"/> directly.
/// </summary>
[CardName("Sea Gate Wreckage")]
public static class SeaGateWreckageFactory
{
    public const string CardName = "Sea Gate Wreckage";

    /// <summary>
    /// Construct Sea Gate Wreckage with no
    /// <see cref="ReplacementBus"/> wired. The ETB-tapped replacement is
    /// omitted (shape-only); both mana abilities + the draw ability
    /// remain attached.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Sea Gate Wreckage.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Replacement bus for the always-enters-tapped
    /// restriction (CR 614.1c). May be null.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Plain Land — no subtype, no supertype.
        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "Sea Gate Wreckage enters
        // tapped." Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {C} — vanilla mana ability (CR 605.1).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {2}, {T}: Draw a card. Activate only if you have no cards in
        // hand.
        //
        // CR 602 — ordinary activated ability. Cost = {2} mana + tap
        // self. Resolution draws a single card through Fx.DrawCards so
        // Dredge-style DrawCardIntent replacements participate.
        //
        // The empty-hand gate is exposed via HasNoCardsInHand for
        // activator / bot policy probing; the ActivatedAbility surface
        // does not yet expose a generic CanActivate hook (same posture
        // as Magmatic Channeler's delirium-style gate).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () =>
            {
                var controller = land.Controller ?? owner;
                Fx.DrawCards(controller, 1);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { drawEffect }));

        return land;
    }

    /// <summary>
    /// CR 602.5 — Sea Gate Wreckage's "Activate only if you have no
    /// cards in hand" gate. Reads <see cref="Card.Controller"/> live so
    /// control-change effects re-point the scan. Returns false when the
    /// controller is not yet assigned.
    /// </summary>
    public static bool HasNoCardsInHand(Land wreckage)
    {
        ArgumentNullException.ThrowIfNull(wreckage);
        var controller = wreckage.Controller ?? wreckage.Owner;
        if (controller is null) return false;
        return controller.Zones.Hand.Count == 0;
    }
}
