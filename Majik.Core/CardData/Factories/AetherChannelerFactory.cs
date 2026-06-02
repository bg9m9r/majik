using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aether Channeler (Dominaria United, {2}{U}).
///
/// Creature — Human Wizard 2/1. Oracle text:
///   "When this creature enters, choose one —
///    • Create a 1/1 white Bird creature token with flying.
///    • Return another target nonland permanent to its owner's hand.
///    • Draw a card."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Human Wizard, mana cost {2}{U}. Colour identity blue
///   (derived from the {U} pip per CR 202.2c). Mana value 3 (CR 202.3).
/// - <b>ETB modal triggered ability</b> (CR 700.2d — "Choose one —",
///   CR 603.1 / CR 603.6a): three modes, one chosen at resolve time via
///   <see cref="IPlayerAgent.ChooseModeAsync"/>. Same modal-ETB shape as
///   <see cref="CharmingPrinceFactory"/> — a per-card mode index is captured
///   at factory time so tests can supply a deterministic mode without
///   registering a full agent, and a registered agent overrides it.
///
/// ## Modes
/// - <b>Mode 0 — Create a 1/1 white Bird with flying</b> (CR 111.4): mints a
///   token via <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>
///   (Bird subtype, white, Flying keyword). Same token shape as
///   <see cref="GlintNestCraneFactory"/>'s Bird cousin.
/// - <b>Mode 1 — Return another target nonland permanent to its owner's
///   hand</b> (CR 701.20): bounce via <see cref="Fx.BounceToHand(ICard, ZoneService?)"/>.
///   "Another" = distinct object from Aether Channeler itself (CR 115.5b);
///   "nonland" re-checked at resolution (CR 608.2b). Mirrors
///   <see cref="AetherAdeptFactory"/>'s bounce, with the "another nonland"
///   filter of Oblivion Ring's target gather.
/// - <b>Mode 2 — Draw a card</b> (CR 121.1): moves the top card of the
///   controller's library to hand; empty library flags the SBA loss via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> (CR 704.5b),
///   matching the Clue/Food draw posture in <see cref="TokenFactory"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only, no agent, no TriggerManager.
///   Defaults to mode 2 (draw a card) — the safest no-target default.
/// - <see cref="Create(Player, int, TriggerManager?)"/> — sets the mode index
///   at factory time via a captured closure; a registered
///   <see cref="IPlayerAgent"/> overrides it.
///
/// ## Deferred (v1 gaps)
/// - <b>True agent-driven mode prompt on stack entry</b>: the mode is captured
///   at factory time for test convenience (same posture as
///   <see cref="CharmingPrinceFactory"/>). When the engine's "mode choice on
///   stack entry" surface ships, the captured <c>mode</c> closure becomes the
///   wiring point for the agent call.
/// </summary>
[CardName("Aether Channeler")]
public static class AetherChannelerFactory
{
    public const string CardName = "Aether Channeler";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>Mode index for "Create a 1/1 white Bird with flying."</summary>
    public const int ModeToken = 0;
    /// <summary>Mode index for "Return another target nonland permanent to its owner's hand."</summary>
    public const int ModeBounce = 1;
    /// <summary>Mode index for "Draw a card."</summary>
    public const int ModeDraw = 2;

    /// <summary>Printed mode labels, in oracle order (CR 700.2d).</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Create a 1/1 white Bird creature token with flying.",
        "Return another target nonland permanent to its owner's hand.",
        "Draw a card.",
    };

    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Token,    // Create a Bird — develop the board.
        BotIntent.Bounce,   // Bounce a nonland permanent — tempo / removal.
        BotIntent.Draw,     // Draw a card — value.
    };

    /// <summary>
    /// Construct Aether Channeler. The ETB trigger is attached for shape
    /// inspection; supplying a <see cref="TriggerManager"/> additionally
    /// registers it on the bus.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="mode">Pre-selected mode (0=token, 1=bounce, 2=draw).
    /// Overridden by a registered <see cref="IPlayerAgent"/> if one is present
    /// in <see cref="AgentRegistry"/>. Defaults to <see cref="ModeDraw"/>
    /// (no-target, safest default for shape/dispatcher tests).</param>
    /// <param name="triggers">TriggerManager — required for bus-driven ETB.
    /// May be null.</param>
    public static Creature Create(Player owner, int mode = ModeDraw, TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, choose one —
        //    • Create a 1/1 white Bird creature token with flying.
        //    • Return another target nonland permanent to its owner's hand.
        //    • Draw a card."
        // Modal body — mode resolved via AgentRegistry / ctx.Agent (when an
        // agent is registered) or the supplied mode parameter.
        // Mode 1 declares a 0..1 target request (MinTargets=0 so modes 0/2
        // don't gate the ETB while the unchosen mode-1 carries a target slot).
        // CR 700.2d — "choose one" pick count is 1.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: choose one — create a Bird; bounce a nonland permanent; draw a card",
            async ctx =>
            {
                if (etbTrigger == null) return;

                var controller = card.Controller ?? owner;
                var chosenMode = await PickModeAsync(controller, mode, ctx).ConfigureAwait(false);

                switch (chosenMode)
                {
                    case ModeToken:
                        ExecuteCreateBird(controller);
                        break;

                    case ModeBounce:
                        ExecuteBounce(etbTrigger, card);
                        break;

                    case ModeDraw:
                        ExecuteDraw(controller);
                        break;
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                // Mode 1 target slot. MinTargets=0 so modes 0 and 2 don't
                // require a target to be chosen (CR 700.2d — only the chosen
                // mode's targeting is relevant). "Another nonland permanent"
                // (CR 115.5b — exclude self; CR 305.1 — nonland).
                new TargetRequest(
                    Description: "another target nonland permanent",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(c => !c.HasType(CardType.Land))
                        .Where(c => !ReferenceEquals(c, card))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // ------------------------------------------------------------------
    // Mode helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolve the mode to execute. Consults the registered agent's
    /// <see cref="IPlayerAgent.ChooseModeAsync"/> when available; falls back to
    /// the captured <paramref name="defaultMode"/> (the factory-time mode
    /// parameter). Same pattern as <see cref="CharmingPrinceFactory"/>.
    /// </summary>
    private static async ValueTask<int> PickModeAsync(Player controller, int defaultMode, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        if (agent == null) return defaultMode;

        try
        {
            var pick = await agent.ChooseModeAsync(
                    ctx.Game!,
                    modes: Modes,
                    modeIntents: ModeIntents)
                .ConfigureAwait(false);

            if (pick >= 0 && pick < Modes.Count) return pick;
        }
        catch
        {
            // Agent hard-requires a non-null context or throws — fall back to
            // the deterministic default (same posture as CharmingPrince).
        }

        return defaultMode;
    }

    /// <summary>
    /// Mode 0 — Create a 1/1 white Bird creature token with flying (CR 111.4).
    /// </summary>
    private static void ExecuteCreateBird(Player controller)
    {
        TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec(
                Name: "Bird",
                Power: 1,
                Toughness: 1,
                Subtypes: new[] { CardSubtype.Bird },
                Keywords: new[] { "Flying" },
                Colors: new[] { ManaColor.White }),
            controller);
    }

    /// <summary>
    /// Mode 1 — Return another target nonland permanent to its owner's hand
    /// (CR 701.20). "Another" + "nonland" re-checked at resolution (CR 608.2b).
    /// </summary>
    private static void ExecuteBounce(TriggeredAbility trigger, Creature source)
    {
        var chosen = trigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Permanent target) return;

        // CR 608.2b — resolution-time legality re-checks.
        if (target.Zone != ZoneType.Battlefield) return;
        if (ReferenceEquals(target, source)) return;       // "another"
        if (target.HasType(CardType.Land)) return;          // "nonland"

        Fx.BounceToHand(target);
    }

    /// <summary>
    /// Mode 2 — Draw a card (CR 121.1). Moves the top card of the controller's
    /// library to hand; empty library flags the SBA loss (CR 704.5b).
    /// </summary>
    private static void ExecuteDraw(Player controller)
    {
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            controller.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
