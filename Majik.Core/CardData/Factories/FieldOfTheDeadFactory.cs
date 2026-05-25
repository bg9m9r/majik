using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Field of the Dead (Core Set 2020).
///
/// Land. Oracle text:
///   "Field of the Dead enters tapped.
///    {T}: Add {C}.
///    Whenever this land or another land enters under your control, if you
///    have seven or more lands with different names, create a 2/2 black
///    Zombie creature token."
///
/// ## Implemented (v1)
/// - <b>Non-basic Land</b>, no printed subtype.
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional. Wired
///   via <see cref="EntersTappedReplacement"/> on a supplied
///   <see cref="ReplacementBus"/>. Shape-only path (no
///   <see cref="ReplacementBus"/>) skips registration — same posture
///   every always-tapped factory takes (Bojuka Bog / Sunscorched Desert).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>Land-ETB-under-controller triggered ability (CR 603.1 / 603.6a)</b>
///   over <see cref="CardMovedEvent"/>: fires when ANY land enters the
///   battlefield under Field of the Dead's controller — INCLUDING Field
///   of the Dead itself (the printed text is "this land OR another
///   land"). Routed through <see cref="Triggers.OnLandEntersUnderControl"/>.
///   Intervening-if (CR 603.4): controller's battlefield has ≥7 lands with
///   distinct printed <see cref="ICard.Name"/>s. On resolve, creates a
///   2/2 black Zombie creature token via
///   <see cref="TokenFactory.CreateOnBattlefield"/> with a supplied
///   <see cref="ZoneService"/> when wired (so the token's own ETB
///   <see cref="CardMovedEvent"/> fires for downstream listeners — soul
///   warden / amalia / and Field of the Dead's own counter — but
///   crucially the Zombie is NOT a Land, so it can't recursively re-fire
///   Field of the Dead's trigger).
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape — the land-ETB trigger is
/// attached for shape but not registered with a
/// <see cref="TriggerManager"/>; enters-tapped is not registered against
/// any <see cref="ReplacementBus"/>; token creation has no
/// <see cref="ZoneService"/> path so the Zombie lands on the battlefield
/// without firing <see cref="CardMovedEvent"/>. Use the
/// <see cref="Create(Player, ReplacementBus?, TriggerManager?, ZoneService?)"/>
/// overload to wire runtime services for end-to-end behaviour.
///
/// ## Deferred (v1 gaps)
/// - <b>Token controller dies-with-controller</b>: tokens follow standard
///   SBA (CR 704.5d) — no Field of the Dead-specific cleanup needed.
/// - <b>Distinct-name evaluation</b>: uses the printed <see cref="ICard.Name"/>
///   live at trigger-time. Copy effects / face-down lands / name-change
///   effects (none currently in Modern's land pool) are not specially
///   handled — same posture as Valakut's Mountain-count predicate.
/// </summary>
[CardName("Field of the Dead")]
public static class FieldOfTheDeadFactory
{
    public const string CardName = "Field of the Dead";

    /// <summary>Distinct-name threshold for the trigger (CR rules text).</summary>
    public const int DistinctNameThreshold = 7;

    /// <summary>
    /// Construct Field of the Dead with no live wiring. The land-ETB trigger
    /// is attached for shape but is not registered with a
    /// <see cref="TriggerManager"/>; the enters-tapped replacement is
    /// omitted (shape-only).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null, zones: null);

    /// <summary>
    /// Construct Field of the Dead. When <paramref name="replacements"/> is
    /// supplied the enters-tapped restriction is registered (CR 614.1c).
    /// When <paramref name="triggers"/> is supplied the land-ETB trigger
    /// is registered with the bus so a <see cref="CardMovedEvent"/>
    /// matching the predicate auto-queues the ability. When
    /// <paramref name="zones"/> is supplied, the Zombie token is moved
    /// to the battlefield via <see cref="ZoneService"/> so downstream
    /// ETB listeners (Soul Warden etc.) fire.
    /// </summary>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Field of the Dead is just "Land" — no printed subtype, no Basic
        // supertype. Its own ETB DOES satisfy the trigger predicate per
        // printed text ("this land or another land").
        var card = new Land(CardName);
        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Enters-tapped replacement — CR 614.1c.
        //   "Field of the Dead enters tapped."
        // Unconditional; no gate.
        // --------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(card));
        }

        // --------------------------------------------------------------
        // {T}: Add {C} — vanilla colourless mana ability (CR 605.1).
        // --------------------------------------------------------------
        card.AddAbility(new ManaAbility(card, owner, ManaCost.Parse("C")));

        // --------------------------------------------------------------
        // Triggered ability (CR 603.1 / 603.6a) —
        //   "Whenever this land or another land enters under your
        //    control, if you have seven or more lands with different
        //    names, create a 2/2 black Zombie creature token."
        // CR 603.4 — intervening-if checked at trigger-detection time.
        // Token spawn body reads `Controller` live so a control-change
        // (rare on lands) routes correctly per CR 109.5.
        // --------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var spawnEffect = new Effect(
            $"{CardName}: create 2/2 black Zombie token",
            () =>
            {
                if (trigger == null) return;

                var controller = card.Controller ?? owner;

                // Defensive re-check — CR 603.4 second-pass: at
                // resolution, if the intervening-if is false the
                // ability is removed from the stack without effect.
                if (CountDistinctlyNamedLands(controller) < DistinctNameThreshold)
                {
                    return;
                }

                var spec = new TokenFactory.TokenSpec(
                    Name: "Zombie",
                    Power: 2,
                    Toughness: 2,
                    Subtypes: new[] { CardSubtype.Zombie },
                    Keywords: null,
                    Colors: new[] { ManaColor.Black });

                TokenFactory.CreateOnBattlefield(spec, controller, zones);
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { spawnEffect },
            interveningIf: () =>
                CountDistinctlyNamedLands(card.Controller ?? owner)
                    >= DistinctNameThreshold,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Count distinctly-named Lands on <paramref name="controller"/>'s
    /// battlefield. CR 205.1 — "name" is the printed name on the card
    /// (we read <see cref="ICard.Name"/> live; the engine has no Layer 3
    /// name-change effect surface today).
    /// </summary>
    public static int CountDistinctlyNamedLands(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .Select(c => c.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }
}
