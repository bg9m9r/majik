using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Waker of Waves (Magic 2021 / Modern Horizons 2).
///
/// Creature — Whale {5}{U}{U} 7/7.
/// Oracle text:
///   "Creatures your opponents control get -1/-0.
///    {1}{U}, Discard this card: Look at the top two cards of your library.
///    Put one of them into your hand and the other into your graveyard."
///
/// ## Implemented (v1)
/// - 7/7 blue Whale with mana cost {5}{U}{U}, MV 7, correct identity /
///   owner / controller.
/// - <b>Static "Creatures your opponents control get -1/-0"</b>: wired via
///   <see cref="LordStaticEffect"/> with <c>matchingSubtype: null</c>
///   (no type restriction), <c>opponentsOnly: true</c>,
///   <c>power: -1, toughness: 0</c>. Layer 7c (CR 613.7c — P/T
///   modifications). <see cref="ContinuousEffect.IsActive"/> gates on the
///   source being on the battlefield, so LTB/flicker naturally lifts the
///   debuff (same cleanup pattern as <see cref="PlagueEngineerFactory"/>).
/// - <b>Activated-from-hand ability</b>: "{1}{U}, Discard this card: Look
///   at the top two cards of your library. Put one of them into your hand
///   and the other into your graveyard." Cost is
///   <see cref="ManaCostCost"/> ({1}{U}) + <see cref="DiscardSelfCost"/>
///   (CR 702.74a — the discard-self cost gates activation to the hand
///   zone; same mechanism as the Kamigawa Channel lands in
///   <see cref="ChannelLandCycleFactory"/>). On resolve, the controller
///   looks at the top 2 cards of their library and picks 1 to hand; the
///   other goes to graveyard. Pick is sourced from the registered
///   <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>; falls back to the
///   first card when no agent is registered (same pattern as Takenuma in
///   <see cref="ChannelLandCycleFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; its <see cref="ContinuousEffect.IsActive"/> check
///   short-circuits when Waker isn't on the battlefield, so the debuff
///   lifts correctly. A future Prune pass could drop the entry (same
///   pattern as <see cref="PlagueEngineerFactory"/>).
/// - <b>Controller-change re-eval</b>: the "your opponents" set is
///   whoever was not Waker's controller at register time. Same caveat as
///   <see cref="PlagueEngineerFactory"/> and
///   <c>OpponentArtifactActivatedSuppressionEffect</c>.
/// - <b>Bot pick quality</b>: the agent can rank the two revealed cards
///   strategically; v1 falls back to first card when no agent registered.
/// </summary>
[CardName("Waker of Waves")]
public static class WakerOfWavesFactory
{
    public const string CardName = "Waker of Waves";
    public const string Cost = "{5}{U}{U}";
    public const int Power = 7;
    public const int Toughness = 7;

    /// <summary>
    /// Construct a Waker of Waves with no live continuous-effects wiring.
    /// The static anti-anthem effect is NOT registered; suitable for
    /// card-shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Waker of Waves. When
    /// <paramref name="continuousEffects"/> is supplied, the static
    /// "Creatures your opponents control get -1/-0" effect is registered
    /// against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// -1/-0 static effect against. May be null — no live debuff.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Whale });

        card.SetOwner(owner);
        card.SetController(owner);

        // Static: "Creatures your opponents control get -1/-0."
        // CR 613.7c — P/T modification via LordStaticEffect with null
        // subtype (no creature-type restriction) and opponentsOnly:true.
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: (CardSubtype?)null,
                power: -1,
                toughness: 0,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: true));
        }

        // Activated from hand: {1}{U}, Discard this card: look at top 2,
        // put one to hand and the other to graveyard.
        // DiscardSelfCost gates activation to Hand (CR 702.74a).
        AttachHandCycleAbility(card, owner);

        return card;
    }

    /// <summary>
    /// Wire the "{1}{U}, Discard this card: Look at the top two cards of
    /// your library. Put one into your hand and the other into your
    /// graveyard." activated ability. Activation is legal only from the
    /// hand (DiscardSelfCost rejects payment from any other zone —
    /// CR 702.74a applies by analogy).
    /// </summary>
    private static void AttachHandCycleAbility(Creature card, Player controller)
    {
        var ability = new ActivatedAbility(
            source: card,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost("{1}{U}"),
                new DiscardSelfCost(card),
            },
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: look at top 2, put one into hand and other into graveyard",
                    ctx => ResolveTopTwoPickAsync(controller, ctx)),
            },
            targetRequests: Array.Empty<TargetRequest>());

        card.AddAbility(ability);
    }

    /// <summary>
    /// Resolve the "look at top 2" body: move top 2 cards off the library,
    /// consult the registered agent (or fall back to first card) to choose
    /// 1 for hand; the other goes to graveyard.
    /// </summary>
    private static async ValueTask ResolveTopTwoPickAsync(Player controller, ResolutionContext ctx)
    {
        var top2 = controller.Zones.Library.GetCards().Take(2).ToList();
        if (top2.Count == 0) return;

        // Agent chooses which card goes to hand; fallback = first card.
        ICard? pick;
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        pick = agent != null
            ? await agent.ChooseLibraryPickAsync(ctx.Game, top2, "any card")
                .ConfigureAwait(false)
            : top2[0];

        // Ensure pick is actually one of the two cards (guard against
        // a misbehaving agent returning something else).
        if (pick == null || !top2.Contains(pick)) pick = top2[0];

        foreach (var c in top2)
        {
            controller.Zones.Library.RemoveCard(c);
            if (ReferenceEquals(c, pick))
            {
                controller.Zones.Hand.AddCard(c);
                c.SetZone(ZoneType.Hand);
            }
            else
            {
                controller.Zones.Graveyard.AddCard(c);
                c.SetZone(ZoneType.Graveyard);
            }
        }
    }
}
