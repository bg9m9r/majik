using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crumbling Vestige (Oath of the Gatewatch).
///
/// Land. Oracle text (Scryfall, verified):
///   "This land enters tapped.
///    When this land enters, add one mana of any color.
///    {T}: Add {C}. ({C} represents colorless mana.)"
///
/// Composed entirely from existing engine primitives — same posture as the
/// cited analogues:
/// <list type="bullet">
/// <item><see cref="WastelandFactory"/> — the repeatable
///   <b>{T}: Add {C}</b> <see cref="ManaAbility"/> (CR 605.1, no stack).
///   {C} (colourless, CR 107.4c) has no dedicated <see cref="ManaCost"/>
///   bucket today; <c>ManaCost.Parse("C")</c> folds it into Generic, exactly
///   as Wasteland / Urza's Saga do.</item>
/// <item><see cref="SavaiTriomeFactory"/> — the unconditional
///   <b>enters-tapped</b> replacement (CR 614.1c) via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. The shape-only path (null bus) skips the
///   registration; the production load path also matches the plain
///   "This land enters tapped." clause through
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/>.</item>
/// <item><see cref="LotusCobraFactory"/> — the one-shot
///   <b>"add one mana of any color"</b> (CR 106). Crumbling Vestige fires it
///   off an enters-the-battlefield-<i>self</i> trigger
///   (<see cref="Triggers.OnEnterBattlefieldSelf"/>, CR 603.6e) rather than
///   landfall. At resolution the controller's mana pool receives one
///   coloured mana; the colour is chosen by the optional
///   <paramref name="colorPicker"/> callback, defaulting to
///   <see cref="LotusCobraFactory.DefaultColor"/> (Green) when absent — same
///   v1 deferral as Lotus Cobra (no <c>ChooseManaColorAsync</c> agent hook
///   yet).</item>
/// </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for the ETB colour</b>: reuses Lotus Cobra's
///   <c>colorPicker</c> deferral — the dispatcher path defaults to Green.
/// - <b>"Add one mana of any color" is not a true mana ability</b>: it is a
///   triggered ability that uses the stack (CR 605.1b — it does something
///   other than producing mana? no — but it is not an <i>activated</i> mana
///   ability, so it goes on the stack as a normal triggered ability, CR
///   603.3). Modelled exactly so.
/// </summary>
[CardName("Crumbling Vestige")]
public static class CrumblingVestigeFactory
{
    public const string CardName = "Crumbling Vestige";

    /// <summary>
    /// Construct Crumbling Vestige with no live bus wiring (shape
    /// observability only — enters-tapped is omitted and the ETB trigger is
    /// attached but not registered). The ETB effect still adds the default
    /// Green mana when its effect is executed directly.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, colorPicker: null);

    /// <summary>
    /// Construct Crumbling Vestige with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB "add one mana of any
    /// color" trigger is registered so the bus surfaces it as pending when
    /// the land enters (CR 603.6e).</param>
    /// <param name="replacements">When supplied, the unconditional
    /// enters-tapped restriction (CR 614.1c) is registered against it.</param>
    /// <param name="colorPicker">Optional callback returning the colour to
    /// add at ETB resolution. Consulted on each fire; when null (or a
    /// non-coloured pip) Green is used — same posture as Lotus Cobra.</param>
    public static Land Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        Func<ManaColor>? colorPicker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." — CR 614.1c. Unconditional. Shape-only
        // path (no ReplacementBus) skips registration; same posture as
        // SavaiTriomeFactory. The production load path also matches the
        // clause via EntersTappedBinder off the oracle text.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // "When this land enters, add one mana of any color." — CR 603.6e
        // (enters-the-battlefield trigger) + CR 106 (add coloured mana). At
        // resolution the controller's mana pool receives one coloured mana;
        // the colour is chosen by the optional callback, defaulting to Green
        // (Lotus Cobra's deferral until the agent colour-prompt exists).
        // ----------------------------------------------------------------
        var etbManaEffect = new Effect(
            $"{CardName}: when this enters, add one mana of any color",
            () =>
            {
                var controller = land.Controller ?? owner;
                var chosen = colorPicker?.Invoke() ?? LotusCobraFactory.DefaultColor;
                controller.AddManaToPool(LotusCobraFactory.BuildOneManaOfColor(chosen));
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbManaEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // "{T}: Add {C}." — CR 605.1 mana ability (no stack). One
        // ManaAbility producing colourless mana; {C} has no dedicated bucket
        // so it parses into Generic, exactly as Wasteland / Urza's Saga.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        return land;
    }
}
