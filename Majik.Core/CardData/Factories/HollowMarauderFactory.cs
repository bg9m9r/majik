using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hollow Marauder (Edge of Eternities, {6}{B}).
///
/// Creature — Specter Rogue 4/2. Oracle text (verified against Scryfall):
///   "This spell costs {1} less to cast for each creature card in your
///    graveyard.
///    Flying
///    When this creature enters, any number of target opponents each discard
///    a card. For each of those opponents who didn't discard a card with mana
///    value 4 or greater, draw a card."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {6}{B}; mana value 7</item>
///   <item>Type line: Creature — Specter Rogue; power/toughness 4/2; colors B</item>
/// </list>
///
/// Composes three analogue shapes already in the engine:
/// - <b>Graveyard cost reduction (CR 117.7)</b> — same whole-reduction
///   <see cref="CostReductionAbility.TotalReducer"/> shape as
///   <see cref="TolarianTerrorFactory"/> / <see cref="DemilichFactory"/>, but
///   counts CREATURE cards in the caster's graveyard (one-to-one). CR 117.7c —
///   only generic mana is reduced; the {B} pip is floored by
///   <see cref="CostReduction.GetEffectiveCost"/>.
/// - <b>Flying (CR 702.9)</b> — carried as a keyword on the base JSON shape.
/// - <b>ETB target-discard + per-opponent conditional draw</b> — the
///   each-target-discard-then-conditional-consequence body from
///   <see cref="KroxaTitanFactory"/>, but the target set is "any number of
///   target opponents" (CR 115 — a 0..N target request, not "each opponent")
///   and the consequence is a DRAW for the controller per opponent who didn't
///   discard a card with mana value 4 or greater (CR 121.1 instead of Kroxa's
///   life-loss).
///
/// The base shape (name / Creature / Specter Rogue / {6}{B} / 4/2 / Flying)
/// is materialised from the embedded JSON definition
/// (<c>hollow-marauder.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the cost reducer and the ETB
/// trigger are layered on here because the JSON <c>AbilityDefinition</c>
/// schema expresses neither yet (same posture as Tolarian Terror / Kroxa).
///
/// ## Implemented (v1)
/// - 4/2 Creature — Specter Rogue at {6}{B}, black, Flying.
/// - <b>Graveyard cost reduction</b>: generic mana drops {1} per creature card
///   in the caster's graveyard; {B} pip untouched (CR 117.7c). Tolerates a
///   null roster / graveyard (shape-only + pre-board affordability calls).
/// - <b>ETB trigger (CR 603.6a)</b>: a single 0..int.MaxValue "any number of
///   target opponents" <see cref="TargetRequest"/>. On resolution each chosen
///   opponent discards a card of their own choice (CR 701.8 — the discarding
///   player chooses; agent-driven when supplied, deterministic first-card
///   fallback). For each chosen opponent who did NOT discard a card with mana
///   value &gt;= 4 (because they discarded a cheaper card OR had an empty hand
///   and couldn't discard at all), the controller draws a card (CR 121.1).
///   Empty library mid-draw flags the SBA loss (CR 704.5b).
///
/// ## Deferred (v1 gaps)
/// - <b>Discard-choice prompt UI</b>: each target opponent picks what to
///   discard. v1 is agent-driven when an <c>opponentAgent</c> is supplied,
///   else a deterministic first-card pick (same posture as Kroxa /
///   Mind Rot).
/// </summary>
[CardName("Hollow Marauder")]
public static class HollowMarauderFactory
{
    public const string CardName = "Hollow Marauder";
    public const string Slug = "hollow-marauder";

    /// <summary>CR 121.1 — "draw a card" threshold: an opponent who discarded
    /// a card with mana value &gt;= this denies the controller a draw.</summary>
    public const int DrawDenyManaValue = 4;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches the cost reducer and the ETB trigger structurally so the card
    /// shape is correct; no TriggerManager wiring, deterministic discard picks.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, opponentAgent: null);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="opponentAgent">Optional agent for each target opponent's
    /// discard pick (CR 701.8 — the discarding player chooses). Null falls
    /// back to a deterministic first-card pick.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IPlayerAgent? opponentAgent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Creature / Specter Rogue / {6}{B} / 4/2 / Flying)
        // from the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {1} less to cast for each creature
        // card in your graveyard." Whole-reduction shape; counts creature
        // cards in the caster's graveyard at cost-calc time. CR 117.7c —
        // generic only; the {B} pip is floored at zero by
        // CostReduction.GetEffectiveCost. Mirrors TolarianTerrorFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: ComputeReduction,
            description:
                "This spell costs {1} less to cast for each creature card in " +
                "your graveyard."));

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a / CR 115 (target choice) / CR 701.8
        // (discard) / CR 121.1 (draw).
        //   "When this creature enters, any number of target opponents each
        //    discard a card. For each of those opponents who didn't discard a
        //    card with mana value 4 or greater, draw a card."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: target opponents each discard; draw per opponent who " +
            "didn't discard a card with mana value 4+",
            () =>
            {
                if (etbTrigger == null) return;
                ResolveDiscardDraw(owner, card, etbTrigger.ChosenTargets, opponentAgent);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of target opponents",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Caster-graveyard reduction (CR 117.7): {1} per creature card in the
    /// caster's graveyard. Tolerates a null roster / graveyard (shape-only +
    /// pre-board affordability calls).
    /// </summary>
    private static int ComputeReduction(Player? caster)
    {
        var graveyard = caster?.Zones?.Graveyard;
        if (graveyard == null) return 0;

        var n = 0;
        foreach (var g in graveyard.GetCards())
        {
            if (g.HasType(CardType.Creature)) n++;
        }
        return n;
    }

    // -----------------------------------------------------------------------
    // ETB body — CR 701.8 (discard) + CR 121.1 (draw).
    // "any number of target opponents each discard a card. For each of those
    //  opponents who didn't discard a card with mana value 4 or greater, draw
    //  a card."
    //
    // CR 608.2 sequencing: ALL chosen opponents discard first, THEN the
    // controller draws once per opponent who did NOT discard an MV>=4 card.
    // We snapshot each opponent's discard outcome, then draw in a second pass.
    // -----------------------------------------------------------------------
    private static void ResolveDiscardDraw(
        Player owner,
        Creature card,
        IReadOnlyList<IReadOnlyList<object>> chosenTargets,
        IPlayerAgent? opponentAgent)
    {
        var controller = card.Controller ?? owner;

        if (chosenTargets.Count == 0) return;
        var opponents = chosenTargets[0]
            .OfType<Player>()
            // CR 115 / CR 109.5 — "target opponents" excludes the controller.
            .Where(p => !ReferenceEquals(p, controller))
            .ToList();
        if (opponents.Count == 0) return;

        // Pass 1 — each chosen opponent discards a card (CR 701.8). Record
        // whether each discarded a card with mana value >= 4 "this way".
        var drawsOwed = 0;
        foreach (var opp in opponents)
        {
            var discardedHighMv = OpponentDiscardsOne(opp, opponentAgent);
            // CR 121.1 — controller draws for each opponent who did NOT
            // discard an MV>=4 card (cheaper discard OR empty hand).
            if (!discardedHighMv) drawsOwed++;
        }

        // Pass 2 — controller draws the owed cards (CR 121.1). Empty library
        // mid-draw flags the SBA loss (CR 704.5b) and stops further draws.
        for (var i = 0; i < drawsOwed; i++)
        {
            var top = controller.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                controller.MarkTriedToDrawFromEmptyLibrary();
                break;
            }
            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }

    /// <summary>
    /// CR 701.8 — <paramref name="opponent"/> discards one card of their
    /// choice. Returns true IFF the discarded card has mana value &gt;= 4.
    /// An empty hand → no discard → returns false (they didn't discard a
    /// card with mana value 4 or greater this way).
    /// </summary>
    private static bool OpponentDiscardsOne(Player opponent, IPlayerAgent? opponentAgent)
    {
        var hand = opponent.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return false; // couldn't discard at all.

        ICard pick;
        if (opponentAgent != null)
        {
            var chosen = opponentAgent
                .ChooseFromHandAsync(opponent, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                .GetAwaiter().GetResult();
            pick = (chosen != null && chosen.Zone == ZoneType.Hand) ? chosen : hand[0];
        }
        else
        {
            pick = hand[0];
        }

        opponent.Zones.Hand.RemoveCard(pick);
        opponent.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);

        // CR 202.3 — a card's mana value is the total of its mana cost.
        var mv = pick is Card concrete ? concrete.ManaCostValue.TotalValue : 0;
        return mv >= DrawDenyManaValue;
    }
}
