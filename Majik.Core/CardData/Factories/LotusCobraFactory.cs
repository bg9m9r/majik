using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lotus Cobra (Zendikar, {1}{G}).
///
/// Creature — Snake 2/1. Oracle text:
///   "Landfall — Whenever a land you control enters, you may add one mana
///    of any color."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Snake, mana cost {1}{G}, owner/controller stamped.
/// - <b>Landfall triggered ability (CR 614 / CR 603.1)</b>: fires on the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> condition — a
///   <see cref="CardMovedEvent"/> with destination = Battlefield, the
///   moved card has the Land card type, and its controller matches Lotus
///   Cobra's controller. Same primitive used by
///   <see cref="OmnathLocusOfCreationFactory"/>'s second landfall mana
///   bump and other landfall cards.
/// - <b>Add one mana of any color (CR 106)</b>: at resolution the
///   controller's mana pool receives a single coloured mana. The colour
///   is selected by the optional <paramref name="colorPicker"/> callback;
///   when omitted the v1 default is <see cref="ManaColor.Green"/> (Lotus
///   Cobra's own colour). The picker is consulted each time the ability
///   resolves so the bot / agent / test can return a fresh colour per
///   landfall trigger.
/// - <b>"You may"</b>: auto-accepted (same posture as other "you may add
///   mana" landfall effects — v1 simplification; explicit yes/no prompt
///   is deferred until the agent-prompt surface exists).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for the colour</b>: <see cref="IPlayerAgent"/> has no
///   <c>ChooseManaColorAsync</c> hook today. Callers pass a
///   <paramref name="colorPicker"/>; the dispatcher path uses Green by
///   default. Same deferral pattern as
///   <see cref="ManamorphoseFactory.BuildResolveEffect"/>'s colour pair.
/// - <b>"You may"</b>: auto-accepted (same gap as Bloodghast / Tireless
///   Tracker / Sun Titan).
/// </summary>
[CardName("Lotus Cobra")]
public static class LotusCobraFactory
{
    public const string CardName = "Lotus Cobra";
    public const string PrintedManaCost = "{1}{G}";

    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>Default colour added by the landfall trigger when no
    /// <c>colorPicker</c> is supplied — Lotus Cobra's own colour identity
    /// (Green). The v1 simplification mirrors
    /// <c>ManamorphoseFactory</c>'s default colour pair.</summary>
    public const ManaColor DefaultColor = ManaColor.Green;

    /// <summary>
    /// Construct Lotus Cobra with no live TriggerManager wiring. The
    /// landfall trigger is attached for shape so structural tests can
    /// observe it, but isn't registered with a bus; the default green
    /// colour is added on resolution.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, colorPicker: null);

    /// <summary>
    /// Construct Lotus Cobra with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the landfall trigger is
    /// registered with the bus so a CardMovedEvent for a land entering
    /// under the controller's control surfaces the ability as pending.</param>
    /// <param name="colorPicker">Optional callback returning the colour to
    /// add at resolution. Consulted on each fire of the landfall trigger;
    /// when null (or returns a non-coloured pip), Green is used.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<ManaColor>? colorPicker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Snake });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Landfall — CR 614 / CR 603.1. "Whenever a land you control
        // enters, you may add one mana of any color." Add one coloured
        // mana directly to the controller's mana pool (CR 106) at
        // resolution. Colour is selected by the optional callback; when
        // absent or returning a non-coloured pip, Green is used.
        // ----------------------------------------------------------------
        var landfallEffect = new Effect(
            $"{CardName}: landfall — add one mana of any color",
            () =>
            {
                var controller = card.Controller ?? owner;
                var chosen = colorPicker?.Invoke() ?? DefaultColor;
                controller.AddManaToPool(BuildOneManaOfColor(chosen));
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { landfallEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }

    /// <summary>
    /// Build a one-pip <see cref="ManaCost"/> of <paramref name="color"/>.
    /// Non-coloured pips (<see cref="ManaColor.Generic"/> /
    /// <see cref="ManaColor.Colorless"/>) are coerced to
    /// <see cref="DefaultColor"/> because "any color" by CR 106.1b means a
    /// WUBRG colour, not generic / colourless.
    /// </summary>
    public static ManaCost BuildOneManaOfColor(ManaColor color)
    {
        var pip = color switch
        {
            ManaColor.White => "W",
            ManaColor.Blue => "U",
            ManaColor.Black => "B",
            ManaColor.Red => "R",
            ManaColor.Green => "G",
            _ => "G",
        };
        return ManaCost.Parse(pip);
    }
}
