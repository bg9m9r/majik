using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tireless Provisioner (Streets of New Capenna,
/// {2}{G}).
///
/// Creature — Elf Scout 3/2. Oracle text:
///   "Whenever a land enters under your control, choose one —
///      • Create a Treasure token.
///      • Create a Food token."
///
/// ## Implemented (v1)
/// - 3/2 Creature — Elf Scout, mana cost {2}{G}.
/// - <b>Landfall-style triggered ability</b> (CR 603.1 / 603.6a) over
///   <see cref="CardMovedEvent"/>: fires when a land enters the battlefield
///   under the controller's control (CR 614 — same "Whenever a land enters
///   under your control" predicate used by
///   <see cref="TirelessTrackerFactory"/> and
///   <see cref="Triggers.OnLandEntersUnderControl"/>).
/// - <b>Modal resolve</b> (CR 700.2 — "choose one"): on resolve the trigger
///   asks the controller's agent (via <see cref="AgentRegistry"/>) to pick
///   between mode 0 = "Create a Treasure token" and mode 1 = "Create a Food
///   token". The choice routes through
///   <see cref="IPlayerAgent.ChooseModeAsync"/> with both modes carrying a
///   <see cref="BotIntent"/> hint so resource-aware bots can pick (Ramp
///   for Treasure, Heal for Food). When no agent is registered the
///   factory falls back to creating a Treasure token (deterministic — the
///   more frequent EV choice in Modern's ramp / artifacts shells).
/// - <b>Token routing</b>: both tokens are created via
///   <see cref="TokenFactory.CreateTreasure"/> /
///   <see cref="TokenFactory.CreateFood"/>, threading the optional
///   <see cref="ZoneService"/> so the token's own ETB CardMovedEvent fires
///   for downstream subscribers (same shape as Tireless Tracker's Clue
///   creation).
///
/// ## Deferred (v1 gaps)
/// - <b>Per-token sac cost enforcement</b>: Treasure and Food are wired by
///   <see cref="TokenFactory"/> with the same v1 limitations the rest of
///   the engine carries — Treasure's "{T}, Sacrifice this artifact" is
///   surfaced as five colour-pip <see cref="ManaAbility"/>s without an
///   actual sac cost (ManaAbility additional-cost gap), and Food's
///   "{2}, {T}, Sacrifice: gain 3 life" is wired correctly via
///   <see cref="AdditionalCost.Sacrifice"/>. No change here.
/// - <b>Agent prompt UX</b>: the modal choice is a sync-over-async call
///   on <see cref="IPlayerAgent.ChooseModeAsync"/> (same posture as
///   <see cref="ScapeshiftFactory"/>'s library tutor), not a dedicated
///   prompt surface. Production agents already implement ChooseModeAsync;
///   this factory just calls it inside the trigger effect.
/// </summary>
[CardName("Tireless Provisioner")]
public static class TirelessProvisionerFactory
{
    public const string CardName = "Tireless Provisioner";
    public const string Cost = "{2}{G}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>Mode index for "Create a Treasure token".</summary>
    public const int ModeTreasure = 0;
    /// <summary>Mode index for "Create a Food token".</summary>
    public const int ModeFood = 1;

    private static readonly IReadOnlyList<string> Modes = new[]
    {
        "Create a Treasure token",
        "Create a Food token",
    };

    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Ramp,        // Treasure → mana acceleration.
        BotIntent.Heal,        // Food → 3 life sac later.
    };

    /// <summary>
    /// Construct Tireless Provisioner with no live ZoneService /
    /// TriggerManager wiring. The landfall trigger is attached for shape but
    /// not registered with a bus, and the resolved token bypasses
    /// ZoneService. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Tireless Provisioner. When <paramref name="zoneService"/>
    /// is supplied the Treasure / Food token is placed onto the battlefield
    /// via ZoneService so CardMovedEvent fires (downstream ETB listeners
    /// observe the token). When <paramref name="triggers"/> is supplied the
    /// landfall trigger is registered with the bus so a CardMovedEvent for
    /// a land automatically queues the ability.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Landfall trigger — CR 603.1 / 603.6a / CR 614.
        //   "Whenever a land enters under your control, choose one —
        //    Create a Treasure token; Create a Food token."
        // Predicate identical to TirelessTrackerFactory's landfall gate.
        // ----------------------------------------------------------------
        var condition = Triggers.OnLandEntersUnderControl(owner);

        var modalEffect = new Effect(
            $"{CardName}: choose one — create a Treasure token; or create a Food token",
            () =>
            {
                var controller = card.Controller ?? owner;
                var mode = PickMode(controller);
                if (mode == ModeFood)
                {
                    TokenFactory.CreateFood(controller, zoneService);
                }
                else
                {
                    // Default / mode 0: Treasure.
                    TokenFactory.CreateTreasure(controller, zoneService);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { modalEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Consult <paramref name="controller"/>'s agent (via
    /// <see cref="AgentRegistry"/>) for the modal choice. Returns
    /// <see cref="ModeTreasure"/> deterministically when no agent is
    /// registered. Sync-over-async pattern mirrors ScapeshiftFactory's
    /// library-tutor call site.
    /// </summary>
    private static int PickMode(Player controller)
    {
        var agent = AgentRegistry.Get(controller);
        if (agent == null) return ModeTreasure;

        int pick;
        try
        {
            pick = agent.ChooseModeAsync(
                    ctx: null!,
                    modes: Modes,
                    modeIntents: ModeIntents)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Agents that hard-require a non-null GameContext fall back to
            // the deterministic Treasure pick (CR 700.2 — a choice must be
            // made; we still make one).
            return ModeTreasure;
        }

        // Clamp out-of-range agent picks to the Treasure default
        // (defensive — CR 700.2 requires a legal mode).
        return pick == ModeFood ? ModeFood : ModeTreasure;
    }
}
