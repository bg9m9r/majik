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
/// Named-card factory for Brimaz, King of Oreskos (Born of the Gods,
/// {1}{W}{W}). Legendary Creature — Cat Soldier, 3/4. Oracle text (verified
/// against Scryfall):
///   "Vigilance
///    Whenever Brimaz attacks, create a 1/1 white Cat Soldier creature token
///    with vigilance that's attacking.
///    Whenever Brimaz blocks a creature, create a 1/1 white Cat Soldier
///    creature token with vigilance that's blocking that creature."
///
/// ## Implemented (v1)
///
/// - <b>Vigilance (CR 702.21)</b> — a <see cref="KeywordAbility"/> marker so
///   <c>ICard.Abilities</c> reflects the printed line and combat declaration
///   reads vigilance off the keyword set (Brimaz does not tap to attack).
///
/// - <b>"Whenever Brimaz attacks, create a 1/1 white Cat Soldier creature
///   token with vigilance that's attacking" (CR 508.3g)</b> — a per-attacker
///   <see cref="TriggeredAbility"/> on <see cref="Triggers.OnAttackSelf"/>
///   (<see cref="CreatureAttacksEvent"/> for Brimaz). On resolution a 1/1
///   white Cat Soldier token <i>with vigilance</i> is minted via
///   <see cref="TokenFactory.CreateOnBattlefield"/> and spliced into the
///   in-progress combat already tapped-and-attacking the same defender, via
///   <see cref="CombatManager.AddTappedAndAttackingToken"/> (the same combat
///   primitive Mobilize / Voice of Victory use — CR 508.3 enters tapped /
///   CR 508.4 attacks the same defender). Because the token is "put onto the
///   battlefield attacking" rather than declared, it does not re-trigger
///   Brimaz's own attack trigger (CR 508.3g).
///
/// - <b>"Whenever Brimaz blocks a creature, create a 1/1 white Cat Soldier
///   creature token with vigilance that's blocking that creature" (CR 509.1g)</b>
///   — a <see cref="TriggeredAbility"/> on <see cref="Triggers.OnBlockSelf"/>
///   (<see cref="BlockersDeclaredEvent"/> where Brimaz is a blocker). On
///   resolution a 1/1 white Cat Soldier token <i>with vigilance</i> is minted
///   and spliced into the in-progress combat already blocking the SAME
///   attacker Brimaz blocked, via the new
///   <see cref="CombatManager.AddTokenBlockingAttacker"/> primitive — the
///   mirror image of the attacking-token helper. The attacker Brimaz blocked
///   is recovered from the live combat at resolution (the blocker entry whose
///   creature is Brimaz). Because the token is "put onto the battlefield
///   blocking" rather than declared, it does not re-trigger Brimaz's block
///   trigger (CR 509.1g).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Multi-block disambiguation</b>: in the rare case Brimaz is declared
///   blocking more than one attacker in the same combat (only possible via an
///   outside effect granting "can block additional creatures"), v1 mints one
///   token blocking the FIRST attacker Brimaz blocks. The printed trigger
///   fires once per "blocks a creature" instance; per-attacker fan-out is
///   deferred behind multi-block declaration plumbing.
/// - <b>No-combat fallback</b>: when no combat is live (or the spliced
///   defender/attacker is gone) the token enters the battlefield untapped and
///   not attacking/blocking — the "attacking" / "blocking" fidelity requires a
///   live combat to splice into (same no-combat fallback as Mobilize).
/// </summary>
[CardName("Brimaz, King of Oreskos")]
public static class BrimazKingOfOreskosFactory
{
    public const string CardName = "Brimaz, King of Oreskos";
    public const string PrintedManaCost = "{1}{W}{W}";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>Granted keyword — CR 702.21 Vigilance.</summary>
    public const string Vigilance = "Vigilance";

    /// <summary>Minted token — 1/1 white Cat Soldier with vigilance.</summary>
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>The 1/1 white Cat Soldier (vigilance) token spec.</summary>
    public static TokenFactory.TokenSpec CatSoldierTokenSpec { get; } =
        new(
            Name: "Cat Soldier",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Cat, CardSubtype.Soldier },
            Keywords: new[] { Vigilance },
            Colors: new[] { ManaColor.White });

    /// <summary>
    /// Construct Brimaz with no live runtime wiring (the dispatcher / shape
    /// path). Vigilance and both combat triggers are attached for shape
    /// observability; with no <see cref="TriggerManager"/> / combat the
    /// triggers create no tokens. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, combat: null);

    /// <summary>
    /// Construct Brimaz with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, both the attack trigger
    /// (<see cref="CreatureAttacksEvent"/>) and the block trigger
    /// (<see cref="BlockersDeclaredEvent"/>) are registered so they land on the
    /// stack automatically. May be null (shape only).</param>
    /// <param name="combat">When supplied, the attack token is spliced into the
    /// in-progress combat tapped-and-attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>) and the block
    /// token is spliced in blocking the attacker Brimaz blocked
    /// (<see cref="CombatManager.AddTokenBlockingAttacker"/>). May be null.</param>
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
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.21 — Vigilance keyword marker.
        card.AddAbility(new KeywordAbility(Vigilance, card, owner));

        AddAttackTrigger(card, owner, triggers, combat);
        AddBlockTrigger(card, owner, triggers, combat);

        return card;
    }

    // -----------------------------------------------------------------------
    // "Whenever Brimaz attacks, create a 1/1 white Cat Soldier creature token
    // with vigilance that's attacking." (CR 508.1f / 508.3g.)
    // -----------------------------------------------------------------------
    private static void AddAttackTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        var effect = new Effect(
            $"{CardName}: on attack, create a 1/1 white Cat Soldier (vigilance) tapped & attacking",
            () => ResolveAttackTrigger(card, owner, combat));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    private static void ResolveAttackTrigger(Creature card, Player owner, CombatManager? combat)
    {
        var controller = card.Controller ?? owner;
        var token = TokenFactory.CreateOnBattlefield(CatSoldierTokenSpec, controller);

        // CR 508.3 / 508.4 — splice the token into the in-progress combat as a
        // tapped-and-attacking token (same defender). No live combat → the
        // token stays on the battlefield untapped (no-combat fallback).
        combat?.AddTappedAndAttackingToken(token);
    }

    // -----------------------------------------------------------------------
    // "Whenever Brimaz blocks a creature, create a 1/1 white Cat Soldier
    // creature token with vigilance that's blocking that creature."
    // (CR 509.1g.)
    // -----------------------------------------------------------------------
    private static void AddBlockTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        var effect = new Effect(
            $"{CardName}: on block, create a 1/1 white Cat Soldier (vigilance) blocking that creature",
            () => ResolveBlockTrigger(card, owner, combat));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnBlockSelf(card),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    private static void ResolveBlockTrigger(Creature card, Player owner, CombatManager? combat)
    {
        var controller = card.Controller ?? owner;
        var token = TokenFactory.CreateOnBattlefield(CatSoldierTokenSpec, controller);

        // CR 509.1g — recover the attacker Brimaz blocked from the live combat
        // (the blocker entry whose creature is Brimaz) and splice the token in
        // already blocking that SAME attacker. No live combat / Brimaz not a
        // blocker → the token stays on the battlefield not blocking.
        var blockedAttacker = combat?.CurrentCombat?
            .GetAllBlockers()
            .FirstOrDefault(b => ReferenceEquals(b.Creature, card))?
            .BlockedAttacker;

        if (blockedAttacker != null)
        {
            combat?.AddTokenBlockingAttacker(token, blockedAttacker);
        }
    }
}
