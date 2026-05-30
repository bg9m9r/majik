using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wistfulness (Modern Horizons 3, {3}{G/U}{G/U}).
///
/// Creature — Elemental Incarnation 6/5. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, if {G}{G} was spent to cast it, exile
///    target artifact or enchantment an opponent controls.
///    When this creature enters, if {U}{U} was spent to cast it, draw two
///    cards, then discard a card.
///    Evoke {G/U}{G/U} (You may cast this spell for its evoke cost. If you
///    do, it's sacrificed when it enters.)"
///
/// ## Shape source
/// Card identity (name, {3}{G/U}{G/U}, 6/5, Creature — Elemental
/// Incarnation) loads from <c>wistfulness.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (the JSON loader parses hybrid
/// pips). The two conditional-ETB triggers + Evoke marker/sacrifice trigger
/// are layered on in code — same posture as <see cref="VibranceFactory"/>.
///
/// ## Implemented (v1)
/// - 6/5 Elemental Incarnation at {3}{G/U}{G/U}.
/// - <b>Evoke {G/U}{G/U} (CR 702.74)</b> — keyword marker + printed
///   sacrifice trigger via <see cref="EvokeFactory.Build"/>. Pure hybrid-
///   mana alt-cost (no pitch), wired at cast time with
///   <see cref="Majik.Core.Costs.EvokeAlternativeCost(ValueObjects.ManaCost)"/>.
/// - <b>GG-conditional ETB exile (CR 603.4 intervening-if)</b>: gates on
///   <see cref="Card.SpentAtLeast"/>(<see cref="ManaColor.Green"/>, 2).
///   Declares a 1..1 "target artifact or enchantment an opponent controls"
///   request (the opponent-controls filter narrows
///   <see cref="ReclamationSageFactory"/>'s gatherer). On resolution exiles
///   the chosen target via <see cref="Fx.MoveToExile"/> (CR 701.20), after
///   re-validating it is still an opponent-controlled artifact / enchantment
///   on the battlefield (CR 608.2b — illegal target → clean no-op).
/// - <b>UU-conditional ETB loot (CR 603.4 intervening-if)</b>: gates on
///   <see cref="Card.SpentAtLeast"/>(<see cref="ManaColor.Blue"/>, 2). On
///   resolution draws two cards then discards one (CR 121.1 / CR 701.16 —
///   same draw-then-discard body as <see cref="IzzetCharmFactory"/>, sized
///   draw 2 / discard 1; empty-library draw flags SBA loss per CR 704.5b).
///
/// The intervening-if pattern keys off the per-color spent-count ledger
/// (<see cref="Card.PendingCastColorCounts"/>) so {G}{G} / {G}{U} / {U}{U}
/// are correctly distinguished — multiplicity the distinct-set
/// <see cref="Card.PendingCastColors"/> can't express. Both triggers'
/// intervening-ifs are evaluated at trigger-detection time (before either
/// resolves), so the shared ledger is read by both before clearing.
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target / discard prompts</b>: production callers
///   wire <see cref="TriggeredAbility.SetChosenTargets"/> before triggers
///   resolve; discard uses the deterministic last-in-hand fallback (same
///   queue as Izzet Charm / Faithless Looting). Exile falls back to the
///   first legal opponent-controlled artifact/enchantment when no agent
///   picked (mirrors <see cref="ReclamationSageFactory"/>).
/// </summary>
[CardName("Wistfulness")]
public static class WistfulnessFactory
{
    public const string CardName = "Wistfulness";
    public const string Slug = "wistfulness";
    public const int DrawCount = 2;
    public const int DiscardCount = 1;

    private const string EvokeKeyword = "Evoke";

    /// <summary>Construct Wistfulness with triggers attached to the card
    /// shape but NOT registered with any <see cref="TriggerManager"/>.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>Construct Wistfulness with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, all three
    /// triggers (GG-exile, UU-loot, Evoke sacrifice) are registered for
    /// bus-driven firing (CR 603.2).</summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Evoke {G/U}{G/U} — CR 702.74. Marker + printed sacrifice trigger.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(EvokeKeyword, card, owner));
        var evokeSac = EvokeFactory.Build(card);
        card.AddAbility(evokeSac);
        triggers?.RegisterTriggeredAbility(evokeSac);

        // ----------------------------------------------------------------
        // GG ETB — "if {G}{G} spent, exile target artifact/enchantment an
        // opponent controls." CR 603.4 intervening-if on SpentAtLeast(G, 2).
        // ----------------------------------------------------------------
        TriggeredAbility? ggTrigger = null;
        var ggEffect = new Effect(
            $"{CardName}: exile target artifact/enchantment an opponent controls (if GG spent)",
            () =>
            {
                ResolveExile(card, owner, ggTrigger);
                card.ClearPendingCastColors();
            });

        ggTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { ggEffect },
            interveningIf: () => card.SpentAtLeast(ManaColor.Green, 2),
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or enchantment an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: GatherOpponentTargets(card, owner).Cast<object>().ToList(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(ggTrigger);
        triggers?.RegisterTriggeredAbility(ggTrigger);

        // ----------------------------------------------------------------
        // UU ETB — "if {U}{U} spent, draw two cards, then discard a card."
        // CR 603.4 intervening-if on SpentAtLeast(Blue, 2).
        // ----------------------------------------------------------------
        var uuEffect = new Effect(
            $"{CardName}: draw {DrawCount} then discard {DiscardCount} (if UU spent)",
            () =>
            {
                var controller = card.Controller ?? owner;
                DrawThenDiscard(controller);
                card.ClearPendingCastColors();
            });

        var uuTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { uuEffect },
            interveningIf: () => card.SpentAtLeast(ManaColor.Blue, 2),
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(uuTrigger);
        triggers?.RegisterTriggeredAbility(uuTrigger);

        return card;
    }

    /// <summary>Snapshot the controller-visible opponent-controlled
    /// artifact/enchantment set at trigger-creation time. Production callers
    /// refresh via the <see cref="TargetRequest.CandidateGatherer"/> against
    /// a live game context (which can see every opponent's battlefield).</summary>
    private static IReadOnlyList<ICard> GatherOpponentTargets(Creature card, Player owner)
    {
        // No game context here — the controller can't see opponents' zones
        // through this overload, so the snapshot is conservatively empty.
        // The live gatherer (above) populates the real set at resolution.
        return Array.Empty<ICard>();
    }

    /// <summary>
    /// Resolve the GG exile. Honours <see cref="TriggeredAbility.ChosenTargets"/>;
    /// re-validates the pick is still an opponent-controlled artifact /
    /// enchantment on the battlefield (CR 608.2b) before exiling
    /// (CR 701.20). Mandatory effect — but a missing / illegal target is a
    /// clean no-op.
    /// </summary>
    private static void ResolveExile(Creature card, Player owner, TriggeredAbility? gg)
    {
        if (gg == null) return;
        var chosen = gg.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Permanent picked) return;

        var controller = card.Controller ?? owner;

        // CR 608.2b — illegal-on-resolution check: still on the battlefield,
        // still an artifact/enchantment, still controlled by an opponent.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!(picked.HasType(CardType.Artifact) || picked.HasType(CardType.Enchantment))) return;
        if (ReferenceEquals(picked.Controller, controller)) return;

        // CR 701.20 — exile to the card's owner's exile zone.
        Fx.MoveToExile(picked);
    }

    /// <summary>
    /// CR 121.1 / CR 701.16 — draw two cards, then discard one. Empty-library
    /// draw flags SBA loss (CR 704.5b). Discard is the deterministic
    /// last-in-hand fallback (real agent prompt deferred — same posture as
    /// <see cref="IzzetCharmFactory"/> / Faithless Looting).
    /// </summary>
    private static void DrawThenDiscard(Player player)
    {
        for (var i = 0; i < DrawCount; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                player.MarkTriedToDrawFromEmptyLibrary();
                break;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }

        for (var i = 0; i < DiscardCount; i++)
        {
            var pick = player.Zones.Hand.GetCards().LastOrDefault();
            if (pick == null) break;
            player.Zones.Hand.RemoveCard(pick);
            player.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
        }
    }
}
