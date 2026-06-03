using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Coalition Relic (Future Sight, {3}).
///
/// Artifact. Oracle text (Scryfall, verified 2026-06-02):
///   "{T}: Add one mana of any color.
///    {T}: Put a charge counter on this artifact.
///    At the beginning of your first main phase, remove all charge counters
///    from this artifact. Add one mana of any color for each charge counter
///    removed this way."
///
/// <para>
/// This is the charge-counter "mana battery" rock: the controller spends
/// idle turns banking charge counters with the second {T} ability, then
/// cashes the whole stack into a burst of single-color mana at the start of
/// their own main phase. No new subsystem — it composes three primitives
/// that already exist:
/// </para>
///
/// <list type="number">
/// <item>
/// <description>
/// <b>"{T}: Add one mana of any color." (CR 605.1a)</b> — five
/// <see cref="ManaAbility"/> instances (one per WUBRG), the same modal-color
/// shape <see cref="ArcaneSignetFactory"/> / <see cref="SphereOfTheSunsFactory"/>
/// use; the activator picks a color by picking the matching ability slot, so
/// no separate color prompt is needed (CR 605.1 — mana abilities don't use the
/// stack). Each is gated on the relic being on the battlefield and untapped
/// (the printed {T} cost). Tap is the default cost baked into
/// <see cref="ManaAbility"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>"{T}: Put a charge counter on this artifact."</b> — a non-mana
/// <see cref="ActivatedAbility"/> (it has a visible effect other than adding
/// mana, so it is NOT a mana ability and uses the stack, CR 605.1a) whose only
/// cost is <see cref="AdditionalCost.Tap"/>; its effect places one
/// <see cref="CounterType.Charge"/> counter via
/// <see cref="CounterCollection.Add"/> (CR 122).
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>"At the beginning of your first main phase, remove all charge counters
/// … Add one mana of any color for each …"</b> — a
/// <see cref="TriggeredAbility"/> firing on the controller's precombat main
/// step (<see cref="Triggers.OnStepBegin"/> with
/// <see cref="Majik.Core.StateMachine.PhaseStateType.PreCombatMain"/>; the
/// "first" qualifier degrades to the precombat main, the standard posture
/// <see cref="CabalTherapistFactory"/> takes — in a 1v1 game with no
/// additional-main-phase effects the precombat main IS the first main phase).
/// The effect reads the charge-counter count, removes them all (CR 122),
/// then adds that many mana of a single chosen color to the controller's pool
/// (CR 106.6 — "mana of any color" lets the controller choose the color).
/// Because a live color prompt is deferred engine-wide (same posture as
/// <see cref="ColdsteelHeartFactory"/> / Painter's-style choices), the color
/// is resolved through the supplied <paramref name="colorSelector"/>;
/// callers / tests pass the already-chosen color, defaulting to green.
/// </description>
/// </item>
/// </list>
/// </summary>
[CardName("Coalition Relic")]
public static class CoalitionRelicFactory
{
    public const string CardName = "Coalition Relic";
    public const string PrintedManaCost = "{3}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("coalition-relic");

    private static readonly string[] Wubrg = { "W", "U", "B", "R", "G" };

    /// <summary>
    /// Construct Coalition Relic with no live trigger-manager wiring. The
    /// "first main phase" charge-cashing trigger is attached for shape
    /// observability; the five WUBRG mana abilities and the {T}: put-a-charge
    /// activated ability are attached. The trigger's chosen color defaults to
    /// green. Suitable for shape / <see cref="NamedCardFactory"/> dispatch
    /// tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null, colorSelector: null);

    /// <summary>
    /// Construct a fully-wired Coalition Relic.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the first-main-phase
    /// "remove all charge counters / add that much mana" trigger is registered
    /// so a PreCombatMain <see cref="Majik.Core.Events.StepStartedEvent"/> for
    /// the controller queues it.</param>
    /// <param name="colorSelector">Resolves the single color the cashed-out
    /// mana is added as (CR 106.6). When <c>null</c> the choice defaults to
    /// green. Must return one of W/U/B/R/G.</param>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        System.Func<Player, ManaColor>? colorSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var relic = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color. (CR 605.1a — mana ability; doesn't
        // use the stack.) Five ManaAbility instances (one per WUBRG); the
        // activator selects a color by picking the matching slot. {T} is the
        // default ManaAbility cost — gated on the relic being on the
        // battlefield and untapped.
        // ----------------------------------------------------------------
        foreach (var color in Wubrg)
        {
            relic.AddAbility(new ManaAbility(
                source: relic,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => relic.Zone == ZoneType.Battlefield
                                        && !relic.IsTapped));
        }

        // ----------------------------------------------------------------
        // {T}: Put a charge counter on this artifact. NOT a mana ability —
        // its visible effect is placing a counter, so it uses the stack
        // (CR 605.1a). Cost is the bare {T}; the effect adds one charge
        // counter (CR 122).
        // ----------------------------------------------------------------
        var putCharge = new Effect(
            $"{CardName}: put a charge counter",
            () =>
            {
                if (relic.Zone != ZoneType.Battlefield) return;
                relic.Counters.Add(CounterType.Charge, 1);
            });

        relic.AddAbility(new ActivatedAbility(
            source: relic,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(relic) },
            effects: new IEffect[] { putCharge }));

        // ----------------------------------------------------------------
        // At the beginning of your first main phase, remove all charge
        // counters from this artifact. Add one mana of any color for each
        // charge counter removed this way. (CR 603.1 — triggered ability;
        // CR 122 — remove counters; CR 106.6 — color of the controller's
        // choice.)
        // ----------------------------------------------------------------
        var cashEffect = new Effect(
            $"{CardName}: remove all charge counters; add that much mana of a chosen color",
            () =>
            {
                if (relic.Zone != ZoneType.Battlefield) return;
                var controller = relic.Controller ?? owner;

                var removed = relic.Counters.Count(CounterType.Charge);
                if (removed <= 0) return; // nothing banked → no mana (CR 106.6).

                relic.Counters.Remove(CounterType.Charge, removed);

                var color = (colorSelector?.Invoke(controller)) ?? ManaColor.Green;
                controller.AddManaToPool(ManaForColor(color, removed));
            });

        var cashTrigger = new TriggeredAbility(
            source: relic,
            controller: owner,
            condition: Triggers.OnStepBegin(
                owner, Majik.Core.StateMachine.PhaseStateType.PreCombatMain),
            effects: new IEffect[] { cashEffect },
            activeZones: new[] { ZoneType.Battlefield });

        relic.AddAbility(cashTrigger);
        triggers?.RegisterTriggeredAbility(cashTrigger);

        return relic;
    }

    /// <summary>
    /// CR 106.6 — build <paramref name="count"/> pips of a single chosen
    /// color. Throws for a non-W/U/B/R/G choice (the cashed mana must be a
    /// real color, CR 105.1).
    /// </summary>
    private static ManaCost ManaForColor(ManaColor color, int count)
    {
        var pip = color switch
        {
            ManaColor.White => "W",
            ManaColor.Blue => "U",
            ManaColor.Black => "B",
            ManaColor.Red => "R",
            ManaColor.Green => "G",
            _ => throw new ArgumentOutOfRangeException(
                nameof(color), color,
                "Coalition Relic's cashed mana must be one of W/U/B/R/G (CR 105.1)."),
        };

        return ManaCost.Parse(new string(pip[0], count));
    }
}
