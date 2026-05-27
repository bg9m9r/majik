using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hangarback Walker (Magic Origins, {X}{X}).
///
/// Artifact Creature — Construct 0/0. Oracle text (Scryfall, verified):
///   "Hangarback Walker enters with X +1/+1 counters on it.
///    When Hangarback Walker dies, create a 1/1 colorless Thopter
///    artifact creature token with flying for each +1/+1 counter on
///    Hangarback Walker.
///    {1}, {T}: Put a +1/+1 counter on Hangarback Walker."
///
/// Modern Affinity / Hardened Scales / artifact-aggro staple — banks an
/// X-cost ETB body that pays out as a token swarm on death. Pairs with
/// Arcbound Ravager (sac fuel + counter chain), Walking Ballista (other
/// {X}{X} X-counter artifact creature), and Steel Overseer / Hardened
/// Scales for the counter-pump curve.
///
/// ## Implemented (v1)
///
/// - 0/0 <see cref="Creature"/> — Construct, mana cost {X}{X}. Artifact
///   type stamped additively (multi-type, same posture as Walking
///   Ballista / Animation Module's Servo).
///   <see cref="Card.ManaCostValue.HasX"/> reports true.
/// - <b>ETB +1/+1 counters trigger (CR 603.6a / CR 122.1g)</b>: on
///   entering the battlefield, places X +1/+1 counters on Hangarback.
///   X is read from <see cref="Card.PendingCastX"/>, stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> at cast time right
///   after the caster's <c>ChooseXAsync</c>. The stamp is consumed
///   (cleared) so a later non-cast battlefield entry (blink, copy)
///   doesn't reuse it — such an entry leaves Hangarback with zero
///   counters, matching the printed behaviour for a Hangarback that
///   didn't come in via a real X cast (the SBA pass per CR 704.5f
///   immediately puts it in the graveyard as a 0/0). Counter placement
///   routes through <see cref="CountersService.Add"/> when a
///   <see cref="ReplacementBus"/> is supplied so Hardened Scales /
///   Doubling Season rewrite the amount (CR 614 / CR 121.2). Same
///   pattern as <see cref="EndlessOneFactory"/> — see its docstring
///   for the v1 "ETB trigger instead of full 122.1g replacement"
///   rationale (variable-X threading through
///   <see cref="EntersWithCountersReplacement"/>'s
///   <see cref="ZoneMoveIntent"/> is the documented gap).
/// - <b>Dies trigger (CR 603.1 / CR 700.4)</b>: when Hangarback dies,
///   creates N 1/1 colorless Thopter artifact creature tokens with
///   flying where N = the number of +1/+1 counters on Hangarback. The
///   counter total is snapshot-read off the dying card's
///   <see cref="Permanent.Counters"/> bag at trigger-resolution time —
///   the bag is NOT cleared on zone-move (Undying/Modular shape — bag
///   survives the zone move), so the death-side count accurately
///   reflects what Hangarback had when it left the battlefield. Each
///   token is built via <see cref="TokenFactory.CreateOnBattlefield"/>
///   as a 1/1 colourless <see cref="CardSubtype.Thopter"/> creature
///   with the Flying keyword (CR 702.9), then additively stamped
///   <see cref="CardType.Artifact"/> so the resulting token reports
///   Artifact + Creature — Thopter (same multi-type pattern as
///   <see cref="WhirlerVirtuosoFactory"/>'s Thopters and Animation
///   Module's Servo). When a <see cref="ZoneService"/> is supplied
///   each token's ETB publishes <see cref="CardMovedEvent"/> so
///   downstream listeners (Soul Warden / another Mentor of the Meek
///   trigger / Animation Module's +1/+1 chain) fire.
/// - <b>Activated ability — {1}, {T}: Put a +1/+1 counter on
///   Hangarback Walker (CR 605.1)</b>: wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>
///   {1} + <see cref="AdditionalCost.Tap"/> pair. On resolve the
///   counter is placed via <see cref="CountersService.Add"/> so
///   Hardened Scales bumps observe the placement AND the post-commit
///   <see cref="CounterAddedEvent"/> fires (Animation Module chain
///   compatibility).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. ETB + dies triggers
///   attached for shape observability; not registered with any
///   <see cref="TriggerManager"/>; counter placement uses the direct
///   <see cref="CountersService.Add"/> fallthrough (no replacement-bus
///   rewrites, no event publish); tokens enter via the raw zone path.
///   Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, IEventBus?, ZoneService?)"/>
///   — fully wired. Triggers register; counter placement routes through
///   the replacement bus + publishes <see cref="CounterAddedEvent"/>;
///   token ETBs publish <see cref="CardMovedEvent"/> via ZoneService.
///
/// ## Notes
///
/// - <b>Self-trigger of the activated counter→may-pay chain</b>: when
///   Hangarback's {1},{T} ability places a +1/+1 counter on itself,
///   <see cref="CountersService.Add"/> publishes
///   <see cref="CounterAddedEvent"/>; any sibling Animation Module
///   under the same controller sees its own trigger fire (the printed
///   "permanent you control" gate matches). This matches the printed
///   Affinity / Hardened-Scales chain behaviour.
/// </summary>
[CardName("Hangarback Walker")]
public static class HangarbackWalkerFactory
{
    public const string CardName = "Hangarback Walker";
    public const string PrintedManaCost = "{X}{X}";
    public const int Power = 0;
    public const int Toughness = 0;
    public const string ActivatedManaCost = "{1}";
    public const int ThopterPower = 1;
    public const int ThopterToughness = 1;
    public const string ThopterTokenName = "Thopter";

    /// <summary>
    /// Construct Hangarback Walker with no live wiring. ETB + dies
    /// triggers are attached for shape observability; not registered
    /// with any <see cref="TriggerManager"/>; counter placements use
    /// the direct <see cref="CountersService.Add"/> fallthrough (no
    /// replacement-bus rewrites, no event publish). Suitable for shape
    /// / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, eventBus: null, zones: null);

    /// <summary>
    /// Construct Hangarback Walker with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager. When supplied the ETB
    /// counter-placement trigger AND the dies-tokens trigger register
    /// for bus-driven firing (CR 603.2).</param>
    /// <param name="replacements">ReplacementBus. When supplied counter
    /// placements route through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season can rewrite the count
    /// (CR 614).</param>
    /// <param name="eventBus">EventBus. When supplied counter
    /// placements publish <see cref="CounterAddedEvent"/> so
    /// Animation-Module-style "+1/+1 counters were put on …" triggers
    /// can chain.</param>
    /// <param name="zones">ZoneService. When supplied Thopter tokens
    /// ETB publishes <see cref="CardMovedEvent"/> so downstream ETB
    /// listeners (Soul Warden, Mentor of the Meek) fire.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Construct });

        // CR 301.1 / 302.1 — Artifact Creature: stamp the Artifact type
        // additively so HasType-based lookups + colour identity see both
        // types (same posture as Walking Ballista / Arcbound Ravager /
        // Whirler Virtuoso).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB +1/+1 counters trigger — CR 603.6a / CR 122.1g.
        //   "Hangarback Walker enters with X +1/+1 counters on it."
        // v1 folds 122.1g "as it enters with N counters" into the ETB
        // trigger effect: read PendingCastX (stamped by SpellCastFlow
        // right after ChooseXAsync), apply that many +1/+1 counters via
        // CountersService.Add (so Hardened Scales / Doubling Season
        // rewrite the amount), then clear the stamp so re-entries (blink,
        // copy) don't reuse the value. PendingCastX is null for non-cast
        // entries → 0 counters → 0/0 → SBA puts it in the graveyard
        // (CR 704.5f), matching the printed behaviour. Same pattern as
        // EndlessOneFactory's ETB-counter trigger.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enters with X +1/+1 counters (CR 122.1g)",
            () =>
            {
                var x = card.PendingCastX ?? 0;
                if (x > 0)
                {
                    // CR 614 — routes through ReplacementBus so Hardened
                    // Scales bumps + Doubling Season doubles apply.
                    CountersService.Add(card, CounterType.PlusOnePlusOne, x, replacements, eventBus);
                }
                card.ClearPendingCastX();
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.1 / CR 700.4.
        //   "When Hangarback Walker dies, create a 1/1 colorless Thopter
        //    artifact creature token with flying for each +1/+1 counter
        //    on Hangarback Walker."
        //
        // Snapshot the +1/+1 counter total off the dying card's bag at
        // resolution time (Undying-shape — bag survives the zone move,
        // mirrors ModularFactory.Build's dies-trigger read). For each
        // counter create one Thopter token (1/1 colourless artifact
        // creature with flying). The dying source is not the token, so
        // even a 0-counter Hangarback (printed 0/0, never pumped, dies
        // immediately via SBA) creates zero tokens — matching the
        // printed "for each +1/+1 counter" wording (no counters → no
        // tokens).
        //
        // activeZones include Battlefield + Graveyard so the trigger is
        // still active when the dying source has moved into the
        // graveyard at resolve time (CR 603.10c — leaves-the-battlefield
        // / dies triggers see the card in its new zone but the ability
        // remains observable).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName}: create N Thopter tokens (N = +1/+1 counters at dies)",
            () =>
            {
                var n = card.Counters.Count(CounterType.PlusOnePlusOne);
                if (n <= 0) return;

                var controller = card.Controller ?? owner;
                for (var i = 0; i < n; i++)
                {
                    var spec = new TokenFactory.TokenSpec(
                        Name: ThopterTokenName,
                        Power: ThopterPower,
                        Toughness: ThopterToughness,
                        Subtypes: new[] { CardSubtype.Thopter },
                        Keywords: new[] { "Flying" },
                        Colors: Array.Empty<ManaColor>());

                    var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);

                    // CR 111.1 — Thopter tokens are artifact creatures.
                    // Stamp Artifact additively (TokenFactory's shell is
                    // Creature-only; same multi-type stamp as
                    // WhirlerVirtuosoFactory's Thopters + Animation
                    // Module's Servos).
                    token.AddCardType(CardType.Artifact);
                }
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // CR 603.10c — dies trigger remains observable from the
            // graveyard at resolve time (same posture as Modular's
            // death trigger in ModularFactory.Build).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 605.1.
        //   "{1}, {T}: Put a +1/+1 counter on Hangarback Walker."
        //
        // Cost: ManaCostCost("{1}") + AdditionalCost.Tap(card). On
        // resolve the counter is placed via CountersService.Add so
        // Hardened Scales bumps + post-commit CounterAddedEvent both
        // observe the placement (Animation Module chain compatibility).
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on self",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements, eventBus);
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }
}
