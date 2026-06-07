using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
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
/// ## Implemented
/// - Correct name, type (Legendary Artifact), mana cost ({2}), owner/controller.
/// - <b>{T}: Exile target card from a graveyard</b> activated ability with
///   real targeting: "target card from a graveyard" ranges over EVERY
///   player's graveyard, and "target creature you control" is collected as
///   the optional counter recipient. On resolution the chosen card is exiled
///   from whichever graveyard holds it; if it is a creature card it is
///   imprinted (CR 702.49) and the chosen creature you control gets a +1/+1
///   counter.
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
/// ## Deferred
/// - <b>Static: ability-grant via imprint</b>: "creatures you control with
///   +1/+1 counters … have all activated abilities of all creature cards
///   exiled with Agatha's Soul Cauldron" — imprint <em>storage</em> is wired
///   (CR 702.49; <see cref="Majik.Core.Cards.Permanent.ImprintedCards"/>),
///   but the layer-6 grant is NOT. The group-grant primitive
///   (<see cref="Majik.Core.Effects.GrantAbilityToGroupStaticEffect"/>) exists,
///   but it needs an <c>abilityFactory</c> that rebuilds each imprinted
///   creature's activated abilities re-homed to the bearer. The engine's
///   abilities are closures over their original source card (tap costs +
///   effect bodies capture it), so re-homing an ARBITRARY imprinted card's
///   abilities to a different creature is not generally sound — deferred until
///   abilities carry a declarative, re-source-able form.
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
        // CR 605 — not a mana ability; goes on the stack. Targets are
        // collected by the activation flow (CR 602.2b): "target card from a
        // graveyard" ranges over EVERY player's graveyard; the optional
        // "target creature you control" is the counter recipient (collected
        // up front — MinTargets 0 so the ability is still activatable when
        // the controller has no creatures or exiles a non-creature card).
        // ----------------------------------------------------------------
        ActivatedAbility? tapAbility = null;

        var exileEffect = new Effect(
            "Agatha's Soul Cauldron: exile target card from a graveyard, then counter if creature",
            () =>
            {
                if (tapAbility == null) return;
                var chosen = tapAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not ICard target) return;

                // CR 608.2b — the card must still be in a graveyard at
                // resolution. Exile it from whichever graveyard holds it.
                var gyOwner = target.Owner;
                if (gyOwner == null || target.Zone != ZoneType.Graveyard) return;

                gyOwner.Zones.Graveyard.RemoveCard(target);
                gyOwner.Zones.Exile.AddCard(target);
                target.SetZone(ZoneType.Exile);

                if (target.HasType(CardType.Creature))
                {
                    // CR 702.49 — imprint: record this creature card on the
                    // Cauldron so the ability-grant static can reference it
                    // (layer-6 grant deferred; storage wired here).
                    cauldron.AddImprinted(target);

                    // "put a +1/+1 counter on target creature you control" —
                    // the recipient chosen up front (request index 1). No-op
                    // when none was chosen (no legal creature).
                    if (chosen.Count > 1 && chosen[1].Count > 0
                        && chosen[1][0] is Creature recipient)
                    {
                        recipient.Counters.Add(
                            Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);
                    }
                }
            });

        tapAbility = new ActivatedAbility(
            source: cauldron,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(cauldron) },
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "exile target card from a graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    // "a graveyard" — any player's graveyard (CR 109 / 400.3).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Graveyard.GetCards())
                        .Cast<object>()
                        .ToList()),
                new TargetRequest(
                    Description: "target creature you control (counter recipient)",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    // "creature you control" — the Cauldron controller's creatures.
                    CandidateGatherer: ctx =>
                    {
                        var controller = cauldron.Controller ?? owner;
                        return controller.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .Cast<object>()
                            .ToList();
                    }),
            });

        cauldron.AddAbility(tapAbility);

        return cauldron;
    }
}
