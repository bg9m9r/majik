using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Asmoranomardicadaistinaculdacar (Modern Horizons
/// 2). The longest-name card in Magic.
///
/// Verified seed identity (<c>EmbeddedCardRepository.GetByName</c>):
///   <b>Legendary Creature — Human Wizard, 3/3, mana cost {0} (empty).</b>
/// Oracle text:
///   "As long as you've discarded a card this turn, you may pay {B/R} to
///    cast this spell.
///    When Asmoranomardicadaistinaculdacar enters, you may search your
///    library for a card named The Underworld Cookbook, reveal it, put it
///    into your hand, then shuffle.
///    Sacrifice two Foods: Target creature deals 6 damage to itself."
///
/// NOTE: this factory previously shipped a FICTIONAL card (a 4/4 Human
/// Shaman {B}{R}{G} with a Food-tutor {T} ability and two never-printed
/// static abilities). That implementation has been replaced wholesale with
/// the real MH2 card verified against the embedded seed.
///
/// ## Implemented (v1)
///
/// - <b>Identity</b>: 0-cost (empty mana cost) Legendary Creature — Human
///   Wizard, 3/3 (CR 205.4a — Legendary supertype; CR 704.5j legend rule
///   SBA fires automatically once two copies share the battlefield).
///
/// - <b>ETB tutor (CR 603.6a)</b>: "When Asmoranomardicadaistinaculdacar
///   enters, you may search your library for a card named The Underworld
///   Cookbook, reveal it, put it into your hand, then shuffle." Searches
///   the controller's library for a card whose name is exactly
///   <see cref="TutorTargetName"/>, consults the registered
///   <see cref="IPlayerAgent"/> (CR 701.19a — "you may" + the search may
///   fail to find, both legal), moves the pick Library → Hand via the
///   <see cref="ZoneServiceRegistry"/> (event-firing path; direct fallback
///   when no service is registered), then shuffles ONCE (CR 701.20a). Same
///   posture as <see cref="BorderlandRangerFactory"/>'s ETB basic-land
///   tutor. The printed "reveal it" UI signal is a no-op in v1 (same gap
///   as every tutor factory) — the card still reaches the hand so the
///   observable game state is correct.
///
/// - <b>"Sacrifice two Foods: Target creature deals 6 damage to itself."
///   (CR 602.1 activated ability)</b>: cost is sacrificing two artifacts
///   that are Foods (CR 205.3 — Food is an artifact subtype; CR 701.16a —
///   sacrifice). A 1..1 "target creature" <see cref="TargetRequest"/>
///   (Intent: <see cref="BotIntent.Removal"/>). On resolution the TARGET
///   deals 6 damage to itself (CR 119.3 — the source of the damage is the
///   target creature, not Asmoran — relevant for lifelink / "damage dealt
///   by" triggers on the target). Routed through
///   <see cref="Fx.DealDamage"/>. The cost is a single payment-time
///   <see cref="SacrificeTwoFoodsCost"/> — it reads the LIVE battlefield at
///   activation (NOT a construction-time snapshot), so Foods that enter after
///   Asmoran was created are sacrificeable, and the activation is illegal
///   whenever the controller controls fewer than two Foods
///   (CR 602.5e / 601.2g — unpayable cost ⇒ activation illegal). The cost
///   sacrifices two DISTINCT Foods (CR 701.16a) and, on the bus-aware payment
///   path, publishes a <see cref="Events.PermanentSacrificedEvent"/> per
///   sacrifice (it implements <see cref="IBusAwareCost"/>) so aristocrat
///   payoffs fire.
///
/// - <b>Alternative cast cost (CR 118.9)</b>: "As long as you've discarded
///   a card this turn, you may pay {B/R} to cast this spell." Modelled by
///   <see cref="DiscardedThisTurnAlternativeCost"/> (built via
///   <see cref="BuildAlternativeCost"/>), gated on
///   <see cref="TurnState.DiscardsByPlayer"/> &gt; 0 — the same per-turn
///   discard counter Hollow One reads. Supplied to
///   <c>SpellCastFlow.CastAsync</c>'s <c>alternativeCost</c> parameter by
///   the caller (same caller-supplied posture as Voltage Surge / the rest
///   of the alt-cost family); the cast flow's generic alt-cost machinery
///   enforces <see cref="DiscardedThisTurnAlternativeCost.CanCastFor"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Bot alt-cost probe</b>: no
///   <see cref="AlternativeCostProbeRegistry"/> entry for the discard-gated
///   {B/R} cost yet — same posture as Voltage Surge's optional-sacrifice
///   probe. The bot EV layer can still opt in by building the cost via
///   <see cref="BuildAlternativeCost"/> and layering it onto the cast.
/// - <b>Reveal step</b>: the ETB tutor moves the Cookbook Library → Hand
///   without publishing a reveal event — same gap as every tutor factory.
/// </summary>
[CardName("Asmoranomardicadaistinaculdacar")]
public static class AsmoranomardicadaistinaculdacarFactory
{
    public const string CardName = "Asmoranomardicadaistinaculdacar";

    /// <summary>Empty / {0} mana cost — Asmoran is a free spell normally
    /// uncastable without the discard-gated {B/R} alternative.</summary>
    public const string PrintedManaCost = "";

    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>The exact card name the ETB tutor searches for.</summary>
    public const string TutorTargetName = "The Underworld Cookbook";

    /// <summary>The mana cost paid via the discard-gated alternative
    /// (CR 118.9). A single hybrid {B/R} pip.</summary>
    public const string AlternativeManaCost = "{B/R}";

    /// <summary>Damage the target creature deals to itself.</summary>
    public const int SelfDamage = 6;

    /// <summary>
    /// Construct Asmoranomardicadaistinaculdacar owned and controlled by
    /// <paramref name="owner"/> with NO trigger-manager wiring. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Asmoranomardicadaistinaculdacar with optional
    /// <see cref="TriggerManager"/> wiring. When <paramref name="triggers"/>
    /// is supplied, the ETB tutor trigger is registered so the relevant
    /// <c>CardMovedEvent</c> places it on the stack automatically
    /// (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger (CR 603.6a):
        //   "When Asmoranomardicadaistinaculdacar enters, you may search
        //    your library for a card named The Underworld Cookbook, reveal
        //    it, put it into your hand, then shuffle."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor {TutorTargetName} -> hand, then shuffle",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return TutorCookbookToHandAsync(controller, ctx);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability (CR 602.1):
        //   "Sacrifice two Foods: Target creature deals 6 damage to itself."
        // Cost = sacrifice two Food artifacts (CR 205.3 / 701.16a).
        // 1..1 "target creature" TargetRequest. On resolution the TARGET
        // deals 6 damage to itself (CR 119.3 — source is the target).
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: target creature deals {SelfDamage} damage to itself",
            () =>
            {
                if (sacAbility != null
                    && sacAbility.ChosenTargets.Count > 0
                    && sacAbility.ChosenTargets[0].Count > 0
                    && sacAbility.ChosenTargets[0][0] is Creature target)
                {
                    // CR 119.3 — the target creature is the SOURCE of the
                    // damage and the recipient: it deals 6 to itself.
                    Fx.DealDamage(target, SelfDamage);
                }
            });

        sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new SacrificeTwoFoodsCost() },
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// Build the discard-gated {B/R} alternative cast cost (CR 118.9).
    /// Caller supplies the live <see cref="TurnState"/> so
    /// <see cref="DiscardedThisTurnAlternativeCost.CanCastFor"/> can read
    /// the caster's discard count; pass the returned instance into
    /// <c>SpellCastFlow.CastAsync</c>'s <c>alternativeCost</c> parameter.
    /// </summary>
    public static DiscardedThisTurnAlternativeCost BuildAlternativeCost(TurnState turnState)
    {
        ArgumentNullException.ThrowIfNull(turnState);
        return new DiscardedThisTurnAlternativeCost(
            AlternativeManaCost,
            discardCountOf: turnState.DiscardsByPlayer);
    }

    /// <summary>
    /// The two Foods the controller would sacrifice to activate the
    /// "Sacrifice two Foods" ability, or an empty list when the controller
    /// controls fewer than two Foods (CR 602.5e — the activation is then
    /// illegal). A Food is an artifact with the Food subtype (CR 205.3).
    /// Deterministic first-two selection (same posture as the rest of the
    /// sacrifice-cost factory family — real agent-driven sacrifice
    /// prompting awaits the ITarget pipeline).
    /// </summary>
    public static IReadOnlyList<Permanent> FindTwoFoods(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(c => c.HasType(CardType.Artifact) && c.HasSubtype(CardSubtype.Food))
            .Take(2)
            .ToList();
    }

    /// <summary>True when <paramref name="controller"/> controls at least
    /// two Foods (the "Sacrifice two Foods" cost is payable). Delegates to the
    /// live cost primitive so this answer always matches what the engine will
    /// charge at activation time (CR 602.5e — fewer than two Foods ⇒ the
    /// activation is illegal).</summary>
    public static bool CanSacrificeTwoFoods(Player controller) =>
        new SacrificeTwoFoodsCost().CanPay(controller);

    /// <summary>
    /// Search <paramref name="player"/>'s library for a card named
    /// <see cref="TutorTargetName"/>, consult the agent (which may decline;
    /// deterministic first-match fallback when no agent), move the pick
    /// Library → Hand, then shuffle once (CR 701.20a). Same posture as
    /// <see cref="BorderlandRangerFactory"/>'s ETB basic-land tutor.
    /// </summary>
    private static async ValueTask TutorCookbookToHandAsync(Player player, ResolutionContext ctx)
    {
        bool IsTarget(ICard c) =>
            string.Equals(c.Name, TutorTargetName, StringComparison.Ordinal);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsTarget).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        $"{TutorTargetName} to put into your hand")
                    .ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Hand, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "asmoran-tutor");
    }
}
