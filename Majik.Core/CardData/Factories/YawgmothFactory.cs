using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yawgmoth, Thran Physician (Dominaria, {2}{B}).
///
/// Legendary Creature — Human Cleric 2/4.
/// Oracle text (Scryfall, verified against the embedded seed):
///   "Protection from Humans
///    Pay 1 life, Sacrifice another creature: Put a -1/-1 counter on up to one
///    target creature and draw a card.
///    {B}{B}, Discard a card: Proliferate."
///
/// ## Oracle-drift note (the fix)
/// An EARLIER printing of Yawgmoth read "Pay 1 life, Sacrifice another creature:
/// Each other player loses 1 life and discards a card. Put a -1/-1 counter on up
/// to one target creature. You draw a card." This factory used to implement that
/// stale wording — an "each other player loses 1 life / discards a card" rider
/// driven by a <c>Func&lt;IReadOnlyList&lt;Player&gt;&gt; opponentsResolver</c>
/// captured at factory-build time. That rider had TWO problems:
///   1. It is no longer printed — the current Scryfall oracle (above) has no
///      each-opponent life-loss / discard clause at all.
///   2. Even as written it was INERT on the production routed build: the
///      <c>GameFacade.BuildDeckCard → NamedCardFactory.Create(name, owner, effects)</c>
///      path dispatched the single-arg shape build, leaving the resolver null,
///      so the rider did nothing in real games (only the factory-direct tests
///      that injected a resolver saw it run).
/// Both are resolved by deleting the stale rider and modelling the current
/// oracle: the activated ability's printed effect is the -1/-1 counter on up to
/// one target creature (DEFERRED — needs the ITarget / TargetResolver system)
/// plus "draw a card", which is what the engine now wires.
///
/// ## Implemented (v1)
/// - Legendary 2/4 Creature with Human, Cleric subtypes.
/// - Activated ability cost: Pay 1 life + Sacrifice another creature
///   (<see cref="AdditionalCost.PayLife"/> + <see cref="SacrificeAnotherCreatureCost"/>).
/// - Activated ability effect: controller draws a card (CR 120.1).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — canonical build.
/// - <see cref="Create(Player, Effects.ContinuousEffectsService?)"/> — the
///   effects-aware overload the source generator recognises and the production
///   <c>GameFacade</c> routed build dispatches to (via
///   <see cref="NamedCardFactory.Create(string, Player, Effects.ContinuousEffectsService?)"/>).
///   Yawgmoth registers no continuous effect, so it forwards straight to the
///   canonical overload — its sole purpose is to make the generator emit the
///   effects-aware dispatch arm so the routed prod build dispatches through the
///   named factory (mirrors the Stormbreath Dragon / Priest of Forgotten Gods
///   fix). Without it the routed build still fell through to single-arg dispatch
///   (the draw was controller-captured so it was not inert), but routing through
///   the named factory explicitly keeps the prod path on the same overload the
///   audit / other cards use.
///
/// ## Deferred (v1 gaps)
/// - <b>Protection from Humans (CR 702.16)</b>: no protection-from-subtype
///   infrastructure exists yet. The keyword does not affect gameplay.
/// - <b>"-1/-1 counter on up to one target creature"</b>: skipped — requires
///   the ITarget / TargetResolver targeting system (the optional-target prompt
///   + counter placement). The draw still happens.
/// - <b>"{B}{B}, Discard a card: Proliferate."</b>: the second activated
///   ability is not modelled — no Proliferate primitive exists in the engine
///   today (CR 701.27). Deferred alongside the other counter-manipulation gaps.
/// - <b>Sacrifice target prompt</b>: <see cref="SacrificeAnotherCreatureCost.Target"/>
///   must be set by the agent; v1 falls back to the first eligible creature on
///   the battlefield (deterministic).
/// </summary>
[CardName("Yawgmoth, Thran Physician")]
public static class YawgmothFactory
{
    public const string CardName = "Yawgmoth, Thran Physician";
    public const string PrintedManaCost = "{2}{B}{B}";

    /// <summary>Canonical build — see class xmldoc.</summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 4,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Activated ability (current oracle):
        //   Cost: Pay 1 life, Sacrifice another creature
        //   Effect: Put a -1/-1 counter on up to one target creature
        //           (DEFERRED — needs targeting), then draw a card.
        // ----------------------------------------------------------------

        var sacrificeCost = new SacrificeAnotherCreatureCost(card);

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.PayLife(1),
                sacrificeCost,
            },
            effects: new IEffect[]
            {
                // Effect 1: put a -1/-1 counter on up to one target creature.
                // DEFERRED — requires ITarget / TargetResolver infrastructure.
                // See class xmldoc.

                // Effect 2: controller draws a card (CR 120.1).
                new Effect(
                    $"{CardName}: you draw a card",
                    () =>
                    {
                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            // CR 120.3: drawing from an empty library is noted;
                            // SBA will handle loss at next opportunity.
                            owner.MarkTriedToDrawFromEmptyLibrary();
                            return;
                        }
                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }),
            });

        card.AddAbility(ability);
        return card;
    }

    /// <summary>
    /// Effects-aware overload the source generator recognises and the
    /// <b>production</b> <c>GameFacade</c> routed build dispatches to (via
    /// <see cref="NamedCardFactory.Create(string, Player, Effects.ContinuousEffectsService?)"/>).
    /// Yawgmoth registers no continuous effect, so this forwards straight to the
    /// canonical <see cref="Create(Player)"/> — its sole purpose is to make the
    /// generator emit the effects-aware dispatch arm so the routed prod build
    /// dispatches through this named factory (same fix as Stormbreath Dragon /
    /// Priest of Forgotten Gods). The <paramref name="effects"/> service is
    /// intentionally unused.
    /// </summary>
    public static Creature Create(Player owner, Effects.ContinuousEffectsService? effects) =>
        Create(owner);
}
