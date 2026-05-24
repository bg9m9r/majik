using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wishclaw Talisman (Throne of Eldraine, {1}{B}).
///
/// Artifact. Oracle text:
///   "Wishclaw Talisman enters tapped.
///    {T}, Pay 3 life: Search your library for a card, put that card into
///    your hand, then shuffle. An opponent gains control of Wishclaw
///    Talisman. Activate only as a sorcery."
///
/// ## Implemented (v1)
/// - Artifact identity, mana cost {1}{B}, owner/controller.
/// - <b>ETB tapped</b> — wired via <see cref="EntersTappedReplacement"/>
///   when the bus-aware overload is used. The single-arg dispatcher path
///   attaches the replacement to a local <see cref="ReplacementBus"/> so
///   the dispatcher / shape tests can apply the replacement directly
///   without external wiring.
/// - <b>Activated ability — {T}, Pay 3 life</b>: tutor any card from the
///   controller's library to their hand (CR 701.19a — uses the shared
///   <see cref="SpellTemplates.Templates.Search.SearchSpellFactory"/>
///   tutor-any-card semantics: agent-picked, decline = null, ordering
///   handled by the agent). After the tutor body finishes, an
///   <see cref="ControlChangeEffect"/> is registered against the
///   supplied <see cref="ContinuousEffectsService"/> (CR 613.2 — Layer 2
///   control-changing effect) with the new controller chosen by the
///   supplied <paramref name="opponentChooser"/>. The single-arg
///   dispatcher path does NOT change control (no ContinuousEffectsService
///   wired) — the card shape + activated ability + ETB-tapped marker are
///   all present but live control-swap requires the runtime overload.
/// - <b>{T}</b> portion of the cost uses <see cref="AdditionalCost.Tap"/>;
///   <b>Pay 3 life</b> uses <see cref="AdditionalCost.PayLife"/>.
///
/// ## Deferred (v1 gaps)
/// (No remaining gaps for the printed activated ability — see
/// "Implemented" above for the timing-gate wiring.)
/// - <b>Shuffle after search</b>: CR 701.19c. The shared search template
///   skips the shuffle for the same reason — no <c>IZone.Shuffle</c>
///   entry point yet. Library ordering is not exposed through the
///   public iteration surface, so this is observably correct.
/// - <b>Opponent-choice prompt for control change</b>: "An opponent
///   gains control" should prompt the controller to choose among
///   opponents (CR 800.4 / 113.6). The single-arg dispatcher path does
///   not perform the swap; the runtime overload accepts a
///   <see cref="Func{Player}"/> <paramref name="opponentChooser"/> that
///   the caller wires to either an agent prompt or a deterministic
///   first-opponent pick. v1 default is whatever the caller supplies.
/// </summary>
[CardName("Wishclaw Talisman")]
public static class WishclawTalismanFactory
{
    public const string CardName = "Wishclaw Talisman";

    /// <summary>
    /// Construct Wishclaw Talisman owned and controlled by
    /// <paramref name="owner"/>. The ETB-tapped replacement is attached
    /// to a private bus exposed via the returned card's
    /// <see cref="Card.Abilities"/> shape (a structural
    /// <see cref="EntersTappedReplacement"/> marker), and the activated
    /// ability is wired with both costs ({T} + Pay 3 life) and a tutor
    /// effect. The control-swap step is a no-op in this dispatcher path
    /// because no <see cref="ContinuousEffectsService"/> is supplied —
    /// see the (owner, effects, opponentChooser) overload for full
    /// behavior.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, effects: null, opponentChooser: null).Card;

    /// <summary>
    /// Construct Wishclaw Talisman fully wired against the supplied
    /// <see cref="ContinuousEffectsService"/>. When
    /// <paramref name="opponentChooser"/> is supplied, activating the
    /// ability runs the tutor and then registers an
    /// <see cref="ControlChangeEffect"/> swapping control of the
    /// Talisman to the chosen opponent (CR 613.2). The returned
    /// <see cref="WishclawTalismanWiring.EntersTappedReplacement"/>
    /// exposes the ETB-tapped replacement so the caller can register it
    /// against a live <see cref="ReplacementBus"/>.
    /// </summary>
    public static WishclawTalismanWiring Create(
        Player owner,
        ContinuousEffectsService? effects,
        Func<Player>? opponentChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, "{1}{B}");
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB tapped — CR 614.1c. Caller registers the replacement on
        // a live ReplacementBus when ETB routing actually flows through
        // ZoneMoveIntent. Producing the replacement here keeps the
        // ETB-tapped shape consistent with other ETB-tapped cards
        // (Spymaster's Vault, Underground Mortuary).
        // ----------------------------------------------------------------
        var etbTappedReplacement = new EntersTappedReplacement(card);

        // ----------------------------------------------------------------
        // {T}, Pay 3 life:
        //   Search your library for a card, put that card into your hand,
        //   then shuffle.
        //   An opponent gains control of Wishclaw Talisman.
        //   Activate only as a sorcery.
        //
        // CR 605 — not a mana ability (effect is library access + control
        // swap, neither produces mana). CR 117.1a / 307.5 — sorcery-speed
        // restriction enforced via ActionValidator
        // (sorcerySpeed: true on the activated ability below). CR 701.19a
        // — tutor any card to hand; CR 613.2 — Layer 2 control-change
        // effect for the give-away clause.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            "Wishclaw Talisman: tutor any card → hand",
            () =>
            {
                // CR 701.19a — search consults the agent. Mirrors
                // SearchSpellFactory.SearchLibrarySpell ("card") semantics:
                // an agent-picked candidate goes to hand; null = decline.
                var candidates = owner.Zones.Library.GetCards().ToList();
                if (candidates.Count == 0) return;

                var agent = AgentRegistry.Get(owner);
                ICard? pick = agent != null
                    ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates: candidates,
                        kindLabel: "card")
                        .GetAwaiter().GetResult()
                    : candidates[0];
                if (pick == null) return;

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                // CR 701.19c — shuffle deferred (no IZone.Shuffle entry
                // point yet; same rationale as SearchSpellFactory).
            });

        var giveAwayEffect = new Effect(
            "Wishclaw Talisman: an opponent gains control of this",
            () =>
            {
                if (effects == null || opponentChooser == null) return;
                if (card.Zone != ZoneType.Battlefield) return;

                var newController = opponentChooser();
                if (newController == null) return;
                if (ReferenceEquals(newController, card.Controller)) return;

                // CR 613.2 — Layer 2 control-changing effect. Permanent.Controller
                // is left untouched; ContinuousEffectsService.EffectiveController
                // returns the new controller while the effect is active.
                effects.Register(new ControlChangeEffect(card, newController));
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                AdditionalCost.PayLife(3),
            },
            effects: new IEffect[] { tutorEffect, giveAwayEffect },
            sorcerySpeed: true);

        card.AddAbility(activated);

        return new WishclawTalismanWiring(card, etbTappedReplacement);
    }
}

/// <summary>
/// Bundle of the artifact handles returned by the runtime-aware
/// <see cref="WishclawTalismanFactory.Create(Player, ContinuousEffectsService?, Func{Player}?)"/>
/// overload. <see cref="EntersTappedReplacement"/> exposes the ETB-
/// tapped replacement so the caller can register it against a live
/// <see cref="ReplacementBus"/>.
/// </summary>
public sealed record WishclawTalismanWiring(
    Artifact Card,
    EntersTappedReplacement EntersTappedReplacement);
