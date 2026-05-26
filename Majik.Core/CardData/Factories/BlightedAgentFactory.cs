using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blighted Agent (New Phyrexia, {1}{U}).
///
/// Creature — Phyrexian Human Rogue 1/1. Oracle text:
///   "Blighted Agent can't be blocked.
///    Infect (This creature deals damage to creatures in the form of
///    -1/-1 counters and to players in the form of poison counters.)"
///
/// ## Implemented (v1)
///
/// - 1/1 <see cref="Creature"/> at {1}{U} with subtypes Phyrexian,
///   Human, Rogue.
/// - <b>"Blighted Agent can't be blocked." (CR 702.x / CR 509.1c)</b>
///   — registered on the supplied <see cref="ContinuousEffectsService"/>
///   as a non-expiring <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/> scoped to this
///   creature. <see cref="CombatValidator"/> consults the restriction
///   directly (Apply is a no-op for restrictions — see
///   <see cref="CombatRestrictionEffect"/>). A
///   <see cref="KeywordAbility"/> "Unblockable" marker is also attached
///   so card-text inspection / keyword scans observe the rider when no
///   live continuous-effects service is wired (shape-only path).
/// - <b>Infect (CR 702.90)</b> — wired as a <see cref="KeywordAbility"/>
///   marker. The combat-damage replacement (CR 702.90b — infect
///   sources deal damage to creatures as -1/-1 counters and to players
///   as poison counters) is not yet plumbed engine-side; the marker
///   surfaces the keyword on the card so a downstream Infect primitive
///   picks Blighted Agent up for free. Same posture as
///   <see cref="InkmothNexusFactory"/>'s Infect marker on the animated
///   land body.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Infect damage-replacement</b>: the combat-damage pipeline
///   currently treats Infect-sourced damage as ordinary damage. Poison
///   counter tracking on <see cref="Player"/> and the layered combat
///   replacement land in a follow-up infrastructure PR. When that
///   lands, Blighted Agent + Phyrexian Crusader + Plague Myr +
///   Inkmoth Nexus all become live infect threats without further card
///   wiring.
/// - <b>"Can't be blocked" without a continuous-effects service</b>:
///   the shape-only <see cref="Create(Player)"/> path attaches the
///   Unblockable keyword marker but does NOT install the
///   <see cref="CombatRestrictionEffect"/> — <see cref="CombatValidator"/>
///   without an effects service still allows blocks. Production
///   callers thread the live service via the (owner, effects) overload.
/// </summary>
[CardName("Blighted Agent")]
public static class BlightedAgentFactory
{
    public const string CardName = "Blighted Agent";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Blighted Agent with no continuous-effects service. The
    /// Unblockable + Infect keyword markers are attached for card-text
    /// inspection but the live "can't be blocked" combat restriction is
    /// NOT registered. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Blighted Agent with an optional
    /// <see cref="ContinuousEffectsService"/>. When the service is
    /// supplied the "can't be blocked" rider is registered as a
    /// non-expiring <see cref="CombatRestrictionEffect"/> bound to
    /// Blighted Agent so <see cref="CombatValidator"/> rejects block
    /// declarations targeting it (CR 509.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. May be null —
    /// the unblockable restriction is then skipped (keyword marker
    /// still attached for inspection).</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[]
            {
                CardSubtype.Phyrexian,
                CardSubtype.Human,
                CardSubtype.Rogue,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Blighted Agent can't be blocked." — CR 702.x / CR 509.1c.
        //
        // Keyword marker covers the card-text / inspection surface; the
        // working combat restriction is registered on the supplied
        // ContinuousEffectsService (no-op on the shape-only path).
        // Restriction does not expire at end of turn — Blighted Agent is
        // permanently unblockable while on the battlefield. The current
        // CombatRestrictionEffect has no zone-gate (no IsActive override
        // on the sealed type), so once registered the restriction
        // persists for the card's lifetime in the effects service.
        // Producers that need fine-grained zone gating subclass
        // ContinuousEffect directly (see InkmothAnimateLandEffect's
        // IsActive zone check); v1 of Blighted Agent ships the simpler
        // shape — the restriction is benign off-battlefield (the
        // CombatValidator only consults it during attack/block
        // declaration on live battlefield creatures).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Unblockable", card, owner));

        effects?.Register(new CombatRestrictionEffect(
            CombatRestriction.CannotBeBlocked,
            target: card,
            expiresAtEndOfTurn: false));

        // ----------------------------------------------------------------
        // Infect — CR 702.90. Keyword marker only; the combat-damage
        // replacement (-1/-1 counters to creatures + poison counters to
        // players) is deferred. Marker exists so future Infect primitive
        // picks up Blighted Agent without re-touching the factory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        return card;
    }
}
