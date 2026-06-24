using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Carrot Cake (Bloomburrow Commander, {1}{W}).
///
/// Artifact — Food. Oracle text (verified against the embedded Scryfall seed):
///   "When this artifact enters and when you sacrifice it, create a 1/1 white
///    Rabbit creature token and scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {2}, {T}, Sacrifice this artifact: You gain 3 life."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {1}{W}; mana value 2; colors W.</item>
///   <item>Type line: Artifact — Food (CR 301 / CR 205.3 Food subtype).</item>
/// </list>
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Artifact {1}{W} with the Food subtype, owner /
///   controller stamped. Colour W is stamped explicitly (an artifact has no
///   coloured pips in its mana cost layer, but Carrot Cake is printed white).
/// - <b>Two triggered abilities sharing one effect body</b> (CR 603.1):
///   "When this artifact enters AND when you sacrifice it" is two separate
///   triggered abilities per CR 603.1 (a single ability never has two
///   independent trigger events), each running the SAME resolve effect —
///   create a 1/1 white Rabbit creature token and scry 1.
///   <list type="number">
///     <item>The <b>enter</b> trigger fires on the source's own ETB via
///       <see cref="Triggers.OnEnterBattlefieldSelf"/> (CR 603.6a). The live
///       <see cref="TriggerManager"/> auto-binds a card's triggered abilities
///       when it moves to the battlefield and evaluates them against that same
///       ETB <see cref="CardMovedEvent"/>, so the enter trigger sees its own
///       entry.</item>
///     <item>The <b>sacrifice</b> trigger fires on a
///       <see cref="PermanentSacrificedEvent"/> whose
///       <see cref="PermanentSacrificedEvent.SacrificedCard"/> is THIS card and
///       whose <see cref="PermanentSacrificedEvent.SacrificingPlayer"/> is the
///       controller ("when YOU sacrifice it" — CR 603.1 / CR 701.16). The
///       card's own {2},{T},Sacrifice ability pays a bus-aware
///       <see cref="SacrificeSelfCost"/>, which publishes that event — but the
///       trigger fires for ANY sacrifice of the Cake by its controller, not
///       only its own ability (a Food-sac payoff, an edict the controller is
///       hit by, etc.).</item>
///   </list>
/// - <b>Shared resolve effect</b> (<see cref="BuildEnterOrSacrificeEffect"/>):
///   creates the Rabbit token via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>
///   (1/1 white, Rabbit subtype — CR 111.4 token characteristics), then scries
///   1 (CR 701.20) through the same path as <c>ScrySelfEffectDef</c> — peek the
///   top card, prompt the controller's live agent off the
///   <see cref="ResolutionContext"/> (falling back to the
///   <see cref="AgentRegistry"/>, then all-to-bottom), and commit via
///   <see cref="Fx.Scry"/>.
/// - <b>{2}, {T}, Sacrifice this artifact: You gain 3 life</b> activated
///   ability (CR 602.1) — same Food sac-for-life shape as Gingerbrute /
///   Witch's-Oven-family Food tokens. Costs: <see cref="Costs.Mana"/>(\"{2}\"),
///   <see cref="Costs.TapSelf"/>, and a bus-aware
///   <see cref="SacrificeSelfCost"/>; effect gains 3 life (<see cref="Fx.GainLife"/>).
///
/// ## Why a named factory
/// The JSON card-definition path has no "create a specific token" ability-effect
/// verb (the <c>CreateToken</c> resolve kind is spell-path only) and no
/// "when you sacrifice this" self-scoped trigger, so the enter / sacrifice
/// token-and-scry behaviour is not expressible declaratively — hence the
/// bespoke factory. The activated sac-for-life ability could be JSON, but is
/// built here in C# so the whole card lives in one place.
/// </summary>
[CardName("Carrot Cake")]
public static class CarrotCakeFactory
{
    public const string CardName = "Carrot Cake";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>Activation mana cost of the gain-life ability — {2} generic.</summary>
    public const string LifeAbilityManaCost = "{2}";

    /// <summary>Life gained by the {2},{T},Sacrifice ability.</summary>
    public const int LifeGain = 3;

    /// <summary>Scry amount on the enter / sacrifice trigger (CR 701.20).</summary>
    public const int ScryAmount = 1;

    /// <summary>
    /// Construct Carrot Cake with no live ZoneService / TriggerManager wiring.
    /// The triggers are attached for shape but not registered with a bus, and a
    /// resolved Rabbit token bypasses ZoneService. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Carrot Cake. When <paramref name="zoneService"/> is supplied
    /// the created Rabbit token enters via ZoneService so its ETB
    /// <see cref="CardMovedEvent"/> fires (downstream ETB listeners observe the
    /// token). When <paramref name="triggers"/> is supplied the enter /
    /// sacrifice triggers are registered with the bus so the matching events
    /// auto-queue the abilities; otherwise the live engine's
    /// <see cref="TriggerManager"/> auto-binds them on ETB.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            CardName, PrintedManaCost,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Food });
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 111.4 — Carrot Cake is printed white. An artifact's mana cost has
        // no coloured pips, so stamp the colour explicitly (mirrors the token
        // colour-override path) for colour-matters interactions.
        card.SetTokenColors(new[] { ManaColor.White });

        // ----------------------------------------------------------------
        // Enter trigger (CR 603.6a) — "When this artifact enters … create a
        // 1/1 white Rabbit creature token and scry 1."
        // ----------------------------------------------------------------
        var enterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { BuildEnterOrSacrificeEffect(card, owner, zoneService) },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(enterTrigger);
        triggers?.RegisterTriggeredAbility(enterTrigger);

        // ----------------------------------------------------------------
        // Sacrifice trigger (CR 603.1 / CR 701.16) — "and when you sacrifice
        // it, create a 1/1 white Rabbit creature token and scry 1."
        // Fires on a PermanentSacrificedEvent for THIS card sacrificed by its
        // controller. ActiveZones is Graveyard because by the time the event
        // publishes the Cake has already left the battlefield for the graveyard
        // (CR 701.16a) — the trigger must still be registered to observe it.
        // ----------------------------------------------------------------
        var sacTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: OnYouSacrificeThis(card),
            effects: new IEffect[] { BuildEnterOrSacrificeEffect(card, owner, zoneService) },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
        card.AddAbility(sacTrigger);
        triggers?.RegisterTriggeredAbility(sacTrigger);

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice this artifact: You gain 3 life. (CR 602.1)
        // ----------------------------------------------------------------
        var lifeEffect = new Effect(
            $"{CardName}: you gain {LifeGain} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.GainLife(controller, LifeGain);
            });

        var lifeAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                Primitives.Costs.Mana(LifeAbilityManaCost),
                AdditionalCost.Tap(card),
                new SacrificeSelfCost(card),
            },
            effects: new IEffect[] { lifeEffect });
        card.AddAbility(lifeAbility);

        return card;
    }

    /// <summary>
    /// "When you sacrifice it" — CR 603.1 / CR 701.16. Matches the dedicated
    /// <see cref="PermanentSacrificedEvent"/> where the sacrificed permanent is
    /// THIS card and the sacrificing player is its controller ("you"). The
    /// controller is read live off the source so a control-change before the
    /// sacrifice still scopes "you" correctly (CR 109.5). Self-scoped sibling
    /// of <see cref="Triggers.OnAnyPlayerSacrifices"/> /
    /// <see cref="Triggers.OnOpponentSacrifices"/>.
    /// </summary>
    private static ITriggerCondition OnYouSacrificeThis(Artifact card) =>
        new EventTriggerCondition<PermanentSacrificedEvent>((e, _) =>
            ReferenceEquals(e.SacrificedCard, card)
            && ReferenceEquals(e.SacrificingPlayer, card.Controller ?? card.Owner));

    /// <summary>
    /// The shared resolve effect for both triggers: create a 1/1 white Rabbit
    /// creature token (CR 111.4) and scry 1 (CR 701.20). A fresh
    /// <see cref="IEffect"/> instance per trigger keeps the two abilities
    /// independent.
    /// </summary>
    private static IEffect BuildEnterOrSacrificeEffect(
        Artifact card, Player owner, ZoneService? zoneService) =>
        new Effect(
            $"{CardName}: create a 1/1 white Rabbit creature token and scry {ScryAmount}",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 111.4 — 1/1 white Rabbit creature token.
                TokenFactory.CreateOnBattlefield(
                    new TokenFactory.TokenSpec(
                        Name: "Rabbit",
                        Power: 1,
                        Toughness: 1,
                        Subtypes: new[] { CardSubtype.Rabbit },
                        Colors: new[] { ManaColor.White }),
                    controller,
                    zoneService);

                // CR 701.20 — scry 1.
                await ScryOneAsync(controller, ctx).ConfigureAwait(false);
            });

    /// <summary>
    /// Scry 1 (CR 701.20). Peek the top card, prompt the controller's live
    /// agent off the resolution context (then the registry, then all-to-bottom),
    /// and commit. Mirrors <c>ScrySelfEffectDef</c> / Rabbit Response exactly.
    /// </summary>
    private static async ValueTask ScryOneAsync(Player controller, ResolutionContext ctx)
    {
        var peeked = ScryAction.Peek(controller, ScryAmount);
        if (peeked.Count == 0) return;

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked, ctx.Ct)
                .ConfigureAwait(false);
        }
        else
        {
            decision = new ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>());
        }

        Fx.Scry(controller, ScryAmount, decision);
    }
}
