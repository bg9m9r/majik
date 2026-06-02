using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glint-Sleeve Siphoner (Aether Revolt, {1}{B}).
///
/// Creature — Human Rogue 2/1. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Menace
///    Whenever this creature enters or attacks, you get {E} (an energy
///    counter).
///    At the beginning of your upkeep, you may pay {E}{E}. If you do, you
///    draw a card and you lose 1 life."
///
/// A Dimir energy two-drop: each time it enters or attacks it banks one
/// energy, then on each of your upkeeps you may cash in two energy to draw
/// a card (and lose 1 life). Base shape (name, Creature, Human Rogue
/// subtypes, {1}{B}, 2/1) is materialised from the embedded JSON definition
/// (<c>glint-sleeve-siphoner.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the Menace marker, the
/// enters-or-attacks energy trigger, and the pay-{E}{E} upkeep draw trigger
/// are layered on in C# (the JSON <c>AbilityDefinition</c> schema does not
/// express keyword markers, energy gain, enters/attack triggers, optional
/// "may pay" riders, or draw/life-loss effects — same posture as
/// <see cref="VoltaicBrawlerFactory"/>, the energy analogue this reuses,
/// and <see cref="FlamebladeAdeptFactory"/> for the Menace marker).
///
/// ## Implemented (v1)
/// - <b>Creature — Human Rogue {1}{B} 2/1</b> from JSON.
/// - <b>Menace</b> (CR 702.111) — <see cref="KeywordAbility"/> marker,
///   consumed by <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/>
///   (same posture as <see cref="FlamebladeAdeptFactory"/> /
///   <see cref="InsolentNeonateFactory"/>).
/// - <b>Enters-or-attacks energy trigger</b> (CR 603.6a ETB + CR 508.1f
///   attack + CR 106.13b energy) — printed as a single ability with two
///   trigger events. Modelled as TWO <see cref="TriggeredAbility"/>
///   instances sharing one resolution body (one over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>, one over
///   <see cref="Triggers.OnAttackSelf"/>), because each printed event is a
///   distinct trigger condition that puts its own copy on the stack
///   (CR 603.2e — a single ability with multiple trigger events fires once
///   per matching event). Each resolution calls
///   <see cref="Player.GainEnergy"/>(1) on the controller (one {E}).
/// - <b>Pay-{E}{E} upkeep draw trigger</b> (CR 500.4 upkeep / CR 117.5
///   optional "may pay" / CR 120.2 draw / CR 119.3 life loss) — a
///   <see cref="TriggeredAbility"/> over <see cref="Triggers.OnStepBegin"/>
///   filtered to <see cref="Majik.Core.StateMachine.PhaseStateType.Upkeep"/>
///   and the controller (same upkeep-trigger shape as
///   <see cref="TheOneRingFactory"/>). On resolution the controller's
///   registered <see cref="IPlayerAgent"/> is consulted for the optional
///   {E}{E} payment via <see cref="IPlayerAgent.ChooseYesNoAsync(string, BotIntent, System.Threading.CancellationToken)"/>:
///     - <b>Agent present</b>: ask "pay {E}{E} to draw a card and lose 1
///       life?" with <see cref="BotIntent.CardAdvantage"/> |
///       <see cref="BotIntent.LoseLife"/> intent (the bot weighs a net
///       card draw against the 1-life downside).
///     - <b>No agent</b> (shape-only / pre-agent path): default to paying
///       when affordable — a card for two energy and 1 life is a strong
///       net upside (mirrors Voltaic Brawler's affordable-default fallback).
///   When the payment is made (and affordable) the controller spends two
///   energy via <see cref="Player.PayEnergy"/>, draws one card (CR 120.2 —
///   library-top move; empty library flags
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> per CR 704.5b),
///   and loses 1 life (<see cref="Player.LoseLife"/>). Per CR 117.5 the
///   payment is bounded by affordability — fewer than two energy means no
///   draw and no life loss.
///
/// ## Deferred (v1 gaps)
/// - <b>Live TriggerManager wiring</b>: the shape-only
///   <see cref="Create(Player)"/> path attaches all three triggers for
///   inspection but does not register them with a bus. Use the
///   <see cref="Create(Player, TriggerManager)"/> overload for live firing
///   (mirrors Voltaic Brawler's two-arg pattern).
/// - <b>Resolution-time "may pay" prompt</b>: same posture as Voltaic
///   Brawler — <see cref="IPlayerAgent.ChooseYesNoAsync"/> is the pragmatic
///   v1 surface for the resolve-time optional cost (the printed "you may
///   pay {E}{E}" is a resolution decision, CR 603.4).
/// </summary>
[CardName("Glint-Sleeve Siphoner")]
public static class GlintSleeveSiphonerFactory
{
    public const string CardName = "Glint-Sleeve Siphoner";
    public const string Slug = "glint-sleeve-siphoner";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>Energy banked each time it enters or attacks — {E}
    /// (CR 106.13b).</summary>
    public const int EnergyGainPerTrigger = 1;

    /// <summary>Energy spent on the optional upkeep draw — {E}{E}.</summary>
    public const int UpkeepEnergyCost = 2;

    /// <summary>Life lost when the optional upkeep payment is made.</summary>
    public const int UpkeepLifeLoss = 1;

    /// <summary>
    /// Construct Glint-Sleeve Siphoner with no live <see cref="TriggerManager"/>
    /// wiring. The Menace marker, the enters/attacks energy triggers, and
    /// the pay-{E}{E} upkeep draw trigger are attached for shape inspection;
    /// none is registered with a bus. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Glint-Sleeve Siphoner. When <paramref name="triggers"/> is
    /// supplied all three triggered abilities are registered so a self-ETB
    /// <see cref="Majik.Core.Events.CardMovedEvent"/>, a self
    /// <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>, and
    /// a controller upkeep <see cref="Majik.Core.Events.StepStartedEvent"/>
    /// automatically queue their abilities.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human Rogue subtypes, {1}{B}, 2/1). The JSON carries no abilities —
        // Menace + the three triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.111 — Menace keyword marker. Consumed by
        // CombatAbilities.HasMenace at block-declaration time.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // "Whenever this creature enters or attacks, you get {E}."
        //   (CR 603.6a ETB + CR 508.1f attack + CR 106.13b energy.)
        // Printed as a single ability with two trigger events. Each event
        // is its own trigger condition that puts a copy on the stack
        // (CR 603.2e), so we wire TWO TriggeredAbility instances sharing a
        // common one-energy resolution body. No target — the ability names
        // the controller ("you get {E}").
        // ----------------------------------------------------------------
        IEffect EnergyEffect() => new Effect(
            $"{CardName}: you get {{E}} (an energy counter)",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainEnergy(EnergyGainPerTrigger);
            });

        var entersTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { EnergyEffect() },
            activeZones: new[] { ZoneType.Battlefield });

        var attacksTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { EnergyEffect() },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(entersTrigger);
        card.AddAbility(attacksTrigger);
        triggers?.RegisterTriggeredAbility(entersTrigger);
        triggers?.RegisterTriggeredAbility(attacksTrigger);

        // ----------------------------------------------------------------
        // "At the beginning of your upkeep, you may pay {E}{E}. If you do,
        //  you draw a card and you lose 1 life."
        //   (CR 500.4 upkeep / CR 117.5 optional "may pay" / CR 120.2 draw
        //    / CR 119.3 life loss.)
        //
        // OnStepBegin(controller, Upkeep) — your-upkeep trigger (same shape
        // as The One Ring). On resolution: consult the controller's agent
        // for the optional {E}{E}; when paid (and affordable), spend two
        // energy, draw one card (library-top move; empty library flags the
        // SBA per CR 704.5b), and lose 1 life.
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            $"{CardName}: may pay {{E}}{{E}} — if you do, draw a card and lose {UpkeepLifeLoss} life",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 117.5 — "you may pay" is optional and bounded by
                // affordability. Decide first whether to pay.
                if (controller.EnergyCounters < UpkeepEnergyCost) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                bool pay;
                if (agent != null)
                {
                    // Resolution-time optional-cost prompt (CR 603.4). A
                    // card for {E}{E} plus 1 life is card advantage with a
                    // small life downside.
                    pay = await agent.ChooseYesNoAsync(
                            $"{CardName}: pay {{E}}{{E}} to draw a card and lose {UpkeepLifeLoss} life?",
                            BotIntent.CardAdvantage | BotIntent.LoseLife)
                        .ConfigureAwait(false);
                }
                else
                {
                    // No-agent fallback: pay when affordable — drawing a card
                    // for two energy and 1 life is strong net upside (mirrors
                    // Voltaic Brawler's affordable-default posture).
                    pay = true;
                }

                if (!pay) return;
                if (!controller.PayEnergy(UpkeepEnergyCost)) return;

                // CR 120.2 — draw one card (library-top move). Empty library
                // flags the draw-from-empty SBA (CR 704.5b).
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                }
                else
                {
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }

                // CR 119.3 — you lose 1 life.
                controller.LoseLife(UpkeepLifeLoss);
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(
                owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        return card;
    }
}
