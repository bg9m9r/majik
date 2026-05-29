using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gilded Goose (Throne of Eldraine, {G}).
///
/// Creature — Bird 0/2. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, create a Food token. (It's an artifact
///    with "{2}, {T}, Sacrifice this token: You gain 3 life.")
///    {1}{G}, {T}: Create a Food token.
///    {T}, Sacrifice a Food: Add one mana of any color."
///
/// The base shape (name, Creature, Bird subtype, {G}, 0/2) is materialised
/// from the embedded JSON definition (<c>gilded-goose.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Flying keyword, the ETB
/// Food-token trigger, the activated "Create a Food token" ability, and the
/// five sacrifice-a-Food mana abilities are layered on here — the JSON
/// schema doesn't express keyword markers, token-creation effects, or mana
/// abilities, so those live in the factory (same posture as
/// <see cref="TwinSilkSpiderFactory"/>'s Reach + ETB token trigger).
///
/// ## Implemented (v1)
/// - 0/2 <see cref="Creature"/> — Bird at {G}.
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker so combat surfaces (<c>CombatAbilities.HasFlying</c>) observe
///   it — same shape as <see cref="StormscaleScionFactory"/>'s Flying.
/// - <b>ETB triggered ability (CR 603.6a)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). Resolution creates one Food token (CR 111.10) under this
///   card's controller via <see cref="TokenFactory.CreateFood"/>. The Food
///   token's own "{2}, {T}, Sacrifice this token: You gain 3 life" ability is
///   wired by <see cref="TokenFactory.CreateFood"/>.
/// - <b>"{1}{G}, {T}: Create a Food token"</b> — an
///   <see cref="ActivatedAbility"/> (CR 602.1) with a {1}{G}
///   <see cref="ManaCostCost"/> + <see cref="AdditionalCost.Tap"/> on the
///   Goose; resolution mints one Food token. This is NOT a mana ability
///   (it produces a token, not mana — CR 605.1a) so it uses the stack
///   (CR 602.1).
/// - <b>"{T}, Sacrifice a Food: Add one mana of any color"</b> — modeled as
///   five <see cref="ManaAbility"/> instances (one per WUBRG), each pairing
///   the Goose's self-tap with a shared
///   <see cref="SacrificeAFoodCost"/> additional cost. Same "one ability per
///   colour" shape as <see cref="PhyrexianAltarFactory"/> /
///   <see cref="SpringleafDrumFactory"/> — the activator/bot picks the
///   colour by picking the matching ability slot. CR 605.1 — mana abilities
///   don't use the stack; the {T} and the Food-sacrifice are paid
///   concurrently.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability (not registered with any
///   <see cref="TriggerManager"/>); the activated "Create a Food token"
///   ability and the five mana abilities are attached and ready. Food
///   tokens created via these abilities bypass <see cref="ZoneService"/> on
///   ETB (no CardMovedEvent). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. The ETB trigger registers with <paramref name="triggers"/>; all
///   Food tokens route through <paramref name="zones"/> so their own
///   <see cref="CardMovedEvent"/> publishes (downstream ETB subscribers see
///   the token's arrival).
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: the any-colour production is
///   five separate <see cref="ManaAbility"/> instances (one per WUBRG) — the
///   bot's source-picker selects the colour at payment time. A single
///   ManaAbility with "choose colour at activation" is not yet in the engine
///   (same pattern as Phyrexian Altar / City of Brass).
/// - <b>Food-sacrifice target prompt</b>: <see cref="SacrificeAFoodCost"/>
///   picks the first eligible Food token deterministically (agents can
///   pre-set <see cref="SacrificeAFoodCost.Target"/>). Full agent-driven
///   prompting waits on the shared sacrifice-prompt surface (same gap as
///   Witch's Oven / Phyrexian Tower).
/// </summary>
[CardName("Gilded Goose")]
public static class GildedGooseFactory
{
    public const string CardName = "Gilded Goose";
    public const string Slug = "gilded-goose";

    /// <summary>CR 602.1 — printed mana cost of the "Create a Food token"
    /// activated ability.</summary>
    public const string CreateFoodCost = "{1}{G}";

    private const string FlyingKeyword = "Flying";

    /// <summary>
    /// Construct Gilded Goose with no live wiring. The Flying marker, the
    /// ETB Food-token trigger (attached for shape, not registered), the
    /// activated "Create a Food token" ability, and the five sacrifice-a-Food
    /// mana abilities are attached. Food tokens created here bypass
    /// <see cref="ZoneService"/> on ETB. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Gilded Goose with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, every Food token's ETB routes
    /// through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding <see cref="CardMovedEvent"/> lands the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Bird
        // subtype, {G}, 0/2). The JSON carries no abilities — Flying + the
        // ETB trigger + the activated/mana abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker only; consumed by
        // CombatAbilities.HasFlying so block-legality observes it. Same
        // shape as Stormscale Scion's Flying.
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111.10 (Food token).
        //   "When this creature enters, create a Food token."
        // No targets — pure token-creation. Same wiring as Twin-Silk Spider.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create a Food token",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateFood(controller, zones);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // "{1}{G}, {T}: Create a Food token." CR 602.1 — activated ability
        // that USES the stack (it produces a token, not mana — it is not a
        // mana ability per CR 605.1a). Costs: {1}{G} mana + {T} on the Goose.
        // ----------------------------------------------------------------
        var makeFoodEffect = new Effect(
            $"{CardName}: create a Food token",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateFood(controller, zones);
            });

        var makeFoodAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse(CreateFoodCost)),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { makeFoodEffect });

        card.AddAbility(makeFoodAbility);

        // ----------------------------------------------------------------
        // "{T}, Sacrifice a Food: Add one mana of any color." CR 605.1 —
        // five tapless-no, self-tapping ManaAbility instances (one per
        // WUBRG), each pairing the Goose's {T} with a shared
        // SacrificeAFoodCost additional cost. Only one of the five can be
        // activated per payment (one Food sacrificed → one mana of the
        // chosen colour). Same "one ability per colour" shape as Phyrexian
        // Altar / Springleaf Drum.
        // ----------------------------------------------------------------
        var sacFoodCost = new SacrificeAFoodCost();
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            card.AddAbility(new GildedGooseManaAbility(
                source: card,
                controller: owner,
                color: color,
                sacrificeCost: sacFoodCost));
        }

        return card;
    }

    /// <summary>
    /// "Sacrifice a Food" — activated-ability cost (CR 117 / CR 701.16).
    /// Picks a Food-subtype artifact (CR 111.10) the controller controls,
    /// removes it from the battlefield, and puts it into its owner's
    /// graveyard. Sister shape to
    /// <see cref="SacrificeAnotherCreatureOrBloodTokenCost"/> (a subtype-aware
    /// sacrifice picker), narrowed to the Food subtype. A Food token is a
    /// colourless artifact with subtype Food, so the picker matches both
    /// Food tokens and any nontoken Food permanent (e.g. Gingerbrute).
    /// </summary>
    public sealed class SacrificeAFoodCost : ICost
    {
        /// <summary>
        /// Optionally set by the agent to nominate which Food to sacrifice.
        /// When null the first eligible Food on the controller's battlefield
        /// is chosen deterministically (v1 picker policy). Must be a
        /// Food-subtype permanent the paying player controls.
        /// </summary>
        public Permanent? Target { get; set; }

        /// <inheritdoc/>
        public string Description => "sacrifice a Food";

        /// <inheritdoc/>
        public bool CanPay(Player player)
        {
            if (player == null) return false;
            if (Target != null) return IsEligible(Target, player);
            return player.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .Any(p => IsEligible(p, player));
        }

        /// <inheritdoc/>
        public void Pay(Player player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            var pick = Target ?? player.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .FirstOrDefault(p => IsEligible(p, player));

            if (pick == null)
                throw new InvalidOperationException(
                    $"Cannot pay {Description}: no eligible Food to sacrifice.");

            player.Zones.Battlefield.RemoveCard(pick);
            player.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
        }

        private static bool IsEligible(Permanent p, Player player) =>
            p != null
            && ReferenceEquals(p.Controller, player)
            && p.Zone == ZoneType.Battlefield
            && p.HasSubtype(CardSubtype.Food);
    }
}

/// <summary>
/// One of Gilded Goose's five sacrifice-a-Food mana abilities — pays the
/// Goose's self-tap plus a shared <see cref="GildedGooseFactory.SacrificeAFoodCost"/>
/// as an additional cost, producing one mana of a single colour. Subclasses
/// <see cref="ManaAbility"/> so the sacrifice cost is reachable from outside
/// for test / bot target-setting — same shape as Phyrexian Altar's mana
/// ability.
/// </summary>
public sealed class GildedGooseManaAbility : ManaAbility
{
    /// <summary>
    /// The shared Food-sacrifice cost paid as part of activating this
    /// ability. Set <see cref="GildedGooseFactory.SacrificeAFoodCost.Target"/>
    /// before activation to pick a specific Food; otherwise the cost falls
    /// back to its deterministic first-eligible pick.
    /// </summary>
    public GildedGooseFactory.SacrificeAFoodCost SacrificeChoice { get; }

    internal GildedGooseManaAbility(
        Creature source,
        Player controller,
        string color,
        GildedGooseFactory.SacrificeAFoodCost sacrificeCost)
        : base(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse(color),
            // CR 605.1 — {T} is the self-tap (tapsAsCost defaults true via
            // this overload); gate on untapped + a Food available to sac.
            canActivateCheck: () => !source.IsTapped && sacrificeCost.CanPay(controller),
            additionalCostPayer: p => sacrificeCost.Pay(p))
    {
        SacrificeChoice = sacrificeCost;
    }
}
