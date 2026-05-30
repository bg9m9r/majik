using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hanweir Garrison (Eldritch Moon, {2}{R}).
///
/// Creature — Human Soldier, 2/3. Oracle text:
///   "Whenever this creature attacks, create two 1/1 red Human creature
///    tokens that are tapped and attacking.
///    (Melds with Hanweir Battlements.)"
///
/// ## Implemented (v1)
/// - 2/3 red Human Soldier at {2}{R}, owner / controller wired
///   (CR 105 — red from the {R} pip).
/// - <b>"Whenever this creature attacks, create two 1/1 red Human creature
///   tokens that are tapped and attacking" (CR 508.3g)</b> — an
///   <see cref="Triggers.OnAttackSelf"/> <see cref="TriggeredAbility"/> that,
///   on resolution, creates two 1/1 red Human tokens via
///   <see cref="TokenFactory.CreateOnBattlefield"/> and splices each into the
///   in-progress combat as a token that is already tapped and attacking the
///   same defender as the Garrison, via
///   <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3 —
///   enters tapped; CR 508.4 — attacking the same player / planeswalker).
///   Because the tokens are "put onto the battlefield attacking" rather than
///   "declared" as attackers, they do NOT re-trigger the Garrison's own
///   attack trigger (CR 508.3g). This is the same token-rider shape as
///   <see cref="HeroOfBladeholdFactory"/>, minus the Battle cry line and with
///   red Human tokens (not white Soldier).
///
/// ## No-combat fallback
/// Same posture as <see cref="HeroOfBladeholdFactory"/> /
/// <see cref="VoiceOfVictoryFactory"/>: when <paramref name="combat"/> is null
/// (shape / dispatcher tests) the tokens still enter the battlefield, just
/// untapped and not attacking — the "tapped and attacking" fidelity requires a
/// live combat to splice into.
///
/// ## Deferred (v1 gaps)
/// - <b>Meld with Hanweir Battlements</b> (CR 712 — meld cards). The reminder
///   text "(Melds with Hanweir Battlements.)" is not modelled; meld is a
///   not-yet-built mechanic. The standalone Garrison is fully functional on
///   its own; only the meld-into-Hanweir, the Writhing Township side is
///   absent. No other card in the modelled pool references the meld, so this
///   is observationally inert here.
/// </summary>
[CardName("Hanweir Garrison")]
public static class HanweirGarrisonFactory
{
    public const string CardName = "Hanweir Garrison";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>Two 1/1 red Human tokens per attack.</summary>
    public const int TokenCount = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Hanweir Garrison with no live runtime wiring. The attack
    /// trigger is attached to the card shape; the token rider creates plain
    /// battlefield tokens (no combat splice). Suitable for dispatcher / shape
    /// tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, combat: null);

    /// <summary>
    /// Construct Hanweir Garrison with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger is registered
    /// so a <see cref="CreatureAttacksEvent"/> for the Garrison lands it on the
    /// stack automatically.</param>
    /// <param name="combat">When supplied, the Human tokens are spliced into
    /// the in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 508.3g — "Whenever this creature attacks, create two 1/1 red
        // Human creature tokens that are tapped and attacking."
        var tokenEffect = new Effect(
            $"{CardName}: create {TokenCount} tapped & attacking 1/1 red Humans",
            () => ResolveTokenRider(card, owner, combat));

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return card;
    }

    /// <summary>
    /// CR 508.3g — create two 1/1 red Human tokens and splice each into the
    /// in-progress combat tapped and attacking the same defender as the
    /// Garrison. When no combat is live the tokens enter the battlefield
    /// untapped (the "tapped and attacking" fidelity requires a combat to
    /// splice into).
    /// </summary>
    private static void ResolveTokenRider(Creature source, Player owner, CombatManager? combat)
    {
        var controller = source.Controller ?? owner;

        // CR 111.4 — two 1/1 red Human creature tokens.
        var spec = new TokenFactory.TokenSpec(
            Name: "Human",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Human },
            Keywords: null,
            Colors: new[] { ManaColor.Red });

        for (int i = 0; i < TokenCount; i++)
        {
            var token = TokenFactory.CreateOnBattlefield(spec, controller);

            // CR 508.3g — splice the token into the in-progress combat as a
            // tapped and attacking token. When no combat is live the token
            // stays on the battlefield untapped (no-combat fallback).
            combat?.AddTappedAndAttackingToken(token);
        }
    }
}
