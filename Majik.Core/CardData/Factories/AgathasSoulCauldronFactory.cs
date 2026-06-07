using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Agatha's Soul Cauldron (Wilds of Eldraine).
///
/// Legendary Artifact — {2}. Oracle text:
///   "You may spend mana as though it were mana of any color to activate
///    abilities of creatures you control.
///    Creatures you control with +1/+1 counters on them have all activated
///    abilities of all creature cards exiled with Agatha's Soul Cauldron.
///    {T}: Exile target card from a graveyard. When a creature card is
///    exiled this way, put a +1/+1 counter on target creature you control."
///
/// ## Implemented (v1)
/// - Correct name, type (Artifact), mana cost ({2}), owner/controller.
/// - <b>{T}: Exile target card from a graveyard</b> activated ability:
///   v1 auto-selects the first card from the controller's graveyard and
///   exiles it. If the exiled card is a creature card, a +1/+1 counter is
///   placed on the first creature the controller controls on the battlefield.
///   (Full targeting deferred — see below.)
/// - <b>Static: mana-colour-substitution</b> (CR 609.4b): "you may spend mana
///   as though it were mana of any color to activate abilities of creatures
///   you control" is wired as a
///   <see cref="Majik.Core.Costs.ManaColorSubstitutionPermission"/> with
///   <see cref="Majik.Core.Costs.ManaSpendPurpose.ActivateCreatureAbilities"/>.
///   The reusable payment-time substitution primitive (shared with Robber of
///   the Rich's clause) folds a creature ability's coloured pips into generic
///   when this permission is active, so a
///   <see cref="Majik.Core.Costs.ManaColorSubstitutableManaCost"/> used as the
///   mana component of a creature's activated ability accepts any colour. The
///   mana value is unchanged (CR 106.6) — only which mana qualifies widens.
///
/// ## Deferred (v1 gaps)
/// - <b>Graveyard targeting</b>: "target card from a graveyard" should
///   prompt for any card in any graveyard (all players). v1 auto-picks from
///   the controller's own graveyard only. Deferred until target-selection
///   prompts support graveyard-range filtering.
/// - <b>+1/+1 counter targeting</b>: "target creature you control" for the
///   counter placement should prompt the controller. v1 auto-picks the first
///   creature on the controller's battlefield. Deferred alongside graveyard
///   targeting.
/// - <b>Static: ability-grant via imprint</b>: "creatures you control with
///   +1/+1 counters … have all activated abilities of all creature cards
///   exiled" — imprint <em>storage</em> is wired (CR 702.49;
///   <see cref="Majik.Core.Cards.Permanent.ImprintedCards"/>). The layer-6
///   continuous effect that actually grants those abilities to battlefield
///   creatures is deferred until the layer-6 ability-grant subsystem is in
///   place.
/// </summary>
[CardName("Agatha's Soul Cauldron")]
public static class AgathasSoulCauldronFactory
{
    /// <summary>
    /// Construct Agatha's Soul Cauldron owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Legendary Artifact — the printed supertype must be set here: the
        // routed prod build path (NamedCardFactory.Create + OverlayAdditiveBinders)
        // overlays only keyword/mana/ETB binders, NOT supertypes, so a factory
        // that omits Legendary would lose the legend rule (CR 704.5j).
        var cauldron = new Artifact(
            "Agatha's Soul Cauldron", "{2}",
            supertypes: new[] { CardSupertype.Legendary });
        cauldron.SetOwner(owner);
        cauldron.SetController(owner);

        // ----------------------------------------------------------------
        // "You may spend mana as though it were mana of any color to activate
        // abilities of creatures you control." (CR 609.4b.)
        //
        // Reusable payment-time mana-colour-substitution permission. A
        // creature's activated ability whose mana component is a
        // ManaColorSubstitutableManaCost(..., ManaSpendPurpose
        // .ActivateCreatureAbilities) consults this static and folds its
        // coloured pips to generic while the Cauldron is on the battlefield —
        // the same permissive fold Robber of the Rich's clause uses, generalized
        // into a static the payment path queries.
        // ----------------------------------------------------------------
        cauldron.AddAbility(new ManaColorSubstitutionPermission(
            cauldron, owner, ManaSpendPurpose.ActivateCreatureAbilities));

        // ----------------------------------------------------------------
        // {T}: Exile target card from a graveyard.
        // When a creature card is exiled this way, put a +1/+1 counter on
        // target creature you control.
        //
        // CR 605 — not a mana ability; goes on the stack.
        // v1: auto-selects first card from controller's graveyard; if it is
        // a creature card, bumps first creature on controller's battlefield.
        // Full targeting deferred (see xmldoc above).
        // ----------------------------------------------------------------
        var exileEffect = new Effect(
            "Agatha's Soul Cauldron: exile from graveyard, then counter if creature",
            () =>
            {
                var target = owner.Zones.Graveyard.GetCards().FirstOrDefault();
                if (target == null) return; // graveyard empty — no-op

                owner.Zones.Graveyard.RemoveCard(target);
                owner.Zones.Exile.AddCard(target);
                target.SetZone(ZoneType.Exile);

                if (target.HasType(CardType.Creature))
                {
                    // CR 702.49 — imprint: record this creature card on the
                    // Cauldron so the ability-grant static ability can reference
                    // it later (layer-6 grant deferred; storage wired here).
                    cauldron.AddImprinted(target);

                    // Put a +1/+1 counter on target creature you control.
                    // v1: auto-picks the first creature on the battlefield.
                    var creatureToBuff = owner.Zones.Battlefield
                        .GetCards()
                        .OfType<Creature>()
                        .FirstOrDefault();

                    creatureToBuff?.Counters.Add(
                        Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);
                }
            });

        var tapAbility = new ActivatedAbility(
            source: cauldron,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(cauldron) },
            effects: new IEffect[] { exileEffect });

        cauldron.AddAbility(tapAbility);

        return cauldron;
    }
}
