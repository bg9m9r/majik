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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vibrance (Modern Horizons 3, {3}{R/G}{R/G}).
///
/// Creature — Elemental Incarnation 4/4. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, if {R}{R} was spent to cast it, this
///    creature deals 3 damage to any target.
///    When this creature enters, if {G}{G} was spent to cast it, search
///    your library for a land card, reveal it, put it into your hand, then
///    shuffle. You gain 2 life.
///    Evoke {R/G}{R/G}"
///
/// ## Shape source
/// Card identity (name, {3}{R/G}{R/G}, 4/4, Creature — Elemental
/// Incarnation) loads from <c>vibrance.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (the JSON loader already
/// parses hybrid pips — see flame-javelin.json). The two conditional-ETB
/// triggers + the Evoke keyword/sacrifice trigger are layered on in code.
///
/// ## Implemented (v1)
/// - 4/4 Elemental Incarnation at {3}{R/G}{R/G}.
/// - <b>Evoke {R/G}{R/G} (CR 702.74)</b> — keyword marker + the printed
///   "When this creature enters, if its evoke cost was paid, sacrifice it"
///   trigger via <see cref="EvokeFactory.Build"/>. The evoke alternative
///   cost itself is pure hybrid mana ({R/G}{R/G}); callers wire it with
///   <see cref="Majik.Core.Costs.EvokeAlternativeCost(ValueObjects.ManaCost)"/>
///   at cast time (same as <see cref="IngotChewerFactory"/> — no pitch
///   component).
/// - <b>RR-conditional ETB damage (CR 603.4 intervening-if)</b>: a
///   triggered ability whose <see cref="TriggeredAbility.InterveningIf"/>
///   reads <see cref="Card.SpentAtLeast"/>(<see cref="ManaColor.Red"/>, 2)
///   — fires onto the stack ONLY when {R}{R} was actually spent. Declares a
///   single 1..1 "any target" request (same shape as
///   <see cref="MunitionsExpertFactory"/>); on resolution deals 3 damage via
///   <see cref="Fx.DealDamageAny"/> (CR 306.7 — planeswalker damage becomes
///   loyalty removal). Mandatory (no "may"): a legal target must be chosen.
/// - <b>GG-conditional ETB tutor + lifegain (CR 603.4 intervening-if)</b>:
///   intervening-if reads <see cref="Card.SpentAtLeast"/>(<see cref="ManaColor.Green"/>, 2).
///   On resolution: search the controller's library for ONE land card
///   (any land, not just basic — CR 305 — same tutor shape as
///   <see cref="BorderlandRangerFactory"/> minus the Basic filter), move it
///   Library → Hand, shuffle once (CR 701.20a), then gain 2 life
///   (CR 119.3 — <see cref="Player.GainLife"/>).
///
/// The intervening-if pattern keys off the per-color spent-count ledger
/// (<see cref="Card.PendingCastColorCounts"/>) stamped by
/// <see cref="Majik.Core.Game.TurnDriver"/> after mana payment commits, so
/// the engine can distinguish {R}{R} from {R}{G} from {G}{G} — the
/// multiplicity the distinct-set <see cref="Card.PendingCastColors"/> can't
/// express. Both triggers' intervening-ifs are evaluated at
/// trigger-detection time (before either resolves), so the shared ledger is
/// read by both before being cleared at resolution.
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target / tutor prompts</b>: production callers
///   wire <see cref="TriggeredAbility.SetChosenTargets"/> from an agent
///   prompt before triggers resolve; the tutor consults the registered
///   <see cref="IPlayerAgent"/> (deterministic first-land fallback when no
///   agent). Same posture as <see cref="MunitionsExpertFactory"/> /
///   <see cref="BorderlandRangerFactory"/>.
/// - <b>Reveal step</b>: the tutored land moves Library → Hand without a
///   public reveal event — same gap as every tutor factory.
/// </summary>
[CardName("Vibrance")]
public static class VibranceFactory
{
    public const string CardName = "Vibrance";
    public const string Slug = "vibrance";
    public const int EtbDamage = 3;
    public const int LifeGain = 2;

    private const string EvokeKeyword = "Evoke";

    /// <summary>Construct Vibrance with its triggers attached to the card
    /// shape but NOT registered with any <see cref="TriggerManager"/>.
    /// Suitable for shape / <see cref="NamedCardFactory"/> dispatch tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>Construct Vibrance with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, all three
    /// triggers (RR-damage, GG-tutor, Evoke sacrifice) are registered for
    /// bus-driven firing (CR 603.2).</summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Evoke {R/G}{R/G} — CR 702.74. Marker + printed sacrifice trigger.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(EvokeKeyword, card, owner));
        var evokeSac = EvokeFactory.Build(card);
        card.AddAbility(evokeSac);
        triggers?.RegisterTriggeredAbility(evokeSac);

        // ----------------------------------------------------------------
        // RR ETB — "if {R}{R} was spent, deal 3 damage to any target."
        // CR 603.4 — intervening-if gates stack placement on SpentAtLeast.
        // ----------------------------------------------------------------
        TriggeredAbility? rrTrigger = null;
        var rrEffect = new Effect(
            $"{CardName}: deal {EtbDamage} damage to any target (if RR spent)",
            () =>
            {
                if (rrTrigger == null) return;
                var chosen = rrTrigger.ChosenTargets;
                if (chosen.Count > 0 && chosen[0].Count > 0)
                {
                    Fx.DealDamageAny(chosen[0][0], EtbDamage);
                }
                // Consume the spent ledger so a later non-cast re-entry
                // (blink, token copy) doesn't reuse this cast's colors.
                card.ClearPendingCastColors();
            });

        rrTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { rrEffect },
            interveningIf: () => card.SpentAtLeast(ManaColor.Red, 2),
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(rrTrigger);
        triggers?.RegisterTriggeredAbility(rrTrigger);

        // ----------------------------------------------------------------
        // GG ETB — "if {G}{G} was spent, search a land → hand, shuffle,
        // gain 2 life." CR 603.4 intervening-if on SpentAtLeast(Green, 2).
        // ----------------------------------------------------------------
        var ggEffect = new Effect(
            $"{CardName}: tutor a land to hand + gain {LifeGain} life (if GG spent)",
            () =>
            {
                var controller = card.Controller ?? owner;
                TutorOneLandToHand(controller);
                controller.GainLife(LifeGain);
                card.ClearPendingCastColors();
            });

        var ggTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { ggEffect },
            interveningIf: () => card.SpentAtLeast(ManaColor.Green, 2),
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ggTrigger);
        triggers?.RegisterTriggeredAbility(ggTrigger);

        return card;
    }

    /// <summary>
    /// CR 305 — search <paramref name="player"/>'s library for ONE land card
    /// (any land, not just basic — Vibrance has no Basic restriction),
    /// consult the agent (deterministic first-land fallback when none), move
    /// the pick Library → Hand, then shuffle once (CR 701.20a). The "reveal"
    /// step is a no-op signal in v1 (same gap as every tutor factory) — the
    /// card still reaches the hand so observable state is correct.
    /// </summary>
    private static void TutorOneLandToHand(Player player)
    {
        bool IsLand(ICard c) => c.HasType(CardType.Land);

        var agent = AgentRegistry.Get(player);
        var candidates = player.Zones.Library.GetCards().Where(IsLand).ToList();

        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, candidates,
                        "land card to put into your hand")
                    .GetAwaiter().GetResult()
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Hand, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }
        }

        // CR 701.20a — one shuffle after the search, even when nothing found.
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }
}
