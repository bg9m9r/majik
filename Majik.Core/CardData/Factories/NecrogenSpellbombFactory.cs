using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Necrogen Spellbomb (Mirrodin / reprints).
///
/// Artifact — {1}. Oracle text (Scryfall, verified):
///   "{B}, Sacrifice this artifact: Target player discards a card.
///    {1}, Sacrifice this artifact: Draw a card."
///
/// ## Shape source
///
/// Card identity (name, {1}, Artifact) is loaded from
/// <c>Majik.Core/CardData/Cards/necrogen-spellbomb.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The two activated abilities are wired
/// in code below.
///
/// ## Implemented (v1)
/// - <b>{B}, Sacrifice: Target player discards a card</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>("{B}")
///   plus <see cref="AdditionalCost"/>.Sacrifice on the spellbomb itself.
///   A single 1..1 "target player" <see cref="TargetRequest"/> is declared so
///   the activating player's agent picks a player target at activation
///   (CR 602.2b). On resolution the chosen player discards one card. The
///   <i>discarding player chooses</i> which card (CR 701.7a) — modelled with
///   the deterministic first-card pick used by Mind Rot's no-agent fallback,
///   matching the analogue's agent-free resolution posture. CR 701.7c — a
///   player with an empty hand discards nothing (no crash).
///   This mirrors <see cref="AetherSpellbombFactory"/>; only the colored mode
///   differs (targeted discard vs. creature bounce).
/// - <b>{1}, Sacrifice: Draw a card</b> — second
///   <see cref="ActivatedAbility"/> on the same card. <see cref="ManaCostCost"/>("{1}")
///   plus self-sacrifice; resolution moves the spellbomb to its owner's
///   graveyard and draws one card for the controller.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter the target to "is a Player" — resolution-time guard handles
///   illegal targets (CR 608.2b — an effect with an illegal target does
///   nothing).
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op stub.
///   The effect closure performs the zone move so behavior is observable —
///   same posture as <see cref="AetherSpellbombFactory"/>. Remove the explicit
///   move-to-graveyard once <see cref="AdditionalCost.Pay"/> performs the
///   sacrifice itself.
/// - <b>Agent-driven discard pick</b>: the discarding player's choice falls
///   back to the deterministic first card (CR 701.7a leaves the choice to that
///   player). Upgrade to <see cref="IPlayerAgent.ChooseFromHandAsync"/>
///   (<see cref="BotIntent.Discard"/>) when the activated-ability path threads
///   the target player's agent, mirroring <see cref="MindRotFactory"/>.
/// </summary>
[CardName("Necrogen Spellbomb")]
public static class NecrogenSpellbombFactory
{
    public const string CardName = "Necrogen Spellbomb";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("necrogen-spellbomb");

    /// <summary>
    /// Construct Necrogen Spellbomb owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var spellbomb = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        spellbomb.SetOwner(owner);
        spellbomb.SetController(owner);

        // ----------------------------------------------------------------
        // {B}, Sacrifice this artifact: Target player discards a card.
        // CR 602 — activated ability with a single 1..1 "target player"
        // request. The resolution effect reads ChosenTargets and gates on
        // the Player type at resolution (CR 608.2b — illegal target →
        // effect does nothing).
        // ----------------------------------------------------------------
        ActivatedAbility? discardAbility = null;
        var discardEffect = new Effect(
            "Necrogen Spellbomb: target player discards a card + sac self",
            () =>
            {
                if (discardAbility != null
                    && discardAbility.ChosenTargets.Count > 0
                    && discardAbility.ChosenTargets[0].Count > 0
                    && discardAbility.ChosenTargets[0][0] is Player victim)
                {
                    DiscardOneCard(victim);
                }

                SacrificeSelf(spellbomb, owner);
            });

        discardAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{B}"),
                AdditionalCost.Sacrifice(spellbomb),
            },
            effects: new IEffect[] { discardEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        spellbomb.AddAbility(discardAbility);

        // ----------------------------------------------------------------
        // {1}, Sacrifice this artifact: Draw a card.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Necrogen Spellbomb: draw a card + sac self",
            () =>
            {
                SacrificeSelf(spellbomb, owner);

                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty-library loss handled by SBAs
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Sacrifice(spellbomb),
            },
            effects: new IEffect[] { drawEffect });

        spellbomb.AddAbility(drawAbility);

        return spellbomb;
    }

    /// <summary>
    /// The target player discards one card of their own choice
    /// (CR 701.7a). Deterministic first-card pick in the absence of a wired
    /// agent — mirrors <see cref="MindRotFactory"/>'s no-agent fallback.
    /// CR 701.7c — an empty hand discards nothing.
    /// </summary>
    private static void DiscardOneCard(Player victim)
    {
        var pick = victim.Zones.Hand.GetCards().FirstOrDefault();
        if (pick == null) return; // CR 701.7c — nothing to discard.

        victim.Zones.Hand.RemoveCard(pick);
        victim.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Move the spellbomb from the battlefield to its owner's graveyard.
    /// Defensive against double-execution (idempotent if already
    /// sacrificed). Mirrors <see cref="AetherSpellbombFactory"/> — the
    /// generic <see cref="AdditionalCost.Pay"/> sacrifice path is a stub.
    /// </summary>
    private static void SacrificeSelf(Artifact spellbomb, Player owner)
    {
        if (spellbomb.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(spellbomb);
        owner.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);
    }
}
