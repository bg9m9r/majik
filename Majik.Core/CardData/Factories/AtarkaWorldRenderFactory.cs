using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Atarka, World Render (Dragons of Tarkir, {5}{R}{R}).
///
/// Legendary Creature — Elder Dragon 6/4. Oracle text:
///   "Flying, trample.
///    Whenever a Dragon you control attacks, it gains double strike until
///    end of turn."
///
/// ## Implemented (v1)
/// - 6/4 Legendary Creature — Elder Dragon, mana cost {5}{R}{R}.
/// - <b>Flying</b> (CR 702.9) and <b>Trample</b> (CR 702.19) wired as
///   <see cref="KeywordAbility"/> markers — the combat helpers in
///   <see cref="Majik.Core.Combat.CombatAbilities"/> read these directly,
///   same shape as every other printed Flying/Trample creature in this repo.
/// - <b>Dragon-attack triggered ability (CR 603.1 / 508.1f)</b>: fires on
///   <see cref="CreatureAttacksEvent"/> whose attacker is a Dragon
///   controlled by Atarka's current controller. INCLUDES Atarka itself —
///   the printed oracle has no "other" qualifier on this rider, so Atarka
///   triggers off its own attack too.
/// - On resolution, the effect re-reads the live attackers via the
///   supplied <c>attackingCreaturesSource</c> closure (Rabblemaster /
///   Piledriver shape), and registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> for "Double strike"
///   (CR 702.4) on every attacking Dragon Atarka's controller controls.
///   This collapses one-trigger-per-attacking-Dragon down to one
///   resolution scan — observationally identical at the combat-damage
///   step because each Dragon is granted Double strike exactly once
///   (HashSet keyword set in <see cref="CreatureCharacteristics"/>).
///
/// ## Source-closure injection
/// Same shape as <see cref="GoblinRabblemasterFactory"/> /
/// <see cref="IgnobleHierarchFactory"/> — the engine doesn't yet expose a
/// global "currently attacking creatures" view from inside the effect
/// closure, so the factory accepts a
/// <c>Func&lt;IReadOnlyList&lt;Creature&gt;&gt;</c> closure that callers
/// (Game / tests) populate with the live attacker list. When null, the
/// effect body is a no-op (suitable for shape / dispatcher tests).
///
/// ## Deferred (v1 gaps)
/// - <b>Live combat-attackers provider</b>: production callers must wire
///   the closure manually. Once <c>ICurrentCombatProvider</c> ships, this
///   factory will read attackers off the live provider directly. Same
///   caveat as <see cref="GoblinRabblemasterFactory"/>.
/// - <b>Trigger-on-stack timing</b>: the keyword grant is registered
///   immediately when the trigger effect runs. Real MTG semantics put the
///   trigger on the stack before blockers are declared; v1 collapses this
///   to "trigger resolves now" (observationally equivalent for the
///   combat-damage step because Double strike's first-strike sub-step
///   reads the keyword set after the grant is in place). Same posture as
///   every other attack-trigger factory in this repo.
/// </summary>
[CardName("Atarka, World Render")]
public static class AtarkaWorldRenderFactory
{
    public const string CardName = "Atarka, World Render";
    public const string PrintedManaCost = "{5}{R}{R}";
    public const int Power = 6;
    public const int Toughness = 4;

    /// <summary>Granted keyword — CR 702.4 Double strike.</summary>
    public const string GrantedDoubleStrike = "Double strike";

    /// <summary>
    /// Construct Atarka, World Render with no live wiring. The
    /// Dragon-attack trigger is attached to the card shape but is not
    /// registered with a TriggerManager and the effect body has no
    /// attackers source. Suitable for factory-shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Atarka, World Render with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the Dragon-attack
    /// trigger against. May be null — trigger is still attached to the
    /// card shape.</param>
    /// <param name="attackingCreaturesSource">Closure returning the
    /// current attacker creature list. Called at trigger resolution. May
    /// be null — effect body is a no-op.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Elder, CardSubtype.Dragon });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.9 Flying + CR 702.19 Trample keyword markers.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // CR 603.1 / 508.1f — "Whenever a Dragon you control attacks, it
        // gains double strike until end of turn."
        // Condition: any CreatureAttacksEvent where the attacker is a
        // Dragon controlled by Atarka's current controller. Effect:
        // re-scan live attackers and grant Double strike EOT to every
        // attacking Dragon Atarka's controller controls (idempotent —
        // keyword set dedupes).
        // ----------------------------------------------------------------
        var grantEffect = new Effect(
            $"{CardName}: each attacking Dragon you control gains {GrantedDoubleStrike} EOT",
            () =>
            {
                if (attackingCreaturesSource == null) return;

                var controller = card.Controller ?? owner;
                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();

                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (!atk.HasSubtype(CardSubtype.Dragon)) continue;
                    if (!ReferenceEquals(atk.Controller, controller)) continue;
                    if (atk.ActiveEffects == null) continue;

                    // CR 613.1c Layer 6 — keyword grant: Double strike.
                    atk.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(atk, GrantedDoubleStrike));
                }
            });

        var attackCondition = new EventTriggerCondition<CreatureAttacksEvent>(
            (e, _) =>
            {
                if (e.Attacker == null) return false;
                if (!e.Attacker.HasSubtype(CardSubtype.Dragon)) return false;
                return ReferenceEquals(e.Attacker.Controller, card.Controller);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: attackCondition,
            effects: new IEffect[] { grantEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
