using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Outcaster Trailblazer (Outlaws of Thunder Junction,
/// {2}{G}).
///
/// Creature — Human Druid 4/2 (mono-green). Oracle text (verified against
/// Scryfall):
///   "When this creature enters, add one mana of any color.
///    Whenever another creature you control with power 4 or greater enters,
///    draw a card.
///    Plot {2}{G} (You may pay {2}{G} and exile this card from your hand.
///    Cast it as a sorcery on a later turn without paying its mana cost.
///    Plot only as a sorcery.)"
///
/// The card's base shape (name, Creature, Human + Druid subtypes, {2}{G},
/// 4/2) is materialised from the embedded JSON definition
/// (<c>outcaster-trailblazer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed triggers are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// yet express ETB / power-gated-enters triggers, so they live in the
/// factory (same posture as <see cref="GlaringFleshrakerFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>ETB add-one-mana-of-any-color trigger (CR 603.1 / CR 605.1a)</b> —
///   fires on <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution the
///   controller's agent picks a colour (<see cref="Players.Agents.IPlayerAgent.ChooseColorAsync"/>,
///   CR 614.12-style colour prompt; fallback Green — the card's own colour —
///   on the no-agent dispatcher path) and one mana of that colour is added to
///   the controller's pool via <see cref="Player.AddManaToPool"/> (CR 106.1).
///   Note this is a TRIGGERED ability, not a mana ability (CR 605.1a — it has
///   a trigger condition and uses the stack), so it can't be activated for
///   mana mid-payment; it just fills the pool when it resolves.
/// - <b>Another-creature-you-control-with-power-4+-enters draw trigger
///   (CR 603.6a / CR 603.2c)</b> — fires on a <see cref="CardMovedEvent"/> →
///   Battlefield for a creature OTHER than this card, controlled by this
///   card's controller, whose power is 4 or greater (read at trigger time off
///   <see cref="Creature.Power"/> — CR 603.3e, the printed "power 4 or greater"
///   gate, same read shape as Big Game Hunter / Reprisal). On resolution the
///   controller draws a card (<see cref="Fx.DrawCards"/>). Outcaster
///   Trailblazer itself is power 4, but the printed "ANOTHER creature" clause
///   excludes it (the self-exclusion is the !ReferenceEquals guard), so its
///   own ETB does not draw.
///
/// ## Single-arg dispatcher path
///
/// The <see cref="Create(Player)"/> overload attaches both triggers
/// structurally (correct card shape for factory-shape / dispatch tests).
/// Neither trigger is registered with a <see cref="TriggerManager"/>; the
/// ETB-mana half falls back to Green (no agent), and the draw half draws from
/// the controller's live library. Production callers use the full overload.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Plot (CR 718)</b>: the printed "Plot {2}{G}" rider is NOT yet wired —
///   identical deferral to <see cref="SlickshotShowOffFactory"/>. Plot is a
///   cast-from-exile-on-a-later-turn alternative-cost mechanic (pay {2}{G} to
///   exile from hand with a plot marker; cast for {0} at sorcery speed on a
///   subsequent turn, once per turn — CR 718.2). No activated-from-hand
///   alt-cost / plotted-permission primitive exists in the engine yet; ship
///   the printed body, defer Plot until its primitive lands. The bot treats
///   Outcaster Trailblazer as a vanilla 4/2 with the two triggers until Plot
///   ships.
/// </summary>
[CardName("Outcaster Trailblazer")]
public static class OutcasterTrailblazerFactory
{
    public const string CardName = "Outcaster Trailblazer";
    public const string Slug = "outcaster-trailblazer";

    /// <summary>CR 603.3e — the printed "power 4 or greater" gate on the
    /// other-creature-enters draw trigger.</summary>
    public const int PowerThreshold = 4;

    /// <summary>
    /// Construct Outcaster Trailblazer with no live wiring. Both triggers are
    /// attached structurally (ETB add-mana + another-power-4+-enters draw) but
    /// NOT registered with a <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct a fully-wired Outcaster Trailblazer.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for registration. May be null —
    /// both triggers attach structurally but are not enrolled.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Druid subtypes, {2}{G}, 4/2). The JSON carries no abilities
        // — both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Trigger 1 — ETB add one mana of any color (CR 603.1).
        //   "When this creature enters, add one mana of any color."
        // The controller's agent picks the colour at resolution (CR 614.12-
        // style colour prompt). Green is the no-agent fallback (the card's own
        // colour). This is a TRIGGERED ability (CR 605.1a — it has a trigger
        // condition), NOT a mana ability, so it goes on the stack and fills the
        // pool when it resolves.
        // ----------------------------------------------------------------
        var manaEffect = new Effect(
            $"{CardName}: add one mana of any color (enters)",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 105.2 — the five colours; colourless is not a colour, so
                // it is excluded from "any color".
                var color = ManaColor.Green;
                if (ctx.Agent is { } agent)
                {
                    color = await agent
                        .ChooseColorAsync(ctx.Game, $"{CardName}: mana colour", fallback: ManaColor.Green, ctx.Ct)
                        .ConfigureAwait(false);
                }

                controller.AddManaToPool(ManaCost.Parse(SymbolFor(color)));
            });

        var manaTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { manaEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(manaTrigger);
        triggers?.RegisterTriggeredAbility(manaTrigger);

        // ----------------------------------------------------------------
        // Trigger 2 — another power-4+ creature you control enters → draw
        // (CR 603.6a).
        //   "Whenever another creature you control with power 4 or greater
        //    enters, draw a card."
        // Predicate mirrors Triggers.OnAnotherCreatureYouControlEnters (a
        // creature other than self entering the battlefield under this
        // controller — the Soul Warden shape), narrowed by the printed
        // "power 4 or greater" gate (read off Creature.Power at trigger time —
        // CR 603.3e, the same effective-power read as Big Game Hunter /
        // Reprisal). Self-exclusion (!ReferenceEquals) means Outcaster's own
        // ETB — it is itself power 4 — does NOT draw ("ANOTHER creature").
        // ----------------------------------------------------------------
        var drawCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Creature)
            && !ReferenceEquals(e.Card, card)
            && ReferenceEquals(e.Card.Controller, owner)
            && e.Card is Creature entering
            && entering.Power >= PowerThreshold);

        var drawEffect = new Effect(
            $"{CardName}: draw a card (another creature you control with power {PowerThreshold}+ entered)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, 1);
            });

        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: drawCondition,
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drawTrigger);
        triggers?.RegisterTriggeredAbility(drawTrigger);

        return card;
    }

    /// <summary>CR 107.4 — the single-pip mana symbol for a colour, used to
    /// build a one-mana <see cref="ManaCost"/> from the agent's pick. Colourless
    /// is never produced by "add one mana of any color" (CR 105.2c — not a
    /// colour); it maps to {G} defensively but is unreachable from the colour
    /// prompt.</summary>
    private static string SymbolFor(ManaColor color) => color switch
    {
        ManaColor.White => "{W}",
        ManaColor.Blue => "{U}",
        ManaColor.Black => "{B}",
        ManaColor.Red => "{R}",
        ManaColor.Green => "{G}",
        _ => "{G}",
    };
}
