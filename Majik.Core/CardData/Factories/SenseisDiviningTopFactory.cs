using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sensei's Divining Top (Champions of Kamigawa
/// and reprints).
///
/// Artifact — {1}. Oracle text:
///   "{T}: Look at the top three cards of your library, then put them
///    back in any order."
///   "{1}, {T}: Draw a card, then put Sensei's Divining Top on top of
///    its owner's library."
///
/// ## Implemented (v1)
/// - Artifact identity, mana cost {1}, owner/controller wired.
/// - <b>{T}: look-3-and-reorder</b> — <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Tap"/>. Resolution peeks up to three cards
///   off the controller's library via
///   <see cref="ScryAction.Peek"/> and applies a reorder decision via
///   <see cref="ScryAction.Apply"/> with <c>ToBottom = []</c> (Top is
///   reorder-only, never bottoms cards). The reorder decision is sourced
///   from the registered <see cref="IPlayerAgent"/> (same pattern as
///   <see cref="PonderFactory"/>); the pre-agent default preserves the
///   peeked order. Short libraries (&lt; 3 cards) and empty libraries
///   are handled — <c>Peek</c> returns what exists; an empty peek skips
///   the apply altogether.
/// - <b>{1}, {T}: draw, then self-return-to-top</b> —
///   <see cref="ActivatedAbility"/> with <see cref="ManaCostCost"/>("{1}")
///   plus <see cref="AdditionalCost.Tap"/>. Resolution draws one card from
///   the controller's library (flagging
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> for the
///   draw-from-empty SBA per CR 704.5b on empty libraries), then moves
///   Top from the battlefield back onto the TOP of its owner's library
///   via <see cref="IZone.InsertCardAt"/>(0). The move uses raw zone
///   manipulation — same shape as Mystical Tutor's "put on top" target
///   move (no ZoneService routing in v1).
///
/// ## "Self-return mid-resolution" (CR 608.2 — classic Top puzzle)
/// The second activated ability draws a card BEFORE Top leaves the
/// battlefield. This matters because Top's controller can — in theory —
/// see what they're drawing while the ability is still resolving and
/// continue stacking activations. v1 ships the literal effect ordering:
/// draw first, then move Top onto the library top. The "look at top" /
/// "draw, then return" interaction with simultaneous Top activations
/// (the infamous Legacy slow-play loop) is out of scope here — the
/// engine resolves one activated ability at a time and the stack is
/// driven by callers, not by an internal loop.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven targeting / "you may" prompts</b>: both activated
///   abilities resolve unconditionally — the {T} reorder always reorders
///   (default = no-op preservation) and the {1}, {T} ability always
///   draws + returns. Top's oracle text has no "you may", so this is
///   structurally correct; the agent prompt for the reorder choice is
///   plumbed through the registry path (mirrors Ponder).
/// - <b>Sorcery-speed gate</b>: neither ability has a sorcery-speed
///   restriction (CR 602.5b — printed activation timing is instant by
///   default unless oracle says otherwise). Top can spin on opponents'
///   turns.
/// - <b>Legendary supertype</b>: the real card prints as a Legendary
///   Artifact. The task spec scopes this factory to a plain Artifact —
///   the Legend Rule SBA (CR 704.5j) is unaffected unless multiple
///   copies share a controller.
/// </summary>
public static class SenseisDiviningTopFactory
{
    public const string CardName = "Sensei's Divining Top";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Sensei's Divining Top owned and controlled by
    /// <paramref name="owner"/>. Both activated abilities are wired
    /// onto the card shape.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var top = new Artifact(CardName, PrintedManaCost);
        top.SetOwner(owner);
        top.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Look at the top three cards of your library, then put them
        // back in any order. (CR 602.1, no mana.)
        // ----------------------------------------------------------------
        var peekEffect = new Effect(
            "Sensei's Divining Top: peek top 3, reorder",
            () => PeekAndReorder(owner));

        var peekAbility = new ActivatedAbility(
            source: top,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(top) },
            effects: new IEffect[] { peekEffect });

        top.AddAbility(peekAbility);

        // ----------------------------------------------------------------
        // {1}, {T}: Draw a card, then put Sensei's Divining Top on top of
        // its owner's library. (CR 602.1.)
        // ----------------------------------------------------------------
        var drawReturnEffect = new Effect(
            "Sensei's Divining Top: draw 1, then return to top of owner's library",
            () => DrawThenReturnToTop(top, owner));

        var drawReturnAbility = new ActivatedAbility(
            source: top,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(top),
            },
            effects: new IEffect[] { drawReturnEffect });

        top.AddAbility(drawReturnAbility);

        return top;
    }

    /// <summary>
    /// Peek up to three cards off <paramref name="controller"/>'s library
    /// and apply the agent-supplied reorder. Mirrors Ponder's reorder
    /// path: ToBottom is collapsed into TopOrder defensively so any
    /// agent returning a partition still ends up putting every peeked
    /// card back on top (Top is reorder-only, CR 701.20 does not apply —
    /// Top is a look/reorder, not a scry).
    /// </summary>
    private static void PeekAndReorder(Player controller)
    {
        var peeked = ScryAction.Peek(controller, 3);
        if (peeked.Count == 0) return;

        var agent = AgentRegistry.Get(controller);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            // TODO: drop sync-over-async once IEffect.Execute becomes async.
            var agentDecision = agent.ChooseScryDecisionAsync(null, peeked)
                .GetAwaiter().GetResult();
            if (agentDecision.ToBottom.Count > 0)
            {
                var collapsed = agentDecision.TopOrder
                    .Concat(agentDecision.ToBottom)
                    .ToList();
                decision = new ScryAction.ScryDecision(
                    ToBottom: Array.Empty<ICard>(),
                    TopOrder: collapsed);
            }
            else
            {
                decision = agentDecision;
            }
        }
        else
        {
            decision = new ScryAction.ScryDecision(
                ToBottom: Array.Empty<ICard>(),
                TopOrder: peeked.ToList());
        }

        ScryAction.Apply(controller, peeked.Count, decision);
    }

    /// <summary>
    /// Draw a card for <paramref name="controller"/>, then move
    /// <paramref name="top"/> from the battlefield onto the top of its
    /// owner's library via <see cref="IZone.InsertCardAt"/>(0). Empty
    /// library flags the draw-from-empty SBA (CR 704.5b) via
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
    /// </summary>
    private static void DrawThenReturnToTop(Artifact top, Player controller)
    {
        // ----- Draw a card -----
        var drawn = controller.Zones.Library.GetCards().FirstOrDefault();
        if (drawn == null)
        {
            controller.MarkTriedToDrawFromEmptyLibrary();
        }
        else
        {
            controller.Zones.Library.RemoveCard(drawn);
            controller.Zones.Hand.AddCard(drawn);
            drawn.SetZone(ZoneType.Hand);
        }

        // ----- Then put Top on top of its owner's library -----
        var owner = top.Owner;
        if (owner == null) return;

        // Top must be on the battlefield to be moved by this effect
        // (CR 608.2b — illegal/missing source makes the move-self do
        // nothing). The cost-tap was paid at activation; we do not
        // un-tap here — when Top arrives in its new zone it loses the
        // tapped-state per CR 614 (zone change = new object).
        if (top.Zone != ZoneType.Battlefield) return;

        var holder = top.Controller ?? owner;
        holder.Zones.Battlefield.RemoveCard(top);
        owner.Zones.Library.InsertCardAt(0, top);
        top.SetZone(ZoneType.Library);
    }
}
