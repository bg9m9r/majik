using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aftermath Analyst (Bloomburrow, {2}{G}).
///
/// Creature — Elf Detective 1/1. Oracle text (verified against the printed
/// Bloomburrow card, 2026-06-24):
///   "When this creature enters, mill three cards. (Put the top three cards
///    of your library into your graveyard.)
///    {3}{G}, Sacrifice this creature: Return all land cards from your
///    graveyard to the battlefield tapped."
///
/// ## Shape source
/// Card identity (name, {2}{G}, 1/1, Creature — Elf Detective) is loaded from
/// <c>Majik.Core/CardData/Cards/aftermath-analyst.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. Both abilities are attached in code
/// below — the JSON ability schema does not yet express a mill-on-ETB trigger
/// nor a "return all land cards from your graveyard to the battlefield tapped"
/// effect, so they are hand-rolled here (same posture as the suggested
/// analogues <see cref="StitchersSupplierFactory"/> for the ETB mill and
/// <see cref="LumraBellowOfTheWoodsFactory"/> for the return-all-lands half).
///
/// ## Implemented (v1)
/// - <b>ETB trigger (CR 603.6a)</b>: "When this creature enters, mill three
///   cards." Fires on <see cref="Triggers.OnEnterBattlefieldSelf"/>; mills 3
///   from the controller's library via <see cref="Fx.Mill"/> (CR 701.13). If
///   the library has fewer than 3 cards, all remaining are milled and this does
///   not directly cause a loss (the loss only fires later from an empty-library
///   draw — CR 704.5b). Controller is read at resolution time (CR 608.2).
/// - <b>Sacrifice activated ability (CR 602)</b>: "{3}{G}, Sacrifice this
///   creature: Return all land cards from your graveyard to the battlefield
///   tapped." Cost = <see cref="ManaCostCost"/>("{3}{G}") +
///   <see cref="SacrificeSelfCost"/> (CR 701.16 — sacrifice the source). On
///   resolution it snapshots every land card in the controller's graveyard and
///   returns each to the battlefield tapped via
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> (ZoneService-routed when
///   one is registered so ETB triggers / replacements on the returned lands
///   fire — CR 603.6a / CR 614), then taps each (CR 701.18 — they enter
///   tapped). The snapshot is taken before the loop because the move mutates
///   the graveyard in place. Aftermath Analyst itself is already in the
///   graveyard as a sacrifice cost by resolution, but it is a creature, never a
///   land, so it is never returned.
///
/// ## Wiring
/// - <see cref="Create(Player)"/> attaches the ETB trigger + the activated
///   ability to the card shape but does NOT register the trigger with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / shape tests —
///   mirrors <see cref="StitchersSupplierFactory"/>'s two-arg pattern. The
///   activated ability resolves through the controller's registered
///   <see cref="ZoneServiceRegistry"/> entry when present, raw-zone fallback
///   otherwise.
/// - <see cref="Create(Player, TriggerManager)"/> additionally registers the
///   ETB trigger with the live <see cref="TriggerManager"/> so an ETB
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> places it on
///   the stack automatically (CR 603.3).
/// </summary>
[CardName("Aftermath Analyst")]
public static class AftermathAnalystFactory
{
    public const string CardName = "Aftermath Analyst";
    public const string Slug = "aftermath-analyst";

    /// <summary>Number of cards milled by the ETB trigger (printed value,
    /// CR 701.13).</summary>
    public const int MillCount = 3;

    /// <summary>Printed activation cost of the sacrifice ability (CR 117.5).</summary>
    public const string ActivationCost = "{3}{G}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Aftermath Analyst with its ETB trigger + sacrifice ability
    /// attached to the card shape but the trigger NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Aftermath Analyst with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant <c>CardMovedEvent</c> places it on the stack
    /// automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, mill three cards."
        // Mills 3 from the controller's library (CR 701.13). Controller is
        // read at resolution time so a control-change between trigger
        // placement and resolution mills the *current* controller (CR 608.2).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName} ETB: mill {MillCount}",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                Fx.Mill(controller, MillCount);
                return ValueTask.CompletedTask;
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
        // Sacrifice activated ability — CR 602.
        //   "{3}{G}, Sacrifice this creature: Return all land cards from your
        //    graveyard to the battlefield tapped."
        // Cost = {3}{G} mana (CR 117.5) + sacrifice the source (CR 701.16).
        // On resolution, snapshot every land card in the controller's
        // graveyard and return each to the battlefield tapped.
        // ----------------------------------------------------------------
        var returnLandsEffect = new Effect(
            $"{CardName}: return all land cards from your graveyard to the battlefield tapped",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                ReturnAllLands(controller);
                return ValueTask.CompletedTask;
            });

        var sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationCost),
                new SacrificeSelfCost(card),
            },
            effects: new IEffect[] { returnLandsEffect });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// Return every land card in <paramref name="controller"/>'s graveyard to
    /// the battlefield tapped (CR 701.18). The land set is snapshotted up front
    /// because <see cref="Fx.ReturnFromGraveyardToBattlefield"/> mutates the
    /// graveyard in place. Routes through the controller's registered
    /// <see cref="ZoneServiceRegistry"/> entry when present so ETB triggers /
    /// replacements on the returned lands fire (CR 603.6a / CR 614); raw-zone
    /// fallback otherwise. Exposed for tests; mirrors the live effect closure.
    /// </summary>
    public static void ReturnAllLands(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var zones = ZoneServiceRegistry.Get(controller);
        var lands = controller.Zones.Graveyard.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();

        foreach (var land in lands)
        {
            Fx.ReturnFromGraveyardToBattlefield(land, controller, zones);
            // CR 701.18 — the returned permanents enter the battlefield tapped.
            if (land is Permanent perm && perm.Zone == ZoneType.Battlefield && !perm.IsTapped)
            {
                perm.Tap();
            }
        }
    }
}
