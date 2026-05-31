using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Augur of Bolas (Magic 2013 / various reprints, {1}{U}).
///
/// Creature — Merfolk Wizard 1/3. Oracle text:
///   "When this creature enters, look at the top three cards of your library.
///    You may reveal an instant or sorcery card from among them and put it
///    into your hand. Put the rest on the bottom of your library in any order."
///
/// ## Implemented (v1)
///
/// - 1/3 Creature — Merfolk Wizard at {1}{U}; owner / controller wired.
///   Subtypes <see cref="CardSubtype.Merfolk"/> + <see cref="CardSubtype.Wizard"/>.
///
/// - ETB triggered ability (CR 603.6a — "when this creature enters"):
///     1. Peek the top <see cref="LookCount"/> (3) cards of the controller's
///        library (fewer if the library is short — CR 701.21 / same posture as
///        Magmatic Channeler / Amped Raptor). Empty library → clean no-op.
///     2. Filter the peeked pile to Instant or Sorcery cards — the eligible
///        reveal pool (CR 205.2 / card type check; note Augur is narrower than
///        Magmatic Channeler which allows Creatures as well).
///     3. Ask the controller's registered <see cref="IPlayerAgent"/> (via
///        <see cref="AgentRegistry"/>) to pick an eligible card via
///        <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> — the printed
///        "you may" maps to the agent returning <c>null</c> to decline
///        (CR 603.6c — printed "may" is a controller choice on resolution).
///        Pre-agent deterministic fallback: pick the first eligible card
///        (consistent with Magmatic Channeler / Sleight of Hand factories).
///     4. Move the pick (if any) Library → Hand via raw zone manipulation.
///     5. Put the rest — the peeked cards that were NOT picked — on the
///        bottom of the library in snapshot order (v1: identity ordering;
///        the "in any order" agent prompt is a future plug-in per the same
///        gap noted in <see cref="MagmaticChannelerFactory"/>).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card + ETB shape. The ETB effect uses
///   raw zone manipulation directly (no TriggerManager required). Suitable
///   for dispatcher / shape-only tests.
/// - <see cref="Create(Player, TriggerManager?, Func{IReadOnlyList{ICard}, ICard?}?,
///   Action{Result}?)"/> — full overload with optional TriggerManager
///   registration, picker override, and resolved-callback for test observation.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"In any order" agent prompt for re-bottoming</b>: v1 preserves
///   snapshot order. A multi-card library-place prompt plugs in here.
/// - <b>Reveal-event emission</b>: the printed "reveal" should emit a
///   <see cref="Majik.Core.Events.CardRevealedEvent"/> for the picked card
///   (deferred behind the reveal-event plumbing pass — same gap as
///   Stoneforge Mystic / Magmatic Channeler).
/// </summary>
[CardName("Augur of Bolas")]
public static class AugurOfBolasFactory
{
    public const string CardName = "Augur of Bolas";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 3;
    public const int LookCount = 3;

    /// <summary>
    /// Result of the ETB trigger resolution. <see cref="Peeked"/> is every
    /// card the ETB looked at (top of library first), <see cref="Eligible"/>
    /// is the subset filtered to Instant or Sorcery, and <see cref="Picked"/>
    /// is the card the controller chose to reveal and put to hand — or
    /// <c>null</c> when the "may" was declined or no eligible card existed.
    /// The picked card has been moved to the Hand zone; all others have been
    /// moved to the bottom of the Library.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ICard> Peeked,
        IReadOnlyList<ICard> Eligible,
        ICard? Picked);

    /// <summary>
    /// Construct Augur of Bolas with no runtime services. The ETB trigger
    /// is attached for shape inspection and uses <see cref="AgentRegistry"/>
    /// for picks (pre-agent fallback: first eligible card). Suitable for
    /// dispatcher / shape-only tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, choosePick: null, onEtbResolved: null);

    /// <summary>
    /// Construct Augur of Bolas with optional TriggerManager wiring,
    /// picker override, and result callback.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger is registered
    /// so a <see cref="Majik.Core.Events.CardMovedEvent"/> → Battlefield for
    /// this card lands on the stack automatically.</param>
    /// <param name="choosePick">Override for the eligible-card selector.
    /// Receives the list of instant/sorcery cards in the top three; returns
    /// the card to put into hand, or <c>null</c> to decline the "may"
    /// (CR 603.6c). When <c>null</c> the factory consults
    /// <see cref="AgentRegistry"/>; if no agent is registered the
    /// deterministic fallback (first eligible card) applies.</param>
    /// <param name="onEtbResolved">Callback invoked after the ETB resolves
    /// with the full <see cref="Result"/>. Used by tests to observe zone
    /// moves without going through TriggerManager.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<ICard>, ICard?>? choosePick = null,
        Action<Result>? onEtbResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger (CR 603.6a):
        //   "When this creature enters, look at the top three cards of your
        //    library. You may reveal an instant or sorcery card from among
        //    them and put it into your hand. Put the rest on the bottom of
        //    your library in any order."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName} — look at top {LookCount}, may reveal an instant or sorcery to hand, " +
            "rest on bottom",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                var result = await ResolveEtbAsync(controller, ctx, choosePick).ConfigureAwait(false);
                onEtbResolved?.Invoke(result);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Execute Augur of Bolas's ETB body against <paramref name="controller"/>'s
    /// library. Public so tests and bots can drive the resolution without
    /// going through TriggerManager.
    ///
    /// Peeks up to <see cref="LookCount"/> cards (fewer if the library is
    /// short), builds the eligible pile (Instant or Sorcery), and asks
    /// <paramref name="choosePick"/> — or the registered
    /// <see cref="IPlayerAgent"/> via
    /// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> — which card (if any)
    /// to reveal and put into hand. The picked card is moved Library → Hand;
    /// all others are moved to the bottom of the library in snapshot order.
    /// </summary>
    public static async ValueTask<Result> ResolveEtbAsync(
        Player controller,
        ResolutionContext ctx,
        Func<IReadOnlyList<ICard>, ICard?>? choosePick = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;

        // CR 701.20 — "Look at the top three cards of your library." Snapshot
        // up to LookCount cards (fewer if the library is short). Empty
        // library is a clean no-op (no draw-from-empty SBA fires here).
        var peeked = library.GetCards().Take(LookCount).ToList();
        if (peeked.Count == 0)
        {
            return new Result(
                Peeked: Array.Empty<ICard>(),
                Eligible: Array.Empty<ICard>(),
                Picked: null);
        }

        // Eligible reveal pool — Instant OR Sorcery (CR 205.2 type check).
        // Creatures, lands, artifacts, enchantments, planeswalkers are
        // excluded by the printed wording.
        var eligible = peeked
            .Where(c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
            .ToList();

        // "You may reveal…" — controller chooses one or declines (CR 603.6c).
        // Priority order for pick resolution:
        //   1. Supplied choosePick override (test / production caller).
        //   2. Registered IPlayerAgent via AgentRegistry.
        //   3. Deterministic fallback: first eligible card.
        ICard? pick = null;
        if (eligible.Count > 0)
        {
            if (choosePick != null)
            {
                pick = choosePick(eligible);
            }
            else
            {
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                if (agent != null)
                {
                    pick = await agent.ChooseLibraryPickAsync(
                        ctx.Game,
                        candidates: eligible,
                        kindLabel: "instant or sorcery card")
                        .ConfigureAwait(false);

                    // Defensive — never accept a pick outside the eligible pile.
                    if (pick != null && !eligible.Contains(pick))
                    {
                        pick = null;
                    }
                }
                else
                {
                    // Pre-agent deterministic fallback (consistent with
                    // MagmaticChannelerFactory / SleightOfHandFactory).
                    pick = eligible[0];
                }
            }

            // Defensive — if the supplied choosePick returned a card outside
            // the eligible pile, treat it as a declined "may" rather than
            // silently moving an ineligible card to hand.
            if (pick != null && !eligible.Contains(pick))
            {
                pick = null;
            }
        }

        // Move the pick (if any) Library → Hand.
        if (pick != null)
        {
            library.RemoveCard(pick);
            controller.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
        }

        // CR 701.20 — "Put the rest on the bottom of your library in any
        // order." Move all non-picked peeked cards to the bottom of the
        // library. Library.AddCard appends to the end (bottom), so the
        // existing library tail is preserved.
        foreach (var remainder in peeked)
        {
            if (ReferenceEquals(remainder, pick)) continue;
            library.RemoveCard(remainder);
            library.AddCard(remainder);
            remainder.SetZone(ZoneType.Library);
        }

        return new Result(
            Peeked: peeked,
            Eligible: eligible,
            Picked: pick);
    }
}
