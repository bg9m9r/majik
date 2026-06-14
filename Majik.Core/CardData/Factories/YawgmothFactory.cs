using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
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
/// one target creature plus "draw a card", which is what the engine now wires.
///
/// ## Implemented (v1)
/// - Legendary 2/4 Creature with Human, Cleric subtypes.
/// - Activated ability cost: Pay 1 life + Sacrifice another creature
///   (<see cref="AdditionalCost.PayLife"/> + <see cref="SacrificeAnotherCreatureCost"/>).
/// - Activated ability effect: put a -1/-1 counter on UP TO ONE target creature
///   (CR 115.1b — an optional <see cref="TargetRequest"/> with MinTargets 0 /
///   MaxTargets 1; the controller may decline), then the controller draws a
///   card (CR 120.1). The counter half reads <see cref="ActivatedAbility.ChosenTargets"/>
///   and stamps one -1/-1 counter (CR 122) via <see cref="Fx.PlaceCounter"/>;
///   the draw happens whether or not a target was chosen.
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
/// ## Re-source-safe (agatha-oracle-shape-yawgmoth-pay-life-counter-pump-loop)
/// The activated ability is marked <see cref="ActivatedAbility.RebindSafe"/>:
/// the counter half reads its target off the live
/// <see cref="Abilities.ResolutionContext.ChosenTargets"/> and the draw half
/// reads the resolving ability's <see cref="Abilities.ResolutionContext.Controller"/>
/// (not the captured <c>owner</c>), so a re-sourced copy affects the new
/// source's controller. The multi-leg cost (pay 1 life + sacrifice another
/// creature) re-homes too: <see cref="AdditionalCost.PayLife"/> captures no
/// permanent, and <see cref="SacrificeAnotherCreatureCost"/> implements
/// <see cref="Costs.IRebindableCost"/> so
/// <see cref="ActivatedAbility.RebindTo"/> swaps its captured "another creature"
/// exclusion from the original Yawgmoth to the new source. This unblocks
/// Agatha's Soul Cauldron re-homing the REAL ability (the oracle-rebuild
/// fallback cannot reconstruct a multi-leg pay-life + sacrifice cost) to a
/// counter-bearing creature (CR 707.2 / 613.1f).
///
/// ## Deferred (v1 gaps)
/// - <b>Protection from Humans (CR 702.16)</b>: no protection-from-subtype
///   infrastructure exists yet. The keyword does not affect gameplay.
/// - <b>"{B}{B}, Discard a card: Proliferate."</b>: the second activated
///   ability is not modelled — no Proliferate primitive exists in the engine
///   today (CR 701.27). Deferred alongside the other counter-manipulation gaps.
/// ## Sacrifice cost — prompted (the fix)
/// - The "Sacrifice another creature" cost now implements
///   <see cref="Costs.IChooseCreatureToSacrificeCost"/>, so the activation
///   dispatch (<c>GameFacade.DispatchActivate</c> / <c>TurnDriver.DispatchActivate</c>)
///   prompts the controller — via the existing <c>ChooseAsync</c> sink the
///   portal renders as a <c>ChoiceCommand</c> — to choose WHICH creature to
///   sacrifice BEFORE the cost is paid (CR 700.6). Previously the cost silently
///   auto-picked the first eligible creature (live-play bug). With exactly one
///   eligible creature the engine skips the prompt and uses it.
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
        //           (CR 115.1b — optional target), then draw a card.
        // ----------------------------------------------------------------

        var sacrificeCost = new SacrificeAnotherCreatureCost(card);

        // Holds the constructed ability so it can be attached at the end. The
        // effects read their chosen targets / controller off the live
        // ResolutionContext (not this captured reference), which is what makes
        // the ability re-source-safe (see rebindSafe below).
        ActivatedAbility ability;

        // Effect 1: put a -1/-1 counter on UP TO ONE target creature (CR 115.1b
        // — an optional target; the controller may choose ZERO or ONE). Reads
        // the chosen target off the live ResolutionContext (so a re-homed copy
        // reads the rebound ability's own targets); no-ops cleanly when the
        // player declined or the target became illegal (CR 608.2b).
        //
        // RE-SOURCE-SAFE (STAGE 2/3, agatha-oracle-shape-yawgmoth-pay-life-
        // counter-pump-loop): the counter half targets a CHOSEN creature (not
        // Yawgmoth's source), so it is already source-agnostic. We read
        // ctx.ChosenTargets rather than the captured `ability` so a RebindTo
        // copy resolves against ITS chosen targets, not the original ability's.
        var counterEffect = new Effect(
            $"{CardName}: put a -1/-1 counter on up to one target creature",
            ctx =>
            {
                if (ctx.ChosenTargets.Count == 0) return ValueTask.CompletedTask;
                if (ctx.ChosenTargets[0].Count == 0) return ValueTask.CompletedTask; // declined the optional target

                if (ctx.ChosenTargets[0][0] is not Creature target) return ValueTask.CompletedTask;
                if (!target.HasType(CardType.Creature)) return ValueTask.CompletedTask;
                if (target.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;

                // CR 122 — put one -1/-1 counter on the chosen creature. Any
                // creature (Yawgmoth's or an opponent's) is a legal target.
                // Subsequent SBAs (CR 704.5q — toughness 0 → graveyard) are the
                // engine's responsibility once the counter lands.
                Fx.PlaceCounter(target, CounterType.MinusOneMinusOne, 1);
                return ValueTask.CompletedTask;
            });

        // Effect 2: controller draws a card (CR 120.1) — happens whether or not
        // a counter target was chosen ("then draw a card").
        //
        // RE-SOURCE-SAFE: the drawer is the resolving ability's CONTROLLER, read
        // off the live ResolutionContext (ctx.Controller) rather than the
        // captured `owner`. On a RebindTo copy ctx.Controller is the BEARER's
        // controller, so the re-homed Yawgmoth ability draws for the bearer's
        // controller — never the exiled Yawgmoth's owner. Falls back to `owner`
        // only on the context-less legacy sync path (ResolutionContext.Legacy,
        // where Controller is null).
        var drawEffect = new Effect(
            $"{CardName}: you draw a card",
            ctx =>
            {
                var drawer = ctx.Controller ?? owner;
                var top = drawer.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 120.3: drawing from an empty library is noted;
                    // SBA will handle loss at next opportunity.
                    drawer.MarkTriedToDrawFromEmptyLibrary();
                    return ValueTask.CompletedTask;
                }
                drawer.Zones.Library.RemoveCard(top);
                drawer.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
                return ValueTask.CompletedTask;
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.PayLife(1),
                sacrificeCost,
            },
            effects: new IEffect[] { counterEffect, drawEffect },
            // RE-SOURCE-SAFE: every effect reads its subject/controller off the
            // live ResolutionContext (chosen targets + controller) and the
            // SacrificeAnotherCreatureCost re-homes its captured source via
            // IRebindableCost, so the whole ability is sound to re-home via
            // ActivatedAbility.RebindTo (CR 707.2). Lets Agatha's Soul Cauldron
            // grant the REAL multi-leg-cost ability to a bearer.
            rebindSafe: true,
            targetRequests: new[]
            {
                // CR 115.1b — "up to one target creature": MinTargets 0 (the
                // controller may decline) / MaxTargets 1. The CandidateGatherer
                // enumerates every creature on the battlefield (either player's)
                // so the prompt ships the legal pool the portal renders, and
                // declining (zero picks) is honoured.
                new TargetRequest(
                    Description: "up to one target creature",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
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
