using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magmatic Channeler (Core Set 2021, {1}{R}).
///
/// Creature — Human Shaman 1/2. Oracle text:
///   "{2}{R}, {T}: Look at the top four cards of your library. You may
///    reveal a creature or instant card from among them and put it into
///    your hand. Put the rest on the bottom of your library in any order.
///    Activate only if there are four or more instant and/or sorcery cards
///    in your graveyard."
///
/// ## Implemented (v1)
///
/// - 1/2 Creature — Human Shaman at {1}{R}; owner / controller wired.
///   Subtypes <see cref="CardSubtype.Human"/> + <see cref="CardSubtype.Shaman"/>
///   (CR 205.3m — same tribe as Young Pyromancer).
/// - <b>Activated ability (CR 602.1)</b>: <see cref="ActivatedAbility"/>
///   with two costs and one effect.
///   <list type="bullet">
///     <item><see cref="ManaCostCost"/>("{2}{R}") — the mana portion of
///       the activation cost (CR 601.2f / CR 117.7).</item>
///     <item><see cref="AdditionalCost.Tap"/> — the {T} symbol
///       (CR 118.12). Combined with the mana cost the activation is
///       atomic — if either component can't be paid the activation
///       fails up front (CR 601.2g).</item>
///   </list>
/// - <b>"Look at the top four cards of your library. You may reveal a
///   creature or instant card from among them and put it into your hand.
///   Put the rest on the bottom of your library in any order."</b>
///   (CR 701.20). Resolve closure:
///     1. Snapshot up to <see cref="PeekCount"/> cards from the top of
///        the controller's library (fewer if the library is short — same
///        posture as Amped Raptor's exile-top-four / Curator of
///        Mysteries' look). Empty library is a clean no-op.
///     2. Filter the snapshot to <see cref="CardType.Creature"/> OR
///        <see cref="CardType.Instant"/> — the eligible reveal pool.
///        Sorceries are excluded by the printed wording, distinct from
///        the activation gate which counts instants + sorceries.
///     3. Ask the controller's registered <see cref="IPlayerAgent"/>
///        (via <see cref="AgentRegistry"/>) to pick one eligible card
///        via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> — the
///        printed "you may" maps to the agent returning <c>null</c> to
///        decline. The pre-agent default is to take the first eligible
///        card (matches every other look-and-pick factory's deterministic
///        fallback).
///     4. Move the pick Library → Hand via raw zone manipulation. The
///        remaining peeked cards are moved to the bottom of the library
///        in their snapshot order (v1: preserves the original top-to-
///        bottom order, controller's "in any order" choice is the
///        identity for the deterministic path; a future agent prompt
///        for re-ordering can plug in alongside the pick).
/// - <b>"Activate only if there are four or more instant and/or sorcery
///   cards in your graveyard"</b> (CR 602.5b — activation restriction).
///   <see cref="ActivatedAbility"/> does not yet expose the
///   <see cref="Majik.Core.Abilities.ManaAbility"/>-style
///   <c>canActivateCheck</c> hook for non-mana activations (the legacy
///   surface only covers <see cref="ManaAbility"/> — see ChromaticStar /
///   MoxOpal / NobleHierarch precedent). Until the
///   <c>IActivatedAbility.CanActivate</c> predicate ships, the gate is
///   enforced two ways:
///     - At <em>action-enumeration time</em>: the bot's
///       <c>ActivatedAbilityPolicy</c> / agent's legal-action probe
///       can consult <see cref="CanActivateGraveyardGate"/> (exposed
///       static) — same wire-up pattern as
///       <see cref="LurrusOfTheDreamDenFactory"/>'s once-per-turn ledger.
///     - At <em>resolve time</em>: the effect closure re-checks the
///       graveyard threshold and short-circuits cleanly when the rule
///       was violated. CR 602.5b says the cost is still paid (CR 117.x
///       — the activation that bypassed the gate still resolves but
///       with no body), so the {2}{R} + {T} payment is NOT refunded;
///       the effect simply does nothing. This matches the
///       <see cref="LilianaOfTheVeilFactory"/> -6 deferred body shape
///       — the cost was paid, the body is a no-op.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Activated ability
///   is attached for shape observability; resolve closure runs the full
///   look-and-pick logic against raw zones. Suitable for dispatcher /
///   structural tests.
///
/// ## Deferred (v1 gaps)
///
/// - <b><see cref="ActivatedAbility.CanActivate"/> hook</b>: the
///   action-validator pipeline does not yet consult an activation
///   predicate on <see cref="IActivatedAbility"/>. Magmatic Channeler's
///   graveyard threshold is exposed as a static predicate
///   (<see cref="CanActivateGraveyardGate"/>) so callers / policies
///   can gate enumeration; once <see cref="IActivatedAbility.CanActivate"/>
///   ships the predicate is the natural single attachment site (same
///   posture <see cref="ManaAbility"/>'s <c>canActivateCheck</c>
///   already supports for the mana family).
/// - <b>"In any order" agent prompt for re-bottoming</b>: v1 preserves
///   the snapshot order when moving the remainder to the bottom of the
///   library. A real "in any order" agent prompt is the same shape as
///   <see cref="ScryAction.ScryDecision"/>'s ordering field — wire when
///   the agent surface grows a multi-card library-place prompt.
/// - <b>Reveal-event emission</b>: the printed "reveal a creature or
///   instant card" should emit a <see cref="Majik.Core.Events.CardRevealedEvent"/>
///   for the picked card so portal subscribers can flash it. Same gap
///   as Stoneforge Mystic's ETB tutor — deferred behind the reveal-
///   event plumbing pass.
/// </summary>
[CardName("Magmatic Channeler")]
public static class MagmaticChannelerFactory
{
    public const string CardName = "Magmatic Channeler";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 1;
    public const int Toughness = 2;
    public const string ActivationCost = "{2}{R}";
    public const int PeekCount = 4;
    public const int GraveyardThreshold = 4;

    /// <summary>
    /// Construct Magmatic Channeler. The activated look-pick-bottom
    /// ability is attached; the activation cost ({2}{R}, {T}) is paid
    /// atomically by the cost layer (CR 601.2g) and the resolve closure
    /// re-checks the graveyard threshold per <see cref="CanActivateGraveyardGate"/>
    /// (CR 602.5b — see class xmldoc for the v1 gate-enforcement
    /// posture).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.1.
        //   "{2}{R}, {T}: Look at the top four cards of your library. You
        //    may reveal a creature or instant card from among them and
        //    put it into your hand. Put the rest on the bottom of your
        //    library in any order."
        //   "Activate only if there are four or more instant and/or
        //    sorcery cards in your graveyard." — CR 602.5b activation
        //    restriction; v1 enforced via the resolve-time guard inside
        //    the effect closure + the public static predicate
        //    CanActivateGraveyardGate. See class xmldoc.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: look at top {PeekCount}, may take a creature/instant, rest on bottom",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 602.5b — defensive re-check at resolve time. The
                // gate should already have been enforced at activation
                // time by the bot policy / action validator, but until
                // IActivatedAbility.CanActivate ships an authoritative
                // hook, the resolve-time guard is the safety net. The
                // cost was paid by the cost layer (CR 601.2g) — short-
                // circuiting here mirrors Liliana of the Veil's -6
                // ultimate deferred no-op (cost paid, body skipped).
                if (!CanActivateGraveyardGate(controller)) return;

                var library = controller.Zones.Library;

                // CR 701.20 — "Look at the top four cards of your library."
                // Snapshot up to PeekCount cards (fewer if the library is
                // short — same posture as Amped Raptor / Curator of
                // Mysteries). Empty library = clean no-op.
                var peeked = library.GetCards().Take(PeekCount).ToList();
                if (peeked.Count == 0) return;

                // Eligible reveal pool — Creature OR Instant. Sorceries
                // are EXCLUDED by the printed wording (distinct from the
                // graveyard gate which counts instants + sorceries).
                var eligible = peeked
                    .Where(c => c.HasType(CardType.Creature) || c.HasType(CardType.Instant))
                    .ToList();

                // "You may reveal …" — controller's choice. Agent path:
                // ChooseLibraryPickAsync (Intent embedded in the kind
                // label). Pre-agent fallback: first eligible card (matches
                // every other look-and-pick factory's deterministic
                // default — Atraxa, Bonecrusher's Stomp pile, Eladamri's
                // Call).
                ICard? pick = null;
                if (eligible.Count > 0)
                {
                    var agent = AgentRegistry.Get(controller);
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute
                        // becomes async (same pattern as ConsiderFactory).
                        pick = agent.ChooseLibraryPickAsync(
                            ctx: null,
                            candidates: eligible,
                            kindLabel: "creature or instant card")
                            .GetAwaiter().GetResult();

                        // Defensive — never accept a pick the candidate
                        // pool didn't surface (a mis-wired agent could
                        // otherwise drive a hand-add for a card outside
                        // the look-window). Mirrors AmpedRaptor's
                        // chooser-validation guard.
                        if (pick != null && !eligible.Contains(pick))
                        {
                            pick = null;
                        }
                    }
                    else
                    {
                        pick = eligible[0];
                    }
                }

                // Move the pick (if any) Library → Hand. The rest stay
                // in the peeked list for the bottom-of-library step.
                if (pick != null)
                {
                    library.RemoveCard(pick);
                    controller.Zones.Hand.AddCard(pick);
                    if (pick is Card concretePick)
                    {
                        concretePick.SetZone(ZoneType.Hand);
                    }
                }

                // CR 701.20 — "Put the rest on the bottom of your library
                // in any order." v1 preserves snapshot order (top → bottom
                // becomes bottom → bottom-1 → … which is the identity for
                // the deterministic fallback). Future agent prompt for
                // re-ordering plugs in here — see class xmldoc.
                foreach (var remainder in peeked)
                {
                    if (ReferenceEquals(remainder, pick)) continue;
                    library.RemoveCard(remainder);
                    library.AddCard(remainder); // Zone.AddCard appends to the bottom.
                    if (remainder is Card concreteRemainder)
                    {
                        concreteRemainder.SetZone(ZoneType.Library);
                    }
                }
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// CR 602.5b activation gate — "Activate only if there are four or
    /// more instant and/or sorcery cards in your graveyard."
    ///
    /// Public so action-enumeration callers (bot policies, agent legal-
    /// action probes) can gate the activation BEFORE the cost layer
    /// fires. Counts <see cref="CardType.Instant"/> + <see cref="CardType.Sorcery"/>
    /// cards in <paramref name="controller"/>'s graveyard (CR 205.2 /
    /// CR 400.1) and returns <c>true</c> iff the count meets or exceeds
    /// <see cref="GraveyardThreshold"/>. The resolve-time guard in the
    /// effect closure invokes the same predicate as a safety net.
    /// </summary>
    public static bool CanActivateGraveyardGate(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        if (controller.Zones?.Graveyard == null) return false;

        var count = 0;
        foreach (var c in controller.Zones.Graveyard.GetCards())
        {
            if (c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
            {
                count++;
                if (count >= GraveyardThreshold) return true;
            }
        }
        return false;
    }
}
