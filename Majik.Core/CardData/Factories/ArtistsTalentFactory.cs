using Majik.Core.Abilities;
using Majik.Core.CardData.Classes;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Artist's Talent (Bloomburrow, {1}{R}).
///
/// Enchantment — Class {1}{R}. Oracle text (verified vs Scryfall):
///   "(Gain the next level as a sorcery to add its ability.)
///    Whenever you cast a noncreature spell, you may discard a card. If you
///      do, draw a card.
///    {2}{R}: Level 2
///    Noncreature spells you cast cost {1} less to cast.
///    {2}{R}: Level 3
///    If a source you control would deal noncombat damage to an opponent or
///      a permanent an opponent controls, it deals that much damage plus 2
///      instead."
///
/// ## Implementation (full Class leveling — CR 716)
/// Mirrors <see cref="StormchasersTalentFactory"/> (the same Enchantment —
/// Class shell + <see cref="ClassState"/> side-table + sorcery-speed
/// level-up activated abilities), with Artist's Talent's three abilities:
///
/// - <b>Level 1 — rummage cast trigger</b> (CR 603.2): a
///   <see cref="TriggeredAbility"/> filtered to noncreature spells cast by
///   the controller (<see cref="Triggers.OnNonCreatureSpellCastByController"/>).
///   "You may discard a card. If you do, draw a card." v1 deterministic
///   "may" → always discards when the hand is non-empty (matches Faithless
///   Looting's deterministic v1 discard policy; opt-out awaits the agent
///   prompt surface). The discard happens first, then the conditional draw
///   (CR 701.16 discard → CR 121.1 draw) — if nothing was discarded (empty
///   hand) the "if you do" guard fails and no card is drawn. This is a
///   Level-1 ability so it is unconditional (no level gate — a Class enters
///   at level 1 with its level-1 ability active, CR 716.2).
///
/// - <b>Level 2 — cost reduction static</b> (CR 117.7): a
///   <see cref="SpellCostReductionAbility"/> ("Noncreature spells you cast
///   cost {1} less to cast"), gated on <see cref="ClassState.CurrentLevel"/>
///   &gt;= 2 inside its <c>reduction</c> delegate. Lives on the Class
///   permanent's ability list and is scanned by
///   <see cref="CostReduction.GetEffectiveCost"/> at cast time. Generic
///   mana only (CR 117.7c). Restricted to noncreature spells via the
///   predicate.
///
/// - <b>Level 3 — damage-increase replacement</b> (CR 614): a
///   <see cref="DamageIncreaseReplacement"/> (+2) registered against the
///   supplied <see cref="ReplacementBus"/>, gated on level &gt;= 3 AND
///   noncombat damage (<see cref="DamageIntent.IsCombatDamage"/> == false)
///   from a source the controller controls to an opponent or an opponent's
///   permanent. The source / target predicate is shared with Angrath's
///   Marauders (<see cref="AngrathsMaraudersFactory.SourceControlledBy"/> /
///   <see cref="AngrathsMaraudersFactory.TargetIsOpponentOrTheirPermanent"/>)
///   — same "a source you control … to an opponent or a permanent an
///   opponent controls" wording, only Artist's Talent adds the noncombat
///   gate and uses +2 instead of doubling.
///
/// ## Deferred (v1 gaps — shared with the Class family)
/// - <b>"You may" discard</b>: deterministic always-discard when the hand
///   is non-empty (same posture as Faithless Looting / Stormchaser's Talent
///   Level 3). Opt-out awaits the agent prompt surface.
/// - <b>CR 616 replacement ordering</b>: registration order wins when the
///   +2 stacks with other damage modifiers (Furnace of Rath, etc.) — same
///   deferral as the whole <see cref="DamageDoubleReplacement"/> family.
/// </summary>
[CardName("Artist's Talent")]
public static class ArtistsTalentFactory
{
    public const string CardName = "Artist's Talent";
    public const string PrintedManaCost = "{1}{R}";
    public const string Level2Cost = "{2}{R}";
    public const string Level3Cost = "{2}{R}";
    public const int DamageBonus = 2;

    /// <summary>
    /// Construct Artist's Talent with no live TriggerManager / EventBus /
    /// ReplacementBus wiring. The Level-1 rummage trigger + the two level-up
    /// activated abilities + the Level-2 cost-reduction static are all
    /// attached to the card for shape inspection. The Level-3 damage
    /// replacement needs a <see cref="ReplacementBus"/> — register it via
    /// <see cref="RegisterLevelThreeDamage"/> in the live game wiring.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Artist's Talent with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the Level-1 rummage trigger
    /// is registered for bus-driven firing. When <paramref name="eventBus"/>
    /// is supplied, level-up resolutions publish
    /// <see cref="ClassLevelUpEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Class });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Class state binder (CR 716). MaxLevel=3, per-level costs both
        // {2}{R}.
        // ----------------------------------------------------------------
        var classState = new ClassState(
            maxLevel: 3,
            levelUpCosts: new[]
            {
                ManaCost.Parse(Level2Cost),
                ManaCost.Parse(Level3Cost),
            });

        if (eventBus != null)
        {
            classState.OnLevelUp = (from, to) =>
                eventBus.Publish(new ClassLevelUpEvent(card, card.Controller ?? owner, from, to));
        }

        card.AttachClassState(classState);

        // ----------------------------------------------------------------
        // Level 1 — "Whenever you cast a noncreature spell, you may discard
        // a card. If you do, draw a card." (CR 603.2 — active from level 1,
        // no level gate.)
        // ----------------------------------------------------------------
        var rummageEffect = new Effect(
            $"{CardName}: Level 1 — you may discard a card; if you do, draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 701.16 — discard. v1 deterministic "may": always discard
                // when the hand is non-empty (last in hand, matching Faithless
                // Looting's policy). Empty hand → nothing discarded → "if you
                // do" guard fails, so no draw.
                var pick = controller.Zones.Hand.GetCards().LastOrDefault();
                if (pick == null) return;
                controller.Zones.Hand.RemoveCard(pick);
                controller.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);

                // CR 121.1 — "If you do, draw a card."
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var rummageTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnNonCreatureSpellCastByController(owner),
            effects: new IEffect[] { rummageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(rummageTrigger);
        triggers?.RegisterTriggeredAbility(rummageTrigger);

        // ----------------------------------------------------------------
        // Level-up activated abilities — CR 716.4 (sequential), sorcery
        // speed (CR 716.3). Mirrors StormchasersTalentFactory.
        // ----------------------------------------------------------------
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 2));
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 3));

        // ----------------------------------------------------------------
        // Level 2 — "Noncreature spells you cast cost {1} less to cast."
        // (CR 117.7) Static metadata scanned by CostReduction; gated on
        // ClassState.CurrentLevel >= 2 inside the reduction delegate.
        // ----------------------------------------------------------------
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => !c.HasType(CardType.Creature),
            reduction: (_, _) => classState.CurrentLevel >= 2 ? 1 : 0,
            description: "Noncreature spells you cast cost {1} less to cast. (Level 2)"));

        return card;
    }

    /// <summary>
    /// Register the Level-3 damage-increase replacement (CR 614) against
    /// <paramref name="replacements"/>. "If a source you control would deal
    /// noncombat damage to an opponent or a permanent an opponent controls,
    /// it deals that much damage plus 2 instead." Gated on:
    ///   1. Artist's Talent is on the battlefield AND at level &gt;= 3.
    ///   2. The damage is NONCOMBAT (<see cref="DamageIntent.IsCombatDamage"/>
    ///      == false).
    ///   3. <see cref="DamageIntent.Source"/> is controlled by the Class's
    ///      current controller.
    ///   4. The target is an opponent or a permanent an opponent controls.
    /// Predicates (3) + (4) are shared with Angrath's Marauders' identical
    /// "a source you control … to an opponent or their permanent" wording.
    /// Controller read live so control-change effects repoint the clause.
    /// </summary>
    public static void RegisterLevelThreeDamage(
        Enchantment card, ClassState classState, ReplacementBus replacements)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(classState);
        ArgumentNullException.ThrowIfNull(replacements);

        replacements.Register<DamageIntent>(new DamageIncreaseReplacement(
            predicate: intent =>
                card.Zone == ZoneType.Battlefield
                && classState.CurrentLevel >= 3
                && !intent.IsCombatDamage
                && AngrathsMaraudersFactory.SourceControlledBy(intent, card.Controller)
                && AngrathsMaraudersFactory.TargetIsOpponentOrTheirPermanent(intent, card.Controller),
            bonus: DamageBonus));
    }

    /// <summary>
    /// Build the "Level up to <paramref name="targetLevel"/>" sorcery-speed
    /// activated ability (CR 716.3 / 716.4). Mirrors
    /// <see cref="StormchasersTalentFactory"/>.
    /// </summary>
    private static ActivatedAbility BuildLevelUpAbility(
        Enchantment card, Player owner, ClassState classState, int targetLevel)
    {
        var cost = classState.CostFor(targetLevel);

        var effect = new Effect(
            $"{CardName}: level up to {targetLevel}",
            () => classState.LevelUpTo(targetLevel));

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(cost) },
            effects: new IEffect[] { effect },
            sorcerySpeed: true);
    }
}
