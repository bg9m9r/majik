using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rampaging Ferocidon (Ixalan, {2}{R}).
///
/// Creature — Dinosaur 3/3. Oracle text (verified against Scryfall):
///   "Menace
///    Players can't gain life.
///    Whenever another creature enters, this creature deals 1 damage to
///    that creature's controller."
///
/// The base shape (name, Creature, Dinosaur subtype, {2}{R}, 3/3) is
/// materialised from the embedded JSON definition
/// (<c>rampaging-ferocidon.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/> (same
/// posture as <see cref="DanithaCapashenParagonFactory"/>). The three printed
/// abilities are layered on here:
///
/// ## Implemented (v1)
///
/// - <b>Menace (CR 702.111)</b> — a <see cref="KeywordAbility"/> marker so
///   <c>ICard.Abilities</c> reflects the printed line and the combat
///   blocker-count check in <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/>
///   (which reads off the keyword marker) matches.
///
/// - <b>"Players can't gain life" static (CR 119.6 / CR 614.1)</b> — when a
///   <see cref="ReplacementBus"/> is supplied, register a
///   <see cref="LifeGainIntent"/> replacement that rewrites every gain to a
///   zero-amount intent. The intent dispatcher in <see cref="Player.GainLife"/>
///   passes the request through the player's attached bus before mutating the
///   life total. Identical wiring to <see cref="SulfuricVortexFactory"/>'s
///   "players can't gain life" static. Without a bus the static silently
///   no-ops (matches the single-arg dispatcher overload).
///
/// - <b>Another-creature-ETB ping (CR 603.6e)</b>: any creature OTHER than
///   this permanent entering the battlefield (under ANY controller) triggers;
///   on resolution Rampaging Ferocidon deals 1 damage to <i>that creature's
///   controller</i>. The entering creature's controller is captured by the
///   trigger condition at fire time (a single capture field, mirroring
///   <see cref="SulfuricVortexFactory"/>'s upkeep-player capture; the
///   resolving effect reads it back) and the damage routes
///   through <see cref="Fx.DealDamageAny(object, int, Creature?)"/> with this
///   Ferocidon as the source (so source-keyed riders such as lifelink — were
///   it ever granted one — would observe it). Damage to a player lands as life
///   loss (CR 119.3). The trigger is active only while Ferocidon is on the
///   battlefield (the engine default).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The Menace marker + ETB ping
///   trigger are attached for shape observability; no
///   <see cref="TriggerManager"/> / <see cref="ReplacementBus"/> registration.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> — fully
///   wired. <paramref name="triggers"/> picks the ETB ping off the bus;
///   <paramref name="replacements"/> registers the life-gain blocker.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Rampaging Ferocidon")]
public static class RampagingFerocidonFactory
{
    public const string CardName = "Rampaging Ferocidon";
    public const string Slug = "rampaging-ferocidon";
    public const string Menace = "Menace";
    public const int PingDamage = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Rampaging Ferocidon with no live runtime services. The Menace
    /// marker + ETB ping trigger are attached for shape observability; nothing
    /// is registered on a trigger manager or replacement bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Rampaging Ferocidon with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager — when supplied, the
    /// another-creature-ETB ping registers so the bus drives it
    /// automatically.</param>
    /// <param name="replacements">Replacement bus — when supplied, the
    /// "players can't gain life" static registers as a
    /// <see cref="LifeGainIntent"/> replacement (CR 119.6 / 614.1) that
    /// rewrites every gain to zero. Without a bus the static silently
    /// no-ops.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Dinosaur, {2}{R}, 3/3). No abilities in the JSON — Menace marker +
        // life-gain static + ETB ping layered on below.
        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 702.111 — Menace marker, read by the combat declaration rules.
        card.AddAbility(new KeywordAbility(Menace, card, owner));

        // ----------------------------------------------------------------
        // Another-creature-ETB ping — CR 603.6e.
        //   "Whenever another creature enters, this creature deals 1 damage
        //    to that creature's controller."
        // Fires on CardMovedEvent → Battlefield, moved card is a Creature
        // OTHER than this Ferocidon, under ANY player's control. The entering
        // creature's controller is captured by the condition (single capture
        // field, mirroring SulfuricVortexFactory's upkeep-player capture) and
        // the resolving effect deals 1 damage to it via Fx.DealDamageAny
        // (this Ferocidon as the source).
        // ----------------------------------------------------------------
        Player? enteringController = null;

        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (ReferenceEquals(e.Card, card)) return false; // "another" — exclude self
            enteringController = e.Card.Controller;
            return true;
        });

        var pingEffect = new Effect(
            $"{CardName}: deal {PingDamage} damage to the entering creature's controller",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var target = enteringController;
                enteringController = null;
                if (target == null) return;
                if (target.HasLost) return;
                // CR 119.3 — damage to a player is life loss. Source-aware so
                // any future source-keyed rider observes the Ferocidon.
                Fx.DealDamageAny(target, PingDamage, card);
            });

        var pingTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { pingEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(pingTrigger);
        triggers?.RegisterTriggeredAbility(pingTrigger);

        // ----------------------------------------------------------------
        // "Players can't gain life" static — CR 119.6 / CR 614.1.
        //   Register a LifeGainIntent replacement that rewrites every gain to
        //   a zero-amount intent. Same shape as Sulfuric Vortex.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new LambdaReplacement<LifeGainIntent>(
                applies: (_, _) => true,
                replace: (intent, _) => intent with { Amount = 0 },
                oneShot: false,
                tag: card));
        }

        return card;
    }
}
