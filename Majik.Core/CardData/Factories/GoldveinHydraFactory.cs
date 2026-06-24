using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goldvein Hydra (Outlaws of Thunder Junction, {X}{G}).
///
/// Creature — Hydra 0/0. Oracle text (Scryfall, verified):
///   "Vigilance, trample, haste
///    This creature enters with X +1/+1 counters on it.
///    When this creature dies, create a number of tapped Treasure tokens
///    equal to its power."
///
/// ## Shape source
/// Card identity (name, {X}{G}, 0/0, Creature — Hydra, Vigilance + Trample +
/// Haste) is loaded from <c>Majik.Core/CardData/Cards/goldvein-hydra.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/> (the <c>keywords</c> array
/// carries the three evergreen keywords — Vigilance CR 702.20, Trample
/// CR 702.19, Haste CR 702.10). The dies-trigger is attached in code below.
///
/// ## Implemented (v1)
/// - 0/0 Creature — Hydra at {X}{G} with Vigilance + Trample + Haste (JSON).
///   <see cref="Card.ManaCostValue.HasX"/> reports true.
/// - <b>"Enters with X +1/+1 counters on it" (CR 614.1d / CR 202.3b)</b> is
///   NOT wired by this factory. Same posture as
///   <see cref="HangarbackWalkerFactory"/> / <see cref="EndlessOneFactory"/>:
///   the generic <see cref="EntersWithCountersBinder"/> registers the
///   variable-X <see cref="EntersWithCountersReplacement"/> on the production
///   deck-build path (it reads the chosen X off <see cref="Card.PendingCastX"/>,
///   stamped by <see cref="Majik.Core.Game.SpellCastFlow"/> after the caster's
///   <c>ChooseXAsync</c>, and stamps the ETB intent so the Hydra enters WITH
///   the counters — Hardened Scales / Doubling Season compose on that channel,
///   CR 614). The factory deliberately does NOT
///   <c>MarkSelfManagesEntersWithCounters()</c> — setting that flag suppresses
///   the binder, the one mechanism the prod Approach-B route runs, yielding
///   ZERO counters in real play (the bug Hangarback / Walking Ballista document).
/// - <b>Dies trigger (CR 603.6d / CR 700.4 / CR 603.10)</b>: when the Hydra
///   dies, create a number of TAPPED Treasure tokens equal to its power. "its
///   power" is read as last-known information (CR 603.10 / CR 608.2g): the
///   <see cref="Creature"/> instance retains its counters + its
///   <see cref="Card.ActiveEffects"/> reference after leaving the battlefield,
///   so <see cref="Creature.Power"/> at resolution reflects the power the Hydra
///   had immediately before it died (the X +1/+1 counters plus any other pump).
///   Same LKI-power read as <see cref="HeartfireHeroFactory"/>'s dies trigger.
///   Each Treasure is built via <see cref="TokenFactory.CreateTreasure"/>
///   (colourless artifact — Treasure, CR 111.10) and then tapped (CR 701.21a)
///   so it enters tapped per the printed wording. The dies trigger remains
///   active in the graveyard so the Hydra's OWN death resolves (CR 603.10c).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Dies trigger attached for
///   shape observability; not registered with any <see cref="TriggerManager"/>;
///   tokens enter via the raw zone path; <see cref="Card.ActiveEffects"/> is
///   unwired so <see cref="Creature.Power"/> falls back to base P/T (0) unless
///   counters are reflected by a bound effects service. Suitable for shape /
///   dispatcher tests.
/// - <see cref="Create(Player, TriggerManager?, IEventBus?, ZoneService?, ContinuousEffectsService?)"/>
///   — fully wired. Dies trigger registers; Treasure ETBs publish
///   <see cref="CardMovedEvent"/> via ZoneService; the effects service is bound
///   so the binder-placed +1/+1 counters raise <see cref="Creature.Power"/>
///   (CR 122 / 613), making the death-side Treasure count accurate.
/// </summary>
[CardName("Goldvein Hydra")]
public static class GoldveinHydraFactory
{
    public const string CardName = "Goldvein Hydra";
    public const string Slug = "goldvein-hydra";

    /// <summary>
    /// Construct Goldvein Hydra with no live wiring. The dies trigger is
    /// attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>; Treasure tokens enter via the raw zone
    /// path. Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, zones: null, effects: null);

    /// <summary>
    /// Construct Goldvein Hydra with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager. When supplied the dies-Treasures
    /// trigger registers for bus-driven firing (CR 603.2).</param>
    /// <param name="eventBus">EventBus (reserved; carried for parity with the
    /// other wired factories — Treasure ETB events flow through
    /// <paramref name="zones"/>).</param>
    /// <param name="zones">ZoneService. When supplied Treasure tokens ETB
    /// publishes <see cref="CardMovedEvent"/> so downstream ETB listeners fire.</param>
    /// <param name="effects">ContinuousEffectsService bound onto the card so the
    /// binder-placed X +1/+1 counters are reflected in
    /// <see cref="Creature.Power"/> via the layer compute (CR 122 / 613). When
    /// null, <see cref="Creature.GetPower"/> falls back to base P/T (0) unless
    /// the card already carries a bound effects service.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus,
        ZoneService? zones,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature — Hydra,
        // {X}{G}, 0/0, Vigilance + Trample + Haste). The JSON carries no
        // abilities — the dies trigger is layered on below; the ETB-X counters
        // are owned by the EntersWithCountersBinder (see class docstring).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6d / CR 700.4 / CR 603.10.
        //   "When this creature dies, create a number of tapped Treasure
        //    tokens equal to its power."
        //
        // "its power" is last-known information (CR 603.10 / CR 608.2g): the
        // Creature instance retains its counters + ActiveEffects reference
        // after the zone move, so Creature.Power here is the power the Hydra
        // had immediately before it left the battlefield (mirrors
        // HeartfireHeroFactory's LKI-power dies read). For each point of power
        // create one TAPPED Treasure token (CR 111.10 colourless artifact;
        // tapped per CR 701.21a). Zero / negative power → zero tokens.
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName} dies: create tapped Treasure tokens equal to its power",
            () =>
            {
                var power = card.Power;
                if (power <= 0) return;

                var controller = card.Controller ?? owner;
                for (var i = 0; i < power; i++)
                {
                    var treasure = TokenFactory.CreateTreasure(controller, zones);
                    // "tapped Treasure tokens" — enters tapped (CR 701.21a).
                    treasure.Tap();
                }
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // CR 603.10c — a self-naming dies trigger remains observable from
            // the graveyard so the Hydra's OWN death still resolves the
            // Treasures (same posture as HangarbackWalkerFactory's dies trigger).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
