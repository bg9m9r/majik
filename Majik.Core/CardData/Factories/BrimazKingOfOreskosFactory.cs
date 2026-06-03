using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brimaz, King of Oreskos (Born of the Gods, {1}{W}{W}).
///
/// Legendary Creature — Cat Soldier 3/4. Oracle text (verified against
/// Scryfall):
///   "Vigilance
///    Whenever Brimaz attacks, create a 1/1 white Cat Soldier creature token
///    with vigilance that's attacking.
///    Whenever Brimaz blocks a creature, create a 1/1 white Cat Soldier
///    creature token with vigilance that's blocking that creature."
///
/// ## Implementation
///
/// - 3/4 Legendary white Cat Soldier, mana cost {1}{W}{W}.
/// - <b>Vigilance (CR 702.21)</b>: a <see cref="KeywordAbility"/> marker, read
///   by the combat tap path.
/// - <b>Attack token rider (CR 508.3g)</b>: a <see cref="TriggeredAbility"/> on
///   <see cref="Triggers.OnAttackSelf"/> that creates a 1/1 white Cat Soldier
///   token with vigilance and splices it into the in-progress combat via
///   <see cref="CombatManager.AddTappedAndAttackingToken"/>. That seam taps the
///   token (CR 508.3); the printed token has vigilance, but a creature "put
///   onto the battlefield attacking" was never "declared as an attacker"
///   (CR 508.3g), so the vigilance don't-tap rule (CR 508.1k) never applied —
///   the token enters tapped via the splice regardless. Same shape as
///   <see cref="HanweirGarrisonFactory"/> / <see cref="HeroOfBladeholdFactory"/>.
/// - <b>Block token rider (CR 509.1h — token blocking a specific attacker)</b>:
///   a <see cref="TriggeredAbility"/> on <see cref="Triggers.OnBlockSelf"/> that
///   creates a 1/1 white Cat Soldier token with vigilance and splices it into
///   the in-progress combat as a token that is already blocking the SAME
///   attacker Brimaz blocked (the attacker travels on
///   <see cref="CreatureBlocksEvent"/> and is captured by the trigger's
///   condition), via the new
///   <see cref="CombatManager.AddBlockingToken"/> seam — the block-side sibling
///   of <see cref="CombatManager.AddTappedAndAttackingToken"/>.
///
/// ## No-combat fallback
/// Same posture as <see cref="HanweirGarrisonFactory"/>: when
/// <paramref name="combat"/> is null (shape / dispatcher tests) the tokens
/// still enter the battlefield, just not attacking / blocking — the combat
/// fidelity requires a live combat to splice into.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (the <see cref="NamedCardFactory"/>
///   dispatch target). Vigilance + both riders attached; tokens enter plain.
/// - <see cref="Create(Player, TriggerManager?, CombatManager?, ZoneService?)"/>
///   — fully wired.
/// </summary>
[CardName("Brimaz, King of Oreskos")]
public static class BrimazKingOfOreskosFactory
{
    public const string CardName = "Brimaz, King of Oreskos";
    public const string PrintedManaCost = "{1}{W}{W}";
    public const int Power = 3;
    public const int Toughness = 4;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>1/1 white Cat Soldier token with vigilance.</summary>
    private static TokenFactory.TokenSpec CatSoldierSpec() => new(
        Name: "Cat Soldier",
        Power: TokenPower,
        Toughness: TokenToughness,
        Subtypes: new[] { CardSubtype.Cat, CardSubtype.Soldier },
        Keywords: new[] { "Vigilance" },
        Colors: new[] { ManaColor.White });

    /// <summary>
    /// Construct Brimaz with no live runtime wiring (the dispatch target).
    /// Both attack and block riders are attached to the card shape; the token
    /// riders create plain battlefield tokens (no combat splice).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, combat: null, zones: null);

    /// <summary>
    /// Construct Brimaz with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both riders are registered so the
    /// matching <see cref="CreatureAttacksEvent"/> / <see cref="CreatureBlocksEvent"/>
    /// land them on the stack automatically.</param>
    /// <param name="combat">When supplied, the attack token is spliced in tapped
    /// and attacking (<see cref="CombatManager.AddTappedAndAttackingToken"/>) and
    /// the block token is spliced in blocking the captured attacker
    /// (<see cref="CombatManager.AddBlockingToken"/>).</param>
    /// <param name="zones">When supplied, tokens are created through the
    /// ZoneService so their ETB <see cref="CardMovedEvent"/> fires.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);
        card.AddSupertype(CardSupertype.Legendary);

        // CR 702.21 — Vigilance keyword marker.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // ----------------------------------------------------------------
        // CR 508.3g — "Whenever Brimaz attacks, create a 1/1 white Cat Soldier
        // creature token with vigilance that's attacking."
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: create a 1/1 white Cat Soldier (vigilance) attacking",
            () =>
            {
                var token = TokenFactory.CreateOnBattlefield(
                    CatSoldierSpec(), card.Controller ?? owner, zones);
                // CR 508.3g — splice the token into the in-progress combat as
                // tapped and attacking the same defender. No-combat fallback:
                // token stays on the battlefield not attacking.
                combat?.AddTappedAndAttackingToken(token);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // CR 509.1h — "Whenever Brimaz blocks a creature, create a 1/1 white
        // Cat Soldier creature token with vigilance that's blocking that
        // creature." The blocked attacker travels on CreatureBlocksEvent; the
        // condition captures it so the effect knows which attacker the new
        // token blocks (mirrors OracleTriggeredAbilityBinder's captured-event
        // pattern).
        // ----------------------------------------------------------------
        Creature? capturedBlockedAttacker = null;

        var blockEffect = new Effect(
            $"{CardName}: create a 1/1 white Cat Soldier (vigilance) blocking that creature",
            () =>
            {
                var token = TokenFactory.CreateOnBattlefield(
                    CatSoldierSpec(), card.Controller ?? owner, zones);
                // CR 509.1h — splice the token in as a token already blocking
                // the same attacker Brimaz blocked. No-combat fallback (or the
                // attacker already left combat): token stays on the battlefield
                // not blocking.
                var blocked = capturedBlockedAttacker;
                if (blocked != null)
                {
                    combat?.AddBlockingToken(token, blocked);
                }
            });

        var blockCondition = new EventTriggerCondition<CreatureBlocksEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Blocker, card)) return false;
            capturedBlockedAttacker = e.BlockedAttacker;
            return true;
        });

        var blockTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: blockCondition,
            effects: new IEffect[] { blockEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(blockTrigger);
        triggers?.RegisterTriggeredAbility(blockTrigger);

        return card;
    }
}
