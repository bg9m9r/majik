using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kor Firewalker (Worldwake, {W}).
///
/// Creature — Kor Soldier 2/2. Oracle text:
///   "Protection from red
///    Whenever a player casts a red spell, you may gain 1 life."
///
/// ## Implemented (v1)
///
/// - 2/2 <see cref="Creature"/> at {W} with subtypes Kor, Soldier,
///   owner / controller wired.
/// - <b>Protection from red (CR 702.16)</b>: a single
///   <see cref="ProtectionAbility"/> ("red"). The
///   <see cref="Majik.Core.Rules.Protection"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities"/> / target-legality
///   helpers interpret the quality (DEBT-A: damage / enchant+equip /
///   block / target). Same always-on, no-IsActive-gate shape as
///   <see cref="BurrentonForgeTenderFactory"/> /
///   <see cref="PhyrexianCrusaderFactory"/>.
/// - <b>Red-spell-cast lifegain trigger (CR 603.1)</b> over
///   <see cref="SpellCastEvent"/>:
///     * Fires for ANY player's spell (controller's own included —
///       oracle "a player" is unrestricted, same posture as
///       <see cref="EidolonOfTheGreatRevelFactory"/>).
///     * Gated on the cast spell being red: the predicate reads the
///       spell's colours via <see cref="CardColors.GetColors"/> (CR 105.2
///       — a card is each colour of its mana-cost pips / color
///       indicator) and fires iff <see cref="ManaColor.Red"/> is present.
///       A multicolour spell with a red pip (e.g. {R}{W}) is a red spell.
///     * Resolution gains Kor Firewalker's controller 1 life via
///       <see cref="Fx.GainLife"/> (CR 119.3 — life gain routes through
///       <see cref="Player.GainLife"/> so LifeGainedThisTurn / Ajani
///       Pridemate-style observers see the gain).
///
/// ## "you may" — optional clause (v1 simplification)
///
/// The oracle reads "you <b>may</b> gain 1 life". Gaining life is purely
/// beneficial with no downside, and the engine's
/// <see cref="TriggeredAbility"/> ctor has no per-trigger "may decline"
/// agent prompt surface (the same modal-yes/no choice that other "may"
/// triggers defer). v1 always takes the gain. A rational agent never
/// declines a free life point, so this is behaviour-equivalent for every
/// game state; the only observable difference would be a contrived
/// interaction with "if you gained life this turn" / "whenever you gain
/// life" payoffs the controller wished to suppress, which v1 does not
/// model. Wiring the optional prompt is deferred to the shared
/// may-trigger choice infrastructure.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only; the trigger is
///   attached for structural observability but not registered with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired; the
///   trigger is registered so any <see cref="SpellCastEvent"/> for a red
///   spell automatically queues the 1-life gain.
/// </summary>
[CardName("Kor Firewalker")]
public static class KorFirewalkerFactory
{
    public const string CardName = "Kor Firewalker";
    public const string PrintedManaCost = "{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Kor Firewalker with no live TriggerManager wiring. The
    /// trigger is attached to the card shape so dispatcher tests see it;
    /// pass the (owner, triggers) overload to register it for live
    /// <see cref="SpellCastEvent"/> dispatch.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Kor Firewalker with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the red-spell
    /// lifegain trigger is registered for live dispatch.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Kor, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.16 — Protection from red. Quality "red"; Rules.Protection
        // / CombatAbilities / TargetLegality interpret it (DEBT-A). Single
        // always-on quality, same shape as Burrenton Forge-Tender.
        card.AddAbility(new ProtectionAbility("red"));

        // CR 603.1 — "Whenever a player casts a red spell, you may gain 1
        // life." Predicate fires for any player's spell whose colours
        // (CR 105.2, via CardColors.GetColors) include red.
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (e.Spell?.Card is not { } spellCard) return false;
            return CardColors.GetColors(spellCard).Contains(ManaColor.Red);
        });

        // Resolution: gain Kor Firewalker's controller 1 life. "you" = the
        // ability's controller (CR 109.5 / 603.1). v1 always takes the
        // optional gain (see class xmldoc). Read the live controller off
        // the card so a control-change effect points "you" at the current
        // controller.
        var gainEffect = new Effect(
            $"{CardName}: gain 1 life (controller)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var you = card.Controller ?? owner;
                if (you.HasLost) return;

                // CR 119.3 — life gain through Player.GainLife so
                // LifeGainedThisTurn / "whenever you gain life" observers
                // see it.
                Fx.GainLife(you, 1);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { gainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
