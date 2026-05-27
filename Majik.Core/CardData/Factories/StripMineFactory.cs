using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Strip Mine (Antiquities / reprints).
///
/// Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {T}, Sacrifice Strip Mine: Destroy target land."
///
/// ## Implemented (v1)
/// - Land identity (nonbasic, no printed supertype, no subtype).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack).
/// - <b>{T}, Sacrifice Strip Mine: Destroy target land</b> — wired as an
///   <see cref="ActivatedAbility"/> with <see cref="AdditionalCost.Tap"/>
///   plus an inline sacrifice-self payment. Unlike Wasteland the target
///   predicate is <c>any land</c> — basic lands are legal targets
///   (cf. CR 109.3 / 205.4a; nothing in the oracle text restricts to
///   nonbasic). The resolution effect guards target legality (still on the
///   battlefield, still a Land) per CR 608.2b and routes the destroyed
///   land to its owner's graveyard (mirrors <see cref="WastelandFactory"/>).
/// - <b>Instant speed</b>: Strip Mine's activated ability has no sorcery-
///   speed rider (CR 602.5b — printed activation timing defaults to
///   instant unless the oracle text says otherwise).
///
/// ## Deferred (v1 gaps)
/// - <b><see cref="AdditionalCost.Sacrifice"/> Pay()</b> is still a no-op
///   stub at the engine level; the self-sacrifice happens inside the
///   effect closure (same shape as Wasteland / Engineered Explosives /
///   Mishra's Bauble).
/// - <b><see cref="Rules.ActionValidator"/> target legality</b> does not
///   yet restrict the agent's target list to Lands. The resolution-time
///   guard handles illegal picks (CR 608.2b); tests exercise the legal
///   path.
/// - <b><see cref="Services.ZoneService"/> routing</b>: raw zone
///   manipulation mirrors Wasteland's destroy primitive — does not emit
///   <see cref="Events.CardMovedEvent"/> via this path. Wire ZoneService
///   through when the broader destroy-pipeline pass lands.
/// </summary>
[CardName("Strip Mine")]
public static class StripMineFactory
{
    public const string CardName = "Strip Mine";

    /// <summary>
    /// Construct Strip Mine owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {T}, Sacrifice Strip Mine: Destroy target land.
        //
        // CR 602 — activated ability with a single target requirement
        // (Rule 602.2b). Costs:
        //   - {T} via AdditionalCost.Tap(land)
        // Self-sacrifice happens inside the effect closure because
        // AdditionalCost.Sacrifice's Pay() is a no-op stub. The resolution
        // effect reads ChosenTargets and gates on Land at resolution
        // (CR 608.2b — illegal target → effect does nothing).
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            "Strip Mine: destroy target land",
            () =>
            {
                if (destroyAbility == null) return;

                // Self-sacrifice (cost paid on activation; visible state
                // catches up here while AdditionalCost.Sacrifice is a stub).
                SacrificeToOwnersGraveyard(land);

                if (destroyAbility.ChosenTargets.Count == 0) return;
                if (destroyAbility.ChosenTargets[0].Count == 0) return;

                var chosen = destroyAbility.ChosenTargets[0][0];
                if (chosen is not ICard card) return;
                if (!card.HasType(CardType.Land)) return;
                if (card.Owner == null) return;
                if (card.Zone != ZoneType.Battlefield) return;

                DestroyToOwnersGraveyard(card);
            });

        destroyAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(destroyAbility);

        return land;
    }

    /// <summary>
    /// Move <paramref name="self"/> from the battlefield to its owner's
    /// graveyard as the sacrifice payment for the activated ability.
    /// Mirrors <see cref="WastelandFactory"/>'s sac primitive.
    /// </summary>
    private static void SacrificeToOwnersGraveyard(Land self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Move the destroyed target <paramref name="card"/> from the
    /// battlefield to its owner's graveyard (CR 701.7b).
    /// </summary>
    private static void DestroyToOwnersGraveyard(ICard card)
    {
        var ownerOfCard = card.Owner;
        if (ownerOfCard == null) return;

        var holder = card.Controller ?? ownerOfCard;
        holder.Zones.Battlefield.RemoveCard(card);
        ownerOfCard.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
