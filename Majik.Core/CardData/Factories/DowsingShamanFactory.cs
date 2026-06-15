using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dowsing Shaman (Champions of Kamigawa, {4}{G}).
///
/// Creature — Centaur Shaman 3/4. Oracle text (verified against Scryfall):
///   "{2}{G}, {T}: Return target enchantment card from your graveyard to your
///    hand."
///
/// ## Implemented (v1)
/// - <b>Creature — Centaur Shaman {4}{G} 3/4</b>, owner/controller wired.
/// - <b>"{2}{G}, {T}: Return target enchantment card from your graveyard to your
///   hand."</b> — a graveyard-recursion <see cref="ActivatedAbility"/> whose
///   costs are a <see cref="ManaCostCost"/> ({2}{G}) + <see cref="AdditionalCost.Tap"/>(self)
///   (CR 118 / 602.2). The recursion target is an enchantment CARD in the
///   CONTROLLER's graveyard (CR 109.5 / 110.4 — "your graveyard" = the ability's
///   controller's graveyard), chosen via a <see cref="TargetRequest"/> +
///   CandidateGatherer and read at resolution from
///   <see cref="ResolutionContext.ChosenTargets"/> (CR 601.2c), re-validated per
///   CR 608.2b. Resolution returns the chosen card to that controller's hand via
///   <see cref="Majik.Core.Primitives.Fx.ReturnFromGraveyardToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>
///   (CR 701.20).
///
///   <para>
///   <b>RE-SOURCE-SAFE (Agatha's Soul Cauldron re-home).</b> The recursion
///   effect reads its source / controller off the live
///   <see cref="ResolutionContext.Source"/> (<c>(ctx.Source as Permanent)?.Controller
///   ?? card.Controller ?? owner</c>) rather than capturing the authoring
///   <c>card</c>, and the ability is marked
///   <see cref="ActivatedAbility.RebindSafe"/> = true. Its {T} cost is a
///   source-capturing <see cref="AdditionalCost.Tap"/> that
///   <see cref="ActivatedAbility.RebindTo"/> Stage-1 re-homes via
///   <see cref="AdditionalCost.RebindSource"/>. Agatha's Soul Cauldron therefore
///   re-homes the REAL ability to a counter-bearing creature (CR 707.2 / 613.1f
///   / 702.49): the bearer taps ITSELF and recurs an enchantment from the
///   bearer's controller's graveyard. This is the EXACT shape
///   <see cref="OracleActivatedAbilityBinder"/> reconstructs for the grant
///   ("{cost}: Return target &lt;type&gt; card from your graveyard to your
///   hand."), so an imprinted graveyard-recursion body re-homes onto a grown
///   bearer for free.
///   </para>
/// </summary>
[CardName("Dowsing Shaman")]
public static class DowsingShamanFactory
{
    public const string CardName = "Dowsing Shaman";
    public const string PrintedManaCost = "{4}{G}";
    public const string AbilityManaCost = "{2}{G}";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Dowsing Shaman owned and controlled by <paramref name="owner"/>.
    /// The single graveyard-recursion activated ability is attached. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Centaur, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        AddRecursionAbility(card, owner);

        return card;
    }

    // -----------------------------------------------------------------------
    // "{2}{G}, {T}: Return target enchantment card from your graveyard to your
    //  hand."
    //
    // RE-SOURCE-SAFE: the effect reads (ctx.Source as Permanent)?.Controller for
    // the live controller, never the captured `card`. The {T} cost is an
    // AdditionalCost that RebindTo re-homes (Stage 1), so Agatha's Soul Cauldron
    // re-homes the REAL ability to a bearer. Marked rebindSafe: true.
    // -----------------------------------------------------------------------
    private static void AddRecursionAbility(Creature card, Player owner)
    {
        // CR 601.2c — the chosen target is read at resolution from
        // ResolutionContext.ChosenTargets[0][0]; the candidate pool is the
        // controller's graveyard ENCHANTMENT cards (CR 109.5 / 110.4).
        var targetRequest = new TargetRequest(
            Description: "target enchantment card from your graveyard",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Draw,
            CandidateGatherer: _ => GraveyardEnchantmentCards(card.Controller ?? owner));

        var recurEffect = new Effect(
            $"{CardName}: return target enchantment card from your graveyard to your hand",
            ctx =>
            {
                // RE-SOURCE-SAFE — the live source's controller (the bearer's
                // controller after a RebindTo; else this Shaman) drives "your
                // graveyard".
                var controller = ResolveController(ctx, card, owner);

                // CR 608.2b — read the chosen target; re-validate at resolution:
                // still an enchantment card in the controller's graveyard.
                var pick = ctx.ChosenTargets.Count > 0 && ctx.ChosenTargets[0].Count > 0
                    ? ctx.ChosenTargets[0][0] as ICard
                    : null;
                if (pick == null) return ValueTask.CompletedTask;
                if (pick.Zone != ZoneType.Graveyard) return ValueTask.CompletedTask;
                if (!controller.Zones.Graveyard.ContainsCard(pick)) return ValueTask.CompletedTask;
                if (!pick.HasType(CardType.Enchantment)) return ValueTask.CompletedTask;

                // CR 701.20 — return the chosen card to its owner's hand (the
                // controller, re-checked above). Routed through the registered
                // ZoneService when available so the move publishes.
                var zones = ZoneServiceRegistry.Get(controller);
                Fx.ReturnFromGraveyardToHand(pick, zones);
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(AbilityManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { recurEffect },
            targetRequests: new[] { targetRequest },
            // Agatha's Soul Cauldron re-home soundness — the effect reads the live
            // ResolutionContext.Source's controller; the {T} cost re-homes via
            // AdditionalCost.RebindSource (Stage 1).
            rebindSafe: true));
    }

    /// <summary>
    /// The live source's controller (the bearer's controller after a RebindTo,
    /// else this Shaman's controller, else the authoring owner). Drives "your
    /// graveyard" so a re-homed ability recurs from the BEARER's controller's
    /// graveyard, never the exiled imprinted card's.
    /// </summary>
    private static Player ResolveController(ResolutionContext ctx, Creature card, Player owner) =>
        (ctx.Source as Permanent)?.Controller ?? card.Controller ?? owner;

    /// <summary>
    /// Candidate pool for the recursion target — enchantment CARDS in the
    /// controller's graveyard (CR 110.4 — a card in a graveyard, not a permanent).
    /// </summary>
    private static IReadOnlyList<object> GraveyardEnchantmentCards(Player controller) =>
        controller.Zones.Graveyard.GetCards()
            .Where(c => c.HasType(CardType.Enchantment))
            .Cast<object>()
            .ToList();
}
