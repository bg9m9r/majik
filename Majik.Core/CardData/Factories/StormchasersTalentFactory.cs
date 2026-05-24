using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormchaser's Talent (Modern Horizons 3, {U}{R}).
///
/// Enchantment — Class {U}{R}. Oracle text:
///   "Class. (Gain the next level as a sorcery to add its ability.)
///    When this Class enters, create a 1/1 blue and red Mercenary creature
///      token with prowess.
///    {1}{U}{R}: Level 2
///    — Whenever you cast a noncreature spell, the Mercenary deals 1 damage
///      to any target.
///    {3}{U}{R}: Level 3
///    — Whenever you cast a noncreature spell, draw a card, then discard a
///      card."
///
/// ## Implemented (v1 — ETB-only fallback)
/// - Shell: <see cref="Enchantment"/> with <see cref="CardSubtype.Class"/>
///   subtype (CR 205.3h / CR 716). Mana cost {U}{R}.
/// - <b>ETB trigger</b> (CR 603.6a): "When this Class enters, create a 1/1
///   blue and red Mercenary creature token with prowess." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. Token built via
///   <see cref="TokenFactory.CreateOnBattlefield"/> with
///   <see cref="CardSubtype.Mercenary"/> and a <c>"Prowess"</c> keyword
///   marker in the <see cref="TokenFactory.TokenSpec.Keywords"/> list
///   (CR 702.108). Routes through <see cref="ZoneService"/> when supplied
///   so <see cref="CardMovedEvent"/> fires for downstream ETB listeners
///   (Soul Warden, Champion of the Parish, etc.).
///
/// ## Deferred (v1 gaps — full Class mechanic)
/// - <b>Class leveling (CR 716)</b>: the activated level-up abilities
///   <c>{1}{U}{R}: Level 2</c> and <c>{3}{U}{R}: Level 3</c> are NOT wired.
///   Blockers: (1) no per-activated-ability sorcery-speed gate yet (same
///   gap as Tasigur, the Golden Fang's {B}{G}{U} activation, Wishclaw
///   Talisman's tutor, Priest of Fell Rites' reanimate); (2) no Class-level
///   tracker bound to the card via a binder analogous to
///   <see cref="CardData.SagaBinder"/> (the
///   <see cref="CardData.Classes.ClassState"/> MVP exists but no
///   Permanent-side hook attaches it); (3) "next level only, sequential"
///   restriction (CR 716.2 / 716.3) needs a custom activated-ability cost
///   gate. The <see cref="CardData.Classes.ClassState"/> primitive ships
///   as the state holder; wire-up is a follow-up PR.
/// - <b>Level 2 cast-trigger</b> ("Whenever you cast a noncreature spell,
///   the Mercenary deals 1 damage to any target"): DEFERRED with the
///   leveling primitive. Requires (a) the level gate above, plus (b) a
///   per-Class registry of "which Mercenary token did this Class spawn"
///   so the trigger targets the right body (the printed text says "the
///   Mercenary", not "target creature"). Single-target damage routing
///   for "any target" is already covered by
///   <see cref="OracleSpellBinder.DealDamage"/>.
/// - <b>Level 3 cast-trigger</b> ("Whenever you cast a noncreature spell,
///   draw a card, then discard a card"): DEFERRED with the leveling
///   primitive. Loot body itself is well-trodden — same shape as
///   <see cref="PsychicFrogFactory"/> / <see cref="FaithlessLootingFactory"/>
///   /<see cref="SwordOfFeastAndFamineFactory"/>. Discard prompt would
///   inherit the deterministic-first-card v1 picker.
/// - <b>Token colour identity (blue + red)</b>: Mercenary token is created
///   as colourless under the v1 token shape — same gap as Esika's Chariot's
///   green Cats, Crashing Footfalls' green Rhinos, Pact of the Titan's red
///   Giant. Subtype + P/T + token flag are correct;
///   <c>CardColors</c> plumbing for tokens is the broader fix.
/// - <b>Prowess pump on the token</b>: the <c>"Prowess"</c> keyword marker
///   is attached to the Mercenary token so structural shape inspection
///   (and `KeywordAbility` introspection) reads Prowess, but the
///   <see cref="Majik.Core.Keywords.ProwessFactory"/> triggered-ability
///   pump requires a live <see cref="ContinuousEffectsService"/> which
///   the <see cref="TokenFactory"/> shape doesn't yet thread through to
///   token-resident keywords. Same v1 gap as
///   <see cref="MonasteryMentorFactory"/>'s spawned Monk tokens (see that
///   factory's xmldoc for the broader plan).
/// </summary>
public static class StormchasersTalentFactory
{
    public const string CardName = "Stormchaser's Talent";
    public const string PrintedManaCost = "{U}{R}";

    /// <summary>
    /// Construct Stormchaser's Talent with no live ZoneService / TriggerManager
    /// wiring. The ETB Mercenary-token trigger is attached for shape
    /// inspection; tests fire it by invoking the effect directly. Suitable
    /// for dispatcher / shape tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Stormchaser's Talent with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, the spawned Mercenary
    /// token routes through <see cref="TokenFactory.CreateOnBattlefield"/>
    /// using the service so the token publishes <see cref="CardMovedEvent"/>
    /// on battlefield entry (downstream ETB listeners fire). When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered for bus-driven firing.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Class });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When this Class enters, create a 1/1 blue and red Mercenary
        //    creature token with prowess."
        // Token colour identity (blue + red) deferred — see class xmldoc.
        // Prowess pump on token deferred — keyword marker only, see class
        // xmldoc.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create a 1/1 Mercenary creature token with prowess",
            () => CreateMercenaryToken(card.Controller ?? owner, zoneService));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.6a ETB effect — create a 1/1 Mercenary creature token with
    /// the <c>"Prowess"</c> keyword marker under <paramref name="controller"/>'s
    /// control. Token colour identity (blue + red) deferred (see class
    /// xmldoc); Prowess pump on the token deferred (see class xmldoc).
    /// </summary>
    private static Creature CreateMercenaryToken(Player controller, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Mercenary",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Mercenary },
            Keywords: new[] { "Prowess" });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
