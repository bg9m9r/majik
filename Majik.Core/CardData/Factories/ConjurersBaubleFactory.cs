using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conjurer's Bauble (Modern Horizons 2). Artifact —
/// {0}. Oracle text:
///
///   "{1}, {T}, Sacrifice Conjurer's Bauble: Put target card from your
///    graveyard on the bottom of your library. Draw a card."
///
/// Third member of the zero-cost bauble cycle (sister cards: Mishra's
/// Bauble, Urza's Bauble). Unlike its siblings — which sacrifice for an
/// information peek + delayed upkeep draw — Conjurer's Bauble pays {1}
/// extra to immediately recycle a graveyard card and cantrip.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {0}, owner / controller wiring).
/// - <b>Activated ability</b>: {1} + {T} + Sacrifice (self) — wired via
///   <see cref="ManaCostCost"/>("{1}") + <see cref="AdditionalCost.Tap"/>
///   + <see cref="AdditionalCost.Sacrifice"/>. The sacrifice cost is
///   <em>declared</em> on the ability; the engine's generic
///   <see cref="AdditionalCost.Pay"/> sacrifice path is currently a stub,
///   so the effect closure performs the move-to-graveyard directly —
///   same posture as Mishra's / Urza's Bauble + every other v1 self-sac
///   factory (Pyrite Spellbomb, Aether Spellbomb, Expedition Map).
/// - <b>Target card in controller's graveyard</b>: a single
///   <see cref="TargetRequest"/> declared so the activating player's
///   agent picks one graveyard card at activation (CR 602.2b).
///   Resolution reads <see cref="ActivatedAbility.ChosenTargets"/> and
///   moves the picked card Graveyard -> bottom of Library — mirrors
///   <see cref="GoblinCharbelcherFactory"/>'s <c>library.AddCard(card)</c>
///   append-as-bottom convention. CR 608.2b — if the chosen target is
///   no longer in the controller's graveyard at resolution, the move
///   step is skipped (illegal target), but the cantrip still happens
///   because the sacrifice cost was already paid.
/// - <b>Draw a card</b>: routes through <see cref="Fx.DrawCards"/>(owner, 1)
///   — same as Pyrite Spellbomb's {R} mode. Empty library is a silent
///   no-op; SBAs (CR 704.5b / CR 120.3) handle the loss condition.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: see class-level note above —
///   <see cref="AdditionalCost.Pay"/> sacrifice path is a stub. Remove
///   the explicit move-to-graveyard once the engine wires sacrifice
///   payment for real.
/// </summary>
[CardName("Conjurer's Bauble")]
public static class ConjurersBaubleFactory
{
    public const string CardName = "Conjurer's Bauble";
    public const string PrintedManaCost = "{0}";

    /// <summary>
    /// Construct Conjurer's Bauble owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var bauble = new Artifact(CardName, PrintedManaCost);
        bauble.SetOwner(owner);
        bauble.SetController(owner);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice Conjurer's Bauble: Put target card from your
        // graveyard on the bottom of your library. Draw a card.
        // CR 602 — activated ability with three costs. One target request
        // for the graveyard pick (CR 602.2b). Cantrip is unconditional
        // because the cost was paid (CR 608.2b — illegal target only
        // skips the dependent move step).
        // ----------------------------------------------------------------
        ActivatedAbility? recycleAbility = null;
        var recycleEffect = new Effect(
            "Conjurer's Bauble: bottom target GY card, draw a card, sac self",
            () =>
            {
                // Move the bottom-target card first — same observable order
                // as the oracle text. Skipped silently if the target became
                // illegal between activation and resolution (CR 608.2b).
                if (recycleAbility != null
                    && recycleAbility.ChosenTargets.Count > 0
                    && recycleAbility.ChosenTargets[0].Count > 0
                    && recycleAbility.ChosenTargets[0][0] is ICard target
                    && target.Zone == ZoneType.Graveyard
                    && owner.Zones.Graveyard.GetCards().Contains(target))
                {
                    owner.Zones.Graveyard.RemoveCard(target);
                    owner.Zones.Library.AddCard(target); // Append == bottom.
                    target.SetZone(ZoneType.Library);
                }

                SacrificeSelf(bauble, owner);
                Fx.DrawCards(owner, 1);
            });

        recycleAbility = new ActivatedAbility(
            source: bauble,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(bauble),
                AdditionalCost.Sacrifice(bauble),
            },
            effects: new IEffect[] { recycleEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        bauble.AddAbility(recycleAbility);

        return bauble;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="bauble"/> from the battlefield to
    /// its owner's graveyard. Idempotent. Mirrors the closure used by
    /// Mishra's / Urza's / Pyrite / Aether / Nihil Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact bauble, Player owner)
    {
        if (bauble.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(bauble);
        owner.Zones.Graveyard.AddCard(bauble);
        bauble.SetZone(ZoneType.Graveyard);
    }
}
