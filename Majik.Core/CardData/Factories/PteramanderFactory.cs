using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pteramander (Ravnica Allegiance, <c>{U}</c>).
/// Creature — Salamander Drake. 1/1.
///
/// Oracle text (Scryfall-verified):
/// <list type="number">
///   <item>"Flying"</item>
///   <item>"<c>{7}{U}</c>: Adapt 4. This ability costs <c>{1}</c> less to
///       activate for each instant and sorcery card in your graveyard.
///       (If this creature has no +1/+1 counters on it, put four +1/+1
///       counters on it.)"</item>
/// </list>
///
/// ## Implementation
/// <list type="bullet">
///   <item><b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker;
///       combat code reads it the same way Murktide Regent's Flying is
///       wired.</item>
///
///   <item><b>Activated Adapt 4 (CR 702.116)</b>: delegates to
///       <see cref="AdaptFactory.Build"/> with N=4. The helper handles the
///       CR 702.116b "no +1/+1 counters" resolution-time gate and routes
///       the placement through <see cref="CountersService.Add"/> so the
///       post-commit <see cref="CounterAddedEvent"/> publishes. The cost
///       passed to <see cref="AdaptFactory.Build"/> is a printed
///       <c>{7}{U}</c>, but the activated ability's single
///       <see cref="ManaCostCost"/> is then swapped for the
///       graveyard-reducing <see cref="GraveyardReducedManaCost"/> below so
///       the cost is recomputed at activation time.</item>
///
///   <item><b>Activated-ability cost reduction (CR 118.5 / CR 117.7c
///       analogue)</b>: "This ability costs <c>{1}</c> less to activate for
///       each instant and sorcery card in your graveyard." Modeled as a
///       <see cref="ManaCostCost"/> subclass —
///       <see cref="GraveyardReducedManaCost"/> — that recomputes the
///       effective cost every time it is consulted
///       (<see cref="ManaCostCost.CanPay"/> / <see cref="ManaCostCost.Pay"/>).
///       Per CR 118.5, the reduction lowers only the generic portion and is
///       floored at zero (<see cref="ValueObjects.ManaCost.WithGeneric"/>
///       clamps negatives), so the printed <c>{U}</c> pip is never touched:
///       <list type="bullet">
///         <item>0 instants/sorceries in graveyard → pays <c>{7}{U}</c></item>
///         <item>3 in graveyard → pays <c>{4}{U}</c></item>
///         <item>7 in graveyard → pays <c>{U}</c></item>
///         <item>10 in graveyard → still <c>{U}</c> (generic floored at 0;
///             the blue pip is untouched per CR 117.7c)</item>
///       </list>
///       The graveyard is re-counted at pay time against the source's
///       <em>current</em> controller (CR 118.5 — cost is locked in when the
///       ability is activated; this subclass reads live controller state at
///       the moment <see cref="AbilityActivator"/> calls Pay).</item>
/// </list>
///
/// <para>
/// <b>Wiring overloads</b>: both <see cref="Create(Player)"/> and the
/// reduction logic are self-contained — the cost subclass needs only the
/// live card to find its controller's graveyard, so there is no separate
/// "shape vs. wired" split for the reduction. The
/// <paramref name="replacements"/> / <paramref name="eventBus"/> pair only
/// affects the Adapt counter-placement surface (Hardened Scales /
/// "whenever +1/+1 counters are put on" triggers), matching
/// <see cref="EmperorOfBonesFactory"/>'s posture.
/// </para>
/// </summary>
[CardName("Pteramander")]
public static class PteramanderFactory
{
    public const string CardName = "Pteramander";
    public const string PrintedManaCost = "{U}";

    /// <summary>CR 702.116 — printed Adapt cost before reduction.</summary>
    public const string AdaptCost = "{7}{U}";
    public const int AdaptAmount = 4;
    public const int BasePower = 1;
    public const int BaseToughness = 1;

    /// <summary>
    /// Construct Pteramander for the dispatcher / shape-test path: no
    /// <see cref="ReplacementBus"/> or <see cref="IEventBus"/> wired. The
    /// graveyard cost reduction is fully live regardless (it reads the
    /// card's controller at pay time); the Adapt counter-placement just
    /// won't publish <see cref="CounterAddedEvent"/> without an event bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Pteramander with optional engine plumbing.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/>
    /// routed through <see cref="CountersService.Add"/> for the Adapt
    /// counter placement (Hardened Scales / Doubling Season — CR 614).</param>
    /// <param name="eventBus">Optional <see cref="IEventBus"/> the
    /// post-commit <see cref="CounterAddedEvent"/> publishes on (surface for
    /// "whenever +1/+1 counters are put on" triggers). Null ⇒ counters still
    /// commit, no event surfaces.</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: BasePower,
            toughness: BaseToughness,
            subtypes: new[] { CardSubtype.Salamander, CardSubtype.Drake });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Marker only; combat code reads KeywordAbility
        // (same wiring shape as Murktide Regent).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // "{7}{U}: Adapt 4." — CR 702.116. Build the Adapt activated ability
        // via AdaptFactory (which also stamps the "Adapt 4" keyword marker
        // and handles the resolution-time "no +1/+1 counters" gate +
        // CountersService routing). AdaptFactory.Build returns an
        // ActivatedAbility whose single cost is a fixed ManaCostCost; we
        // re-wrap it with the graveyard-reducing cost below.
        // ----------------------------------------------------------------
        var baseAdapt = AdaptFactory.Build(
            card, AdaptCost, AdaptAmount, replacements, eventBus);

        // ----------------------------------------------------------------
        // "This ability costs {1} less to activate for each instant and
        // sorcery card in your graveyard." — CR 118.5. Swap the fixed
        // ManaCostCost for a GraveyardReducedManaCost that recomputes the
        // effective cost at pay time against the source's current
        // controller's graveyard. CR 117.7c — only generic is reduced; the
        // {U} pip is preserved and the generic floors at zero.
        // ----------------------------------------------------------------
        var reducedCost = new GraveyardReducedManaCost(AdaptCost, card);
        var nonManaCosts = baseAdapt.Costs.Where(c => c is not ManaCostCost);
        var adaptAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { reducedCost }.Concat(nonManaCosts),
            effects: baseAdapt.Effects);

        card.AddAbility(adaptAbility);

        return card;
    }

    /// <summary>
    /// CR 118.5 — a <see cref="ManaCostCost"/> whose effective cost is the
    /// printed cost with its generic portion reduced by {1} for each instant
    /// and sorcery card in the bound source's current controller's
    /// graveyard. The reduction is recomputed on every
    /// <see cref="CanPay"/> / <see cref="Pay"/> call so it reflects live
    /// graveyard state at activation time. Generic is floored at zero
    /// (<see cref="ValueObjects.ManaCost.WithGeneric"/>) and coloured pips
    /// are never touched (CR 117.7c).
    /// </summary>
    public sealed class GraveyardReducedManaCost : ManaCostCost
    {
        private readonly ValueObjects.ManaCost _printed;
        private readonly Card _source;

        public GraveyardReducedManaCost(string printedManaCost, Card source)
            : base(printedManaCost)
        {
            _printed = ValueObjects.ManaCost.Parse(printedManaCost);
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// The printed (pre-reduction) cost, for inspection / tooltips.
        /// </summary>
        public ValueObjects.ManaCost Printed => _printed;

        /// <summary>
        /// CR 118.5 — count instant and sorcery cards in the source's
        /// current controller's graveyard. Reads the controller (falling
        /// back to the owner) live, so a change of control before activation
        /// is honoured.
        /// </summary>
        public int Reduction()
        {
            var controller = _source.Controller ?? _source.Owner;
            var graveyard = controller?.Zones?.Graveyard;
            if (graveyard == null) return 0;

            var n = 0;
            foreach (var c in graveyard.GetCards())
            {
                if (c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// The effective cost after applying the graveyard reduction. Only
        /// the generic component is lowered (CR 117.7c); negatives floor to
        /// zero via <see cref="ValueObjects.ManaCost.WithGeneric"/>.
        /// </summary>
        public ValueObjects.ManaCost Effective()
        {
            var newGeneric = _printed.Generic - Reduction();
            return _printed.WithGeneric(newGeneric);
        }

        public override bool CanPay(Player player)
        {
            if (player == null) return false;
            return player.ManaPool.CanPay(Effective());
        }

        public override void Pay(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);
            var effective = Effective();
            if (!player.ManaPool.CanPay(effective))
            {
                throw new Domain.Exceptions.InvalidPlayerActionException(
                    $"Cannot pay mana cost: {effective}");
            }
            if (!player.PayMana(effective))
            {
                throw new Domain.Exceptions.InvalidPlayerActionException(
                    $"Failed to pay mana cost: {effective}");
            }
        }
    }
}
