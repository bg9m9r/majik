using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kataki, War's Wage (Saviors of Kamigawa, {1}{W}).
///
/// Legendary Creature — Spirit, 2/1. Oracle text (verified against Scryfall):
///   "All artifacts have 'At the beginning of your upkeep, sacrifice this
///    artifact unless you pay {1}.'"
///
/// ## Implementation
///
/// The "All artifacts have '…'" clause is a CR 613.1f Layer-6 group
/// ability-grant of a TRIGGERED ability, wired via
/// <see cref="GrantAbilityToGroupLifecycle"/> /
/// <see cref="GrantAbilityToGroupStaticEffect"/>. This is the triggered
/// sibling of #2322 (Chromatic Lantern), which granted only activated / mana
/// abilities to a group. The new piece is the live
/// <see cref="TriggerManager"/> wiring: a group-granted triggered ability
/// must be REGISTERED with the manager to fire (the bearer doesn't cross a
/// zone boundary when granted, so the manager's auto-bind on
/// <see cref="CardMovedEvent"/> never sees it). The lifecycle registers /
/// unregisters each granted upkeep tax as artifacts enter / leave (CR 611.2c)
/// and as Kataki itself enters / leaves play (CR 613.6e).
///
/// <para><b>Scope is symmetric — "ALL artifacts".</b> Every artifact on the
/// battlefield, regardless of controller, is taxed. Each granted trigger is
/// scoped to the BEARER's own controller: it fires on that controller's
/// upkeep (<see cref="Triggers.OnStepBegin"/> filtered to the bearer's
/// controller, CR 603.1) and that controller is the one who must pay {1} or
/// sacrifice the artifact (CR 701.16 — sacrifice). The {1} is paid from the
/// bearer-controller's mana pool.</para>
///
/// <para>The granted trigger uses the bearer artifact as its
/// <c>Source</c>, so the upkeep tax stops firing the instant the artifact
/// leaves play, and "sacrifice THIS artifact" sacrifices the bearer.</para>
///
/// <para><b>Cost-payment prompt</b>: at resolution the bearer-controller's
/// agent is prompted "Pay {1} to keep this artifact?" via the shared
/// <see cref="Majik.Core.Primitives.UpkeepPayUnlessConsequence"/> primitive
/// (CR 117.1); on "yes" + affordable {1} is drained, on "no" / can't-afford
/// the sacrifice tail fires. The legacy / shape-only sync path keeps the
/// deterministic "pay if able" posture — same wiring Stasis / Mana Vault /
/// the pact cycle now share.</para>
///
/// ## Deferred (v1 gaps)
/// - <b>No in-trigger tap-lands step</b>: the {1} is paid from whatever is
///   already in the bearer-controller's pool when the granted trigger
///   resolves — the decision flows through the agent prompt, but there is no
///   resolution-time "tap a land for {1}" sub-prompt.
/// - <b>Production membership = all battlefields.</b> The effects-aware
///   instance-swap route (<c>NamedCardFactory.Create(name, owner, effects)</c>)
///   reaches only a 2-arg <c>Create(Player, ContinuousEffectsService)</c>
///   overload — no <see cref="TriggerManager"/>, no opponent-battlefield
///   enumeration, no event bus. So a fully-live Kataki in a real match needs
///   the 5-arg <see cref="Create(Player, ContinuousEffectsService, IEventBus, TriggerManager, System.Func{System.Collections.Generic.IEnumerable{Permanent}})"/>
///   overload to be invoked with a whole-board membership provider — the same
///   instance-swap prod-wiring residual already tracked for the group-grant
///   family (Chromatic Lantern's 3-arg lifecycle overload, the
///   LordStaticEffect cluster). The seam itself is generic and fully tested.
/// </summary>
[CardName("Kataki, War's Wage")]
public static class KatakiWarsWageFactory
{
    public const string CardName = "Kataki, War's Wage";
    public const string PrintedManaCost = "{1}{W}";
    public const string UpkeepTax = "{1}";

    /// <summary>
    /// Shape-only constructor — Kataki with correct identity (Legendary Spirit
    /// 2/1) but no live group grant. Suitable for factory-shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null, triggers: null, membershipProvider: null);

    /// <summary>
    /// Effects-aware overload matched by the source generator's production
    /// instance-swap route. Wires the Layer-6 grant against the live service.
    ///
    /// <para>CR 110.2 / 700.6 / 611.2c — Kataki's grant is SYMMETRIC ("All
    /// artifacts have …"), so its candidate set must span BOTH battlefields,
    /// not just the controller's own zone. When the live game graph has wired a
    /// <see cref="ContinuousEffectsService.PlayersProvider"/> (GameFacade /
    /// Game), this overload uses the whole-battlefield gatherer so an OPPONENT's
    /// artifacts — and a stolen artifact you control but an opponent owns, which
    /// lives in the owner's battlefield zone — are taxed too. The live event bus
    /// is taken from <see cref="ContinuousEffectsService.EventBus"/> so the
    /// lifecycle tracks artifacts entering / leaving / changing control. Falls
    /// back to the controller's own battlefield only when no roster is wired
    /// (pure card-shape tests).</para>
    ///
    /// <para>The live <see cref="TriggerManager"/> still requires the 5-arg
    /// overload (no per-game trigger-manager registry exists to recover it from
    /// the service) — see the type's Deferred note. Enumeration is whole-board
    /// here regardless.</para>
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
        => Create(
            owner,
            effects,
            eventBus: effects?.EventBus,
            triggers: null,
            membershipProvider: effects?.PlayersProvider is { } roster
                ? BattlefieldGroupGatherer.WholeBattlefield(roster)
                : null);

    /// <summary>
    /// Fully-wired Kataki. When <paramref name="effects"/> is supplied a
    /// <see cref="GrantAbilityToGroupLifecycle"/> attaches the Layer-6 grant
    /// of the per-artifact upkeep tax; when <paramref name="triggers"/> is
    /// supplied each granted trigger is registered with the manager so it
    /// fires; <paramref name="membershipProvider"/> supplies the candidate set
    /// ("all artifacts" = every player's battlefield) — defaults to the
    /// controller's battlefield when null.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers,
        System.Func<System.Collections.Generic.IEnumerable<Permanent>>? membershipProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var kataki = new Creature(
            CardName,
            PrintedManaCost,
            power: 2,
            toughness: 1,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Spirit });
        kataki.SetOwner(owner);
        kataki.SetController(owner);

        if (effects != null)
        {
            var membership = membershipProvider ?? (() => ControllerBattlefield(kataki));

            // CR 613.1f — "All artifacts have 'At the beginning of your upkeep,
            // sacrifice this artifact unless you pay {1}.'" Grant the upkeep
            // tax to every artifact on the battlefield; live membership.
            var lifecycle = new GrantAbilityToGroupLifecycle(
                kataki,
                effects,
                eventBus,
                scope: p => p.HasType(CardType.Artifact),
                abilityFactory: member => BuildUpkeepTax(member),
                membershipProvider: membership,
                triggers: triggers);
            lifecycle.Attach();
        }

        return kataki;
    }

    /// <summary>
    /// Build the granted "At the beginning of your upkeep, sacrifice this
    /// artifact unless you pay {1}" trigger for one artifact
    /// <paramref name="bearer"/>. Controller-scoped to the bearer's OWN
    /// controller (CR 603.1) — "your upkeep" is the artifact-controller's
    /// upkeep, and that controller pays / sacrifices.
    /// </summary>
    private static IReadOnlyList<IAbility> BuildUpkeepTax(Permanent bearer)
    {
        var bearerController = bearer.Controller
            ?? throw new InvalidOperationException(
                "Cannot grant Kataki's upkeep tax: artifact has no controller.");

        // CR 701.16 / CR 117.1 — "sacrifice this artifact unless you pay {1}".
        // "you" is the artifact's CURRENT controller as the ability resolves
        // (control may have changed since the grant). At resolution that
        // controller's agent is prompted "Pay {1}?" via the shared
        // Majik.Core.Primitives.UpkeepPayUnlessConsequence primitive; on "yes"
        // + affordable {1} is drained, on "no" / can't-afford the artifact is
        // sacrificed. The legacy / shape-only sync path keeps "pay if able".
        var taxEffect = Majik.Core.Primitives.UpkeepPayUnlessConsequence.Build(
            "Kataki: at upkeep, sacrifice this artifact unless you pay {1}",
            bearerController,
            ManaCost.Parse(UpkeepTax),
            consequence: () =>
            {
                // Sacrifice — Battlefield -> Graveyard (CR 701.16). Raw zone
                // move, same shape as StasisFactory's upkeep tail.
                var payer = bearer.Controller ?? bearerController;
                payer.Zones.Battlefield.RemoveCard(bearer);
                payer.Zones.Graveyard.AddCard(bearer);
                bearer.SetZone(ZoneType.Graveyard);
            },
            promptText: "Pay {1} to keep this artifact?",
            guard: () => bearer.Zone == ZoneType.Battlefield);

        var tax = new TriggeredAbility(
            source: bearer,
            controller: bearerController,
            condition: Triggers.OnStepBegin(bearerController, StepStateType.Upkeep),
            effects: new IEffect[] { taxEffect },
            activeZones: new[] { ZoneType.Battlefield });

        return new IAbility[] { tax };
    }

    /// <summary>
    /// Default candidate set when no whole-board provider is supplied: the
    /// Kataki controller's own battlefield. (A symmetric "all artifacts"
    /// match needs every player's battlefield — supplied by the 5-arg
    /// overload's <c>membershipProvider</c>.)
    /// </summary>
    private static IEnumerable<Permanent> ControllerBattlefield(Creature kataki)
    {
        var controller = kataki.Controller;
        if (controller == null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
