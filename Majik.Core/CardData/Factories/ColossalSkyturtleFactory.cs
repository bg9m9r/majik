using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Colossal Skyturtle (The Lost Caverns of Ixalan,
/// {4}{G}{G}{U}).
///
/// Enchantment Creature — Turtle 6/5. Oracle text (Scryfall, verified):
///   "Flying, ward {2}
///    Channel — {2}{G}, Discard this card: Return target card from your
///    graveyard to your hand.
///    Channel — {1}{U}, Discard this card: Return target creature to its
///    owner's hand."
///
/// ## Implementation
///
/// - 6/5 <see cref="Creature"/> with <see cref="CardSubtype.Turtle"/>, mana
///   cost {4}{G}{G}{U} (MV 7, green+blue — CR 202.3 / CR 105.1). Enchantment
///   Creature modelled as plain <see cref="Creature"/> per v1 convention
///   (Sanctum Weaver / Reflection deferral — v1-deferrals #10).
///
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker. Combat
///   block-restriction path reads the marker — same shape as
///   <see cref="AirElementalFactory"/>.
///
/// - <b>Ward {2} (CR 702.21)</b>: <see cref="KeywordAbility"/> marker plus a
///   public <see cref="BuildWardEffect"/> builder. Same posture as
///   <see cref="TolarianTerrorFactory"/> — marker keeps keyword-scan surface
///   uniform; <see cref="WardEffect.ResolvesWard"/> is the spell-resolve hook.
///   Battlefield-attached triggered ability is deferred (v1 gap).
///
/// - <b>Channel 1 — {2}{G}, Discard this card (CR 702.74)</b>: activated
///   ability gated to the hand by <see cref="DiscardSelfCost"/> (CR 702.74a).
///   On resolve: return target card from the channel-controller's graveyard
///   to that player's hand (any card type — mirrors
///   <see cref="EternalWitnessFactory"/>). CR 608.2b re-check: if the chosen
///   card has left the graveyard, the effect fizzles.
///
/// - <b>Channel 2 — {1}{U}, Discard this card (CR 702.74)</b>: second
///   activated ability, same discard-gate. On resolve: return target creature
///   to its owner's hand — any creature on any battlefield (opponents'
///   creatures included), mirrors <see cref="BoomerangFactory"/> / VaporSnag's
///   bounce shape but creature-only and no life-loss rider.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Ward {2} trigger wiring</b>: marker + BuildWardEffect exposed; the
///   battlefield-attached triggered-ability surface is deferred — same gap as
///   Tolarian Terror / Kappa Cannoneer.
/// - <b>Agent-driven target prompt for Channel 1</b>: auto-picks the first
///   graveyard card when no agent target is set (same posture as Eternal
///   Witness / Wishclaw Talisman first-card fallback).
/// </summary>
[CardName("Colossal Skyturtle")]
public static class ColossalSkyturtleFactory
{
    public const string CardName = "Colossal Skyturtle";
    public const string PrintedManaCost = "{4}{G}{G}{U}";
    public const int Power = 6;
    public const int Toughness = 5;

    /// <summary>CR 702.21 — printed Ward cost: {2}.</summary>
    public const string WardCost = "{2}";

    /// <summary>CR 702.74 — Channel 1 mana cost: {2}{G}.</summary>
    public const string Channel1ManaCost = "{2}{G}";

    /// <summary>CR 702.74 — Channel 2 mana cost: {1}{U}.</summary>
    public const string Channel2ManaCost = "{1}{U}";

    /// <summary>
    /// CR 702.21 — Colossal Skyturtle's Ward {2} effect, bound to the
    /// supplied <paramref name="card"/>. v1 exposes this as a builder so the
    /// spell-resolution path can opt-in once the Ward trigger primitive
    /// lands. Mirrors <see cref="TolarianTerrorFactory.BuildWardEffect"/>.
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, ManaCost.Parse(WardCost));

    /// <summary>
    /// Construct Colossal Skyturtle owned and controlled by
    /// <paramref name="owner"/>. Flying + Ward {2} markers are attached,
    /// and both Channel activated abilities are wired.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 205.2a — Enchantment Creature: Creature + Enchantment card types.
        var card = PermanentBuilders.EnchantmentCreature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Turtle });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying (CR 702.9) — combat block-restriction marker.
        // Same shape as AirElementalFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Ward {2} (CR 702.21) — marker keyword. WardEffect builder exposed
        // via BuildWardEffect for the spell-resolution opt-in path.
        // Battlefield-attached trigger deferred (v1 gap — same as Tolarian
        // Terror / Kappa Cannoneer).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // ----------------------------------------------------------------
        // Channel 1 — {2}{G}, Discard this card (CR 702.74):
        //   Return target card from your graveyard to your hand.
        //
        // DiscardSelfCost gates activation to ZoneType.Hand (CR 702.74a).
        // Target: any card in the channel-controller's graveyard (all card
        // types — mirrors EternalWitness). CR 608.2b re-check on resolve.
        // ----------------------------------------------------------------
        AttachChannel1GraveyardReturn(card, owner);

        // ----------------------------------------------------------------
        // Channel 2 — {1}{U}, Discard this card (CR 702.74):
        //   Return target creature to its owner's hand.
        //
        // Target: any creature on any battlefield (not controller-restricted
        // — oracle reads "target creature", not "you control"). Mirrors
        // VaporSnag's Bounce shape without the life-loss rider.
        // ----------------------------------------------------------------
        AttachChannel2CreatureBounce(card, owner);

        return card;
    }

    // -----------------------------------------------------------------------
    // Channel 1 — graveyard-to-hand
    // -----------------------------------------------------------------------

    private static void AttachChannel1GraveyardReturn(Creature card, Player owner)
    {
        ActivatedAbility? channel = null;

        var targetRequests = new[]
        {
            new TargetRequest(
                Description: "target card in your graveyard",
                MinTargets: 1,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Tutor,
                // Any card in the controller's graveyard (all types). Live
                // gather at ability-creation time; production path refreshes
                // LegalCandidates at resolve via agent prompt (EternalWitness
                // / Wishclaw posture).
                CandidateGatherer: ctx =>
                {
                    var controller = card.Controller ?? owner;
                    return controller.Zones.Graveyard.GetCards()
                        .Cast<object>()
                        .ToList();
                }),
        };

        var effect = new Effect(
            $"{CardName} (Channel 1): return target card from your graveyard to your hand",
            () =>
            {
                var controller = card.Controller ?? owner;

                // 1) Honour the agent-set target if present (production path).
                ICard? picked = null;
                if (channel!.ChosenTargets.Count > 0
                    && channel.ChosenTargets[0].Count > 0
                    && channel.ChosenTargets[0][0] is ICard chosen)
                {
                    picked = chosen;
                }

                // 2) Deterministic fallback — first card in graveyard
                //    (shape-test / no-agent posture — mirrors EternalWitness).
                picked ??= controller.Zones.Graveyard.GetCards().FirstOrDefault();

                // Empty graveyard → clean no-op.
                if (picked == null) return;

                // CR 608.2b — target must still be in the controller's
                // graveyard at resolution.
                if (picked.Zone != ZoneType.Graveyard) return;
                if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;

                // Move Graveyard → Hand (direct zone mutation; ZoneService
                // path omitted — same raw-zone posture as EternalWitness
                // shape-only mode).
                controller.Zones.Graveyard.RemoveCard(picked);
                controller.Zones.Hand.AddCard(picked);
                picked.SetZone(ZoneType.Hand);
            });

        channel = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(Channel1ManaCost),
                new DiscardSelfCost(card),
            },
            effects: new IEffect[] { effect },
            targetRequests: targetRequests);

        card.AddAbility(channel);
    }

    // -----------------------------------------------------------------------
    // Channel 2 — creature bounce to owner's hand
    // -----------------------------------------------------------------------

    private static void AttachChannel2CreatureBounce(Creature card, Player owner)
    {
        ActivatedAbility? channel = null;

        var targetRequests = new[]
        {
            new TargetRequest(
                Description: "target creature",
                MinTargets: 1,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Bounce,
                // Any creature on any player's battlefield — not restricted
                // to controller-side (oracle: "target creature").
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .OfType<Creature>()
                    .Cast<object>()
                    .ToList()),
        };

        var effect = new Effect(
            $"{CardName} (Channel 2): return target creature to its owner's hand",
            () =>
            {
                if (channel!.ChosenTargets.Count == 0
                    || channel.ChosenTargets[0].Count == 0) return;

                var raw = channel.ChosenTargets[0][0];

                // CR 608.2b — target must still be a creature on the
                // battlefield at resolution.
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;

                var targetOwner = target.Owner;
                if (targetOwner == null) return;

                var controller = target.Controller ?? targetOwner;

                // CR 701.10 — return to owner's hand.
                controller.Zones.Battlefield.RemoveCard(target);
                targetOwner.Zones.Hand.AddCard(target);
                target.SetZone(ZoneType.Hand);
                target.SetController(targetOwner);
            });

        channel = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(Channel2ManaCost),
                new DiscardSelfCost(card),
            },
            effects: new IEffect[] { effect },
            targetRequests: targetRequests);

        card.AddAbility(channel);
    }
}
