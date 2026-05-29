using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Omen of the Sea (Theros Beyond Death, {1}{U}).
///
/// Enchantment. Oracle text (verified against Scryfall 2026-05-29):
///   "Flash
///    When this enchantment enters, scry 2, then draw a card.
///    {2}{U}, Sacrifice this enchantment: Scry 2."
///
/// Omen of the Sea composes shapes the engine already ships:
/// - <b>Flash (CR 702.8)</b> — <see cref="KeywordAbility"/> marker, same as
///   <see cref="SubtletyFactory"/> / <see cref="SpellstutterSpriteFactory"/>.
/// - <b>ETB scry-then-draw (CR 603.6a + CR 701.20 + CR 121.1)</b> — the same
///   <see cref="ScryAction"/> + top-of-library draw body as
///   <see cref="PreordainFactory"/>, hung on an
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> trigger like
///   <see cref="WallOfOmensFactory"/>.
/// - <b>{2}{U}, Sacrifice this: Scry 2 (CR 605 + CR 701.20)</b> — a
///   non-mana <see cref="ActivatedAbility"/> whose cost is a
///   <see cref="ManaCostCost"/> plus a self-<see cref="AdditionalCost.Sacrifice"/>;
///   the sacrifice zone move is performed by the effect closure (same posture
///   as <see cref="NihilSpellbombFactory"/>, where AdditionalCost.Pay is a stub).
///
/// The base card shape (name / Enchantment type / {1}{U} cost) is materialised
/// from the embedded JSON definition (<c>omen-of-the-sea.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the Flash marker, ETB trigger
/// and activated ability are layered on here (same posture as
/// <see cref="ArdentPleaFactory"/> — the JSON AbilityDefinition schema does not
/// express scry / sacrifice-self abilities yet).
///
/// ## Implemented (v1)
/// - Enchantment shape at printed cost {1}{U}.
/// - Flash keyword marker.
/// - ETB triggered ability: scry 2, then draw a card. Scry decision comes from
///   the controller's registered <see cref="IPlayerAgent"/>; the pre-agent
///   default sends both peeked cards to the bottom (same fallback as Preordain).
///   An empty library flags the controller for the SBA-driven loss
///   (CR 104.3c / CR 704.5b) via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
/// - Activated ability {2}{U}, Sacrifice this: Scry 2. Same scry pipeline; the
///   effect closure moves the enchantment Battlefield → Graveyard.
///
/// ## Deferred (v1 gaps — identical posture to the cited analogues)
/// - Stack/priority for the ETB trigger and the activated ability is handled by
///   the engine's TriggerManager / AbilityActivationFlow integration; the
///   single-arg dispatcher path attaches abilities structurally and exposes the
///   effect bodies for direct execution in unit tests.
/// - AdditionalCost.Pay for the sacrifice is a no-op stub; the effect closure
///   performs the zone move (same as Nihil Spellbomb / Aether Spellbomb).
/// </summary>
[CardName("Omen of the Sea")]
public static class OmenOfTheSeaFactory
{
    public const string CardName = "Omen of the Sea";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "omen-of-the-sea";

    /// <summary>Activated-ability mana cost — {2}{U}.</summary>
    public const string ActivatedManaCost = "{2}{U}";

    private const int ScryAmount = 2;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches the Flash marker, ETB trigger and activated ability to the card
    /// shape; no TriggerManager wiring.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Omen of the Sea with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a battlefield <see cref="Events.CardMovedEvent"/> places it
    /// on the stack automatically (CR 603.3).
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Enchantment / {1}{U}) from the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 702.8 — Flash keyword marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this enchantment enters, scry 2, then draw a card."
        // CR 701.20 (scry) sequenced before CR 121.1 (draw).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName} — scry 2, then draw a card on ETB",
            () =>
            {
                Scry2(owner);

                // "Then draw a card." Top-of-library draw; an empty library
                // flags the SBA-driven loss (CR 104.3c / CR 704.5b).
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 605 (not a mana ability; goes on the stack).
        //   "{2}{U}, Sacrifice this enchantment: Scry 2."
        // Cost: {2}{U} (ManaCostCost) + self-sacrifice (Battlefield →
        // Graveyard). The sacrifice zone move is performed by the effect
        // closure (AdditionalCost.Pay is a stub — same as Nihil Spellbomb).
        // ----------------------------------------------------------------
        var scryEffect = new Effect(
            $"{CardName} — {{2}}{{U}}, Sacrifice this: scry 2",
            () =>
            {
                // Sacrifice payment stub: move Battlefield → Graveyard.
                SacrificeSelf(card, owner);

                // CR 701.20 — Scry 2.
                Scry2(owner);
            });

        var scryAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedManaCost),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { scryEffect });

        card.AddAbility(scryAbility);

        return card;
    }

    /// <summary>
    /// CR 701.20 — Scry 2 for <paramref name="player"/>. The decision comes
    /// from the registered <see cref="IPlayerAgent"/> when present; the
    /// pre-agent default sends both peeked cards to the bottom (same fallback
    /// as <see cref="PreordainFactory"/>). No-op on an empty library.
    /// </summary>
    private static void Scry2(Player player)
    {
        var peeked = ScryAction.Peek(player, ScryAmount);
        if (peeked.Count == 0) return;

        var agent = AgentRegistry.Get(player);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            // TODO: drop sync-over-async once IEffect.Execute becomes async.
            decision = agent.ChooseScryDecisionAsync(null, peeked)
                .GetAwaiter().GetResult();
        }
        else
        {
            decision = new ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>());
        }
        ScryAction.Apply(player, peeked.Count, decision);
    }

    /// <summary>
    /// Move <paramref name="card"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield.
    /// </summary>
    private static void SacrificeSelf(Enchantment card, Player owner)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
