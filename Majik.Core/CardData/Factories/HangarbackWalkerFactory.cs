using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hangarback Walker (Magic Origins, {X}{X}).
///
/// Artifact Creature — Construct 0/0. Oracle text (Scryfall, verbatim):
///   "Hangarback Walker enters with X +1/+1 counters on it.
///    When this creature dies, create a 1/1 colorless Thopter artifact
///    creature token with flying for each +1/+1 counter on Hangarback Walker.
///    {1}, {T}: Put a +1/+1 counter on Hangarback Walker."
///
/// ## Implemented (v1)
///
/// - Shape: 0/0 Artifact Creature — Construct, printed cost <c>{X}{X}</c>.
///   <see cref="CardType.Artifact"/> stamped additively on the Creature shell
///   (CR 301.1 / 302.1 — same multi-type posture used by Walking Ballista /
///   Esika's Chariot / Arcbound Ravager).
/// - <b>ETB with X +1/+1 counters (CR 122.1g + CR 614)</b>: read from
///   <see cref="Card.PendingCastX"/> (stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> right after the caster's
///   <c>ChooseXAsync</c>). Counter placement routes through
///   <see cref="CountersService.Add"/> when a <see cref="ReplacementBus"/> is
///   supplied so Hardened Scales / Doubling Season bumps apply. PendingCastX
///   is consumed (cleared) on use — re-entries (blink/copy) start with zero
///   counters, matching Endless One's posture. The mana cost is <c>{X}{X}</c>
///   so two times the chosen X is paid in mana but only X counters land
///   (per the printed text — the cost's doubled-X is unrelated to the ETB
///   amount).
/// - <b>Death trigger — N Thopter tokens (CR 603.6a / 702.43b-shape)</b>:
///   on Battlefield → Graveyard, snapshot the +1/+1 counter count on the
///   dying permanent and create that many 1/1 colorless Thopter artifact
///   creature tokens with Flying. Tokens are colourless (CR 111.4 — printed
///   "1/1 colorless Thopter artifact creature token with flying"); the
///   "artifact creature" type pair is encoded by stamping
///   <see cref="CardType.Artifact"/> additively onto each Thopter shell.
///   Counter total survives the zone-move (Undying-shape — the bag isn't
///   cleared on transition; see Modular for the established pattern).
/// - <b>Activated ability — <c>{1}, {T}: +1/+1 counter</c></b>: pure
///   <see cref="ActivatedAbility"/> with two costs (<see cref="ManaCostCost"/>
///   "{1}" + <see cref="AdditionalCost.Tap"/> self). On resolve, route the
///   counter add through <see cref="CountersService.Add"/> so Hardened Scales
///   bumps the activated bump too.
///
/// ## Deferred (v1 gaps)
///
/// - Strict CR 122.1g "as it enters" timing — counter placement runs from
///   the ETB trigger rather than the entry replacement, same posture as
///   <see cref="EndlessOneFactory"/> (the <c>EntersWithCountersReplacement</c>
///   primitive doesn't thread <c>ChosenSpellParams.X</c> through
///   <see cref="ZoneMoveIntent"/> yet — pinned to the same X-through-intent
///   work Walking Ballista documents).
/// - Activated ability has no agent gating — bots/tests call
///   <see cref="ActivatedAbility.Effects"/> directly. Targeting / mana-payment
///   flow is owned by the cast-flow stack, not this factory.
/// </summary>
[CardName("Hangarback Walker")]
public static class HangarbackWalkerFactory
{
    public const string CardName = "Hangarback Walker";
    public const string PrintedManaCost = "{X}{X}";
    public const int Power = 0;
    public const int Toughness = 0;

    /// <summary>
    /// Construct Hangarback Walker with no live wiring. ETB + death trigger
    /// + activated ability are attached for shape observability; not
    /// registered with any <see cref="TriggerManager"/>; counter placement
    /// uses the direct <see cref="CountersService.Add"/> fallthrough (no
    /// replacement-bus rewrites). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, zones: null);

    /// <summary>
    /// Construct Hangarback Walker with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager. When supplied, both ETB and
    /// death triggers register for bus-driven firing (CR 603.2).</param>
    /// <param name="replacements">Replacement bus. When supplied, counter
    /// placement (ETB-X and the activated bump) routes through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season rewrites apply (CR 614).</param>
    /// <param name="zones">Optional <see cref="ZoneService"/>. When supplied,
    /// the death-trigger's spawned Thopter tokens route through
    /// <see cref="TokenFactory.CreateOnBattlefield"/> using the service so
    /// each token publishes <see cref="Events.CardMovedEvent"/> on
    /// battlefield entry (downstream ETB listeners — Soul Warden, etc. —
    /// fire).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Construct });

        // CR 301.1 / 302.1 — Artifact Creature multi-type (mirrors Walking
        // Ballista / Arcbound Ravager).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB +X +1/+1 counters — CR 122.1g.
        //   "Hangarback Walker enters with X +1/+1 counters on it."
        // X = PendingCastX (stamped by SpellCastFlow after ChooseXAsync).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enters with X +1/+1 counters (CR 122.1g)",
            () =>
            {
                var x = card.PendingCastX ?? 0;
                if (x > 0)
                {
                    CountersService.Add(card, CounterType.PlusOnePlusOne, x, replacements);
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
        // Death trigger — CR 603.6a.
        //   "When this creature dies, create a 1/1 colorless Thopter
        //    artifact creature token with flying for each +1/+1 counter
        //    on Hangarback Walker."
        // Counter total is snapshot off the dying permanent at trigger
        // resolution time (Undying-shape — bag survives the zone move).
        // ----------------------------------------------------------------
        var deathEffect = new Effect(
            $"{CardName}: create N 1/1 Thopter tokens with flying (N = +1/+1 counters at death)",
            () =>
            {
                var n = card.Counters.Count(CounterType.PlusOnePlusOne);
                if (n <= 0) return;

                var controller = card.Controller ?? owner;
                var spec = new TokenFactory.TokenSpec(
                    Name: "Thopter",
                    Power: 1,
                    Toughness: 1,
                    Subtypes: new[] { CardSubtype.Thopter },
                    Keywords: new[] { "Flying" },
                    // CR 111.4 — printed colour is "colorless".
                    Colors: Array.Empty<ManaColor>());

                for (var i = 0; i < n; i++)
                {
                    var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);
                    // CR 301.1 / 302.1 — Thopter tokens are Artifact
                    // Creatures (printed "artifact creature token"). The
                    // Creature shell only stamps CardType.Creature; add
                    // Artifact additively to match the printed type line.
                    token.AddCardType(CardType.Artifact);
                }
            });

        var deathTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { deathEffect },
            // ActiveZones = Battlefield + Graveyard (Wurmcoil/Matter Reshaper
            // posture) so the trigger still matches after ZoneService has
            // stamped Zone = Graveyard before publishing the CardMovedEvent.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(deathTrigger);
        triggers?.RegisterTriggeredAbility(deathTrigger);

        // ----------------------------------------------------------------
        // Activated ability — "{1}, {T}: Put a +1/+1 counter on this."
        // Same shape as Walking Ballista's {4}-bump (CR 602.1 — pay
        // costs, then put counter on resolve). Route the counter add
        // through CountersService so Hardened Scales bumps apply.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: +1/+1 counter (activated)",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ManaCost.Parse("1")),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }
}
