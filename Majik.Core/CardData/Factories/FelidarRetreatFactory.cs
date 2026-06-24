using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Felidar Retreat (Zendikar Rising, {3}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Landfall — Whenever a land you control enters, choose one —
///      • Create a 2/2 white Cat Beast creature token.
///      • Put a +1/+1 counter on each creature you control. Those creatures
///        gain vigilance until end of turn."
///
/// ## Implementation
/// Base shape (name, Enchantment, {3}{W}) is materialised from the embedded
/// JSON definition (<c>felidar-retreat.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="BristlyBillSpineSowerFactory"/>; the landfall trigger is layered
/// on top (the JSON <c>AbilityDefinition</c> schema does not yet express
/// landfall triggers).
///
/// No new mechanic — this composes existing primitives:
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142):
///   fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "a land entering the battlefield under the controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate (same
///   predicate as <see cref="SteppeLynxFactory"/> /
///   <see cref="TirelessProvisionerFactory"/>).
/// - <b>Modal resolve</b> (CR 700.2 — "choose one"): on resolution the
///   controller's agent picks between mode 0 = "Create a 2/2 white Cat Beast
///   creature token" and mode 1 = "Put a +1/+1 counter on each creature you
///   control; those creatures gain vigilance until end of turn". The choice
///   routes through <see cref="IPlayerAgent.ChooseModeAsync"/> (sync-over-async
///   call site mirroring <see cref="TirelessProvisionerFactory.PickModeAsync"/>
///   — wait, see note), each mode carrying a <see cref="BotIntent"/> hint
///   (Token for the Cat, Buff for the team pump). When no agent is registered
///   the factory falls back to the token mode (deterministic — a body on the
///   board is the more frequently-correct EV pick, mirroring Tireless
///   Provisioner's Treasure default).
/// - <b>Mode 0 — token</b>: a 2/2 white Cat Beast creature token via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>,
///   threading the optional <see cref="ZoneService"/> so the token's ETB
///   CardMovedEvent fires (CR 111.4 — explicit white colour stamp; CR 111.6 —
///   enters the battlefield directly).
/// - <b>Mode 1 — counters + vigilance</b>: snapshot the controller's
///   battlefield creatures (CR 608.2), add one
///   <see cref="CounterType.PlusOnePlusOne"/> counter to each (CR 122), and
///   grant each "Vigilance" until end of turn via
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> (CR 702.21 / CR 613.1f
///   Layer 6; CR 514.2 cleanup expiry — read back by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/>). The same
///   counter + UEOT-keyword-grant primitives used by
///   <see cref="InspiringCallFactory"/>. Creatures whose effects service is
///   unwired (shape-only tests) still get the counter but are skipped for the
///   grant.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the trigger for inspection but does not register it with a
///   bus. Use the <see cref="Create(Player, ZoneService?, TriggerManager?)"/>
///   overload for live firing.
/// </summary>
[CardName("Felidar Retreat")]
public static class FelidarRetreatFactory
{
    public const string CardName = "Felidar Retreat";
    public const string Slug = "felidar-retreat";
    public const string PrintedManaCost = "{3}{W}";

    /// <summary>Mode index for "Create a 2/2 white Cat Beast creature token".</summary>
    public const int ModeToken = 0;
    /// <summary>Mode index for "Put a +1/+1 counter on each creature you control;
    /// those creatures gain vigilance until end of turn".</summary>
    public const int ModeCountersVigilance = 1;

    /// <summary>Cat Beast token P/T (CR 111.4).</summary>
    public const int TokenPower = 2;
    public const int TokenToughness = 2;

    /// <summary>Keyword granted until end of turn by mode 1 (CR 702.21).</summary>
    public const string GrantedKeyword = "Vigilance";

    private static readonly IReadOnlyList<string> Modes = new[]
    {
        "Create a 2/2 white Cat Beast creature token.",
        "Put a +1/+1 counter on each creature you control. Those creatures gain vigilance until end of turn.",
    };

    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Token,   // mode 0 → a body on the board.
        BotIntent.Buff,    // mode 1 → team-wide +1/+1 + vigilance.
    };

    /// <summary>
    /// Construct Felidar Retreat with no live ZoneService / TriggerManager
    /// wiring. The landfall trigger is attached for shape inspection but not
    /// registered with a bus, and a resolved token would bypass ZoneService.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Felidar Retreat. When <paramref name="zoneService"/> is
    /// supplied a mode-0 token is placed onto the battlefield via ZoneService
    /// so CardMovedEvent fires. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered with the bus so a CardMovedEvent for
    /// a land entering under the controller's control automatically queues the
    /// ability.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {3}{W}). The JSON carries no abilities — the landfall trigger is
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Enchantment)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Landfall trigger — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, choose one — ..."
        // Predicate identical to Tireless Provisioner's landfall gate.
        // ----------------------------------------------------------------
        var modalEffect = new Effect(
            $"{CardName}: landfall — choose one — create a 2/2 white Cat Beast token; or +1/+1 counter on each creature you control + vigilance UEOT",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                var mode = await PickModeAsync(controller, ctx).ConfigureAwait(false);
                if (mode == ModeCountersVigilance)
                {
                    ApplyCountersAndVigilance(controller);
                }
                else
                {
                    // Default / mode 0: a 2/2 white Cat Beast token.
                    CreateCatBeastToken(controller, zoneService);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { modalEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Mode 0 — CR 111.4 / CR 111.6: create a 2/2 white Cat Beast creature
    /// token and put it onto the battlefield under the controller's control.
    /// The explicit white colour is stamped on the token (CR 105) so
    /// colour-matters effects see it correctly.
    /// </summary>
    public static void CreateCatBeastToken(Player controller, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Cat Beast",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Cat, CardSubtype.Beast },
            Colors: new[] { ManaColor.White });

        TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }

    /// <summary>
    /// Mode 1 — CR 122 / CR 702.21 / CR 514.2: put a +1/+1 counter on each
    /// creature the controller controls and grant each of those creatures
    /// vigilance until end of turn. Snapshot the battlefield once so the set
    /// is fixed at resolution (CR 608.2). Creatures whose
    /// <see cref="Creature.ActiveEffects"/> is unwired (shape-only tests) still
    /// receive the counter but are skipped for the grant — mirrors
    /// <see cref="InspiringCallFactory.BuildResolveEffect"/>.
    /// </summary>
    public static void ApplyCountersAndVigilance(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var creatures = controller.Zones.Battlefield
            .GetCards()
            .OfType<Creature>()
            .Where(c => c.Zone == ZoneType.Battlefield)
            .ToList();

        foreach (var creature in creatures)
        {
            // CR 122 — put a +1/+1 counter on each.
            creature.Counters.Add(CounterType.PlusOnePlusOne, 1);

            // CR 702.21 / CR 613.1f Layer 6 — grant vigilance until end of turn
            // (CR 514.2 cleanup expiry). Read back by
            // CombatAbilities.HasVigilance.
            creature.ActiveEffects?.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, GrantedKeyword));
        }
    }

    /// <summary>
    /// Consult <paramref name="controller"/>'s agent (via
    /// <see cref="AgentRegistry"/>) for the modal choice. Returns
    /// <see cref="ModeToken"/> deterministically when no agent is registered.
    /// Sync-over-async pattern mirrors
    /// <see cref="TirelessProvisionerFactory"/>'s modal call site.
    /// </summary>
    private static async ValueTask<int> PickModeAsync(Player controller, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        if (agent == null) return ModeToken;

        int pick;
        try
        {
            pick = await agent.ChooseModeAsync(
                    ctx.Game!,
                    modes: Modes,
                    modeIntents: ModeIntents)
                .ConfigureAwait(false);
        }
        catch
        {
            // Agents that hard-require a non-null GameContext fall back to the
            // deterministic token pick (CR 700.2 — a choice must still be made).
            return ModeToken;
        }

        // Clamp out-of-range agent picks to the token default
        // (defensive — CR 700.2 requires a legal mode).
        return pick == ModeCountersVigilance ? ModeCountersVigilance : ModeToken;
    }
}
