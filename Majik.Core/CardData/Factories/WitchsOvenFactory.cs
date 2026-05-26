using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Witch's Oven (Throne of Eldraine, {1}).
///
/// Artifact. Oracle text:
///   "{T}, Sacrifice a creature: Create a Food token. If the sacrificed
///    creature's toughness was 4 or greater, create two Food tokens
///    instead."
///
/// ## Implemented (v1)
/// - Artifact shape, mana cost {1}, owner / controller stamped.
/// - One <see cref="ActivatedAbility"/> (CR 602.1) with two costs:
///   <list type="number">
///     <item><see cref="AdditionalCost.Tap"/> on the Oven itself (CR 117 /
///       CR 701.20).</item>
///     <item><see cref="SacrificeACreatureCostWithCapture"/> — the
///       payment picks the first eligible creature the controller controls
///       (deterministic v1 picker, same posture as
///       <see cref="SacrificeAnotherCreatureCost"/>). The cost captures the
///       sacrificed creature so the effect closure can read its base
///       toughness (CR 122.x — "sacrificed creature's toughness").</item>
///   </list>
/// - Resolution: creates one Food token via
///   <see cref="TokenFactory.CreateFood"/>; if the sacrificed creature's
///   base toughness was 4 or greater, creates a second Food token. Per the
///   printed "two ... instead" rider this is implemented as a single
///   token + a conditional second token (CR 111.10 — Food token shape).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice target prompt</b>: the embedded
///   <see cref="SacrificeACreatureCostWithCapture"/> picks the first
///   eligible creature deterministically. An agent-driven "choose a
///   creature to sacrifice" prompt waits on the shared sacrifice-prompt
///   surface (same gap as Greater Gargadon / Skirk Prospector / Phyrexian
///   Tower).
/// - <b>"Toughness was" snapshot</b>: the effect reads
///   <see cref="Creature.BaseToughness"/> on the sacrificed creature post-
///   payment. The creature is in the graveyard at that point, but its
///   printed toughness is unchanged by the zone move, so the value is
///   stable. A more rigorous implementation would snapshot toughness via
///   <see cref="ContinuousEffectsService"/> at last-known-information time
///   (CR 608.2g) to catch the case where a continuous +X toughness pump
///   was active on the battlefield (Giant Growth-style) — deferred.
/// </summary>
[CardName("Witch's Oven")]
public static class WitchsOvenFactory
{
    public const string CardName = "Witch's Oven";
    public const string PrintedManaCost = "{1}";

    /// <summary>The printed toughness threshold for the "two Food tokens
    /// instead" rider (CR 122 — printed numeric threshold).</summary>
    public const int BigCreatureToughnessThreshold = 4;

    /// <summary>
    /// Construct Witch's Oven with no live ZoneService wiring. The created
    /// Food tokens bypass <see cref="ZoneService"/> on ETB (no
    /// CardMovedEvent for downstream subscribers); suitable for shape /
    /// activation tests. Use the
    /// <see cref="Create(Player, ZoneService?)"/> overload to thread a
    /// ZoneService for full bus-driven ETB.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, zoneService: null);

    /// <summary>
    /// Construct Witch's Oven. When <paramref name="zoneService"/> is
    /// supplied each Food token's battlefield ETB publishes
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> so downstream
    /// subscribers see the token enter (CR 603.6a / CR 701.20).
    /// </summary>
    public static Artifact Create(Player owner, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}, Sacrifice a creature: Create a Food token. If the sacrificed
        // creature's toughness was 4 or greater, create two Food tokens
        // instead.
        // CR 602.1 — activated ability. CR 117.1c — costs paid
        // simultaneously; the implementation pays tap, then sacrifices a
        // creature, then resolves the token-creation effect.
        // ----------------------------------------------------------------
        var sacCost = new SacrificeACreatureCostWithCapture();

        var bakeEffect = new Effect(
            $"{CardName}: create 1 or 2 Food tokens (toughness ≥{BigCreatureToughnessThreshold} → 2)",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 111.10 — Food token. Always create one; conditionally
                // create a second when the sacrificed creature's printed
                // toughness was 4 or greater.
                TokenFactory.CreateFood(controller, zoneService);

                var sacrificed = sacCost.Sacrificed;
                if (sacrificed != null
                    && sacrificed.BaseToughness >= BigCreatureToughnessThreshold)
                {
                    TokenFactory.CreateFood(controller, zoneService);
                }
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                sacCost,
            },
            effects: new IEffect[] { bakeEffect });

        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// "Sacrifice a creature" — activated-ability cost (CR 117 / CR 701.20)
    /// that picks the first creature the controller controls, removes it
    /// from the battlefield, and puts it into its owner's graveyard. Sister
    /// shape to <see cref="SacrificeAnotherCreatureCost"/>, but without the
    /// "other than" exclusion (Witch's Oven is an artifact — it can't
    /// sacrifice itself to satisfy "a creature").
    ///
    /// Captures the sacrificed creature on <see cref="Sacrificed"/> so the
    /// resolve-time effect can read its base toughness (CR 122.x —
    /// "sacrificed creature's toughness").
    /// </summary>
    public sealed class SacrificeACreatureCostWithCapture : ICost
    {
        /// <summary>Optionally set by the agent to nominate which creature
        /// to sacrifice. When null the first eligible creature is chosen
        /// deterministically (v1 picker policy).</summary>
        public Creature? Target { get; set; }

        /// <summary>The creature actually sacrificed after a successful
        /// <see cref="Pay"/>. Null before payment.</summary>
        public Creature? Sacrificed { get; private set; }

        /// <inheritdoc/>
        public string Description => "sacrifice a creature";

        /// <inheritdoc/>
        public bool CanPay(Player player)
        {
            if (player == null) return false;
            if (Target != null)
            {
                return ReferenceEquals(Target.Controller, player)
                    && Target.Zone == ZoneType.Battlefield
                    && Target.HasType(CardType.Creature);
            }
            return player.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .Any();
        }

        /// <inheritdoc/>
        public void Pay(Player player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            var pick = Target ?? player.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .FirstOrDefault();

            if (pick == null)
                throw new InvalidPlayerActionException(
                    $"Cannot pay {Description}: no eligible creature to sacrifice.");

            player.Zones.Battlefield.RemoveCard(pick);
            player.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
            Sacrificed = pick;
        }
    }
}
