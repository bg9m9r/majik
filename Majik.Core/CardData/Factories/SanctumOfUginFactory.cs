using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sanctum of Ugin (Battle for Zendikar).
///
/// Land. Oracle text:
///   "{T}: Add {C}.
///    Whenever you cast a colorless spell with mana value 7 or greater,
///    you may sacrifice this land. If you do, search your library for a
///    colorless creature card, reveal it, put it into your hand, then
///    shuffle."
///
/// ## Implementation
///
/// - Land identity (no printed supertypes / subtypes — non-basic).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> producing
///   one generic mana (CR 605.1, same shape as
///   <see cref="EldraziTempleFactory"/>'s first ability).
/// - <b>Spell-cast trigger (CR 603.1)</b> over <see cref="SpellCastEvent"/>:
///   predicate is:
///     1. The spell's controller is Sanctum's controller ("you cast").
///     2. The spell is colourless —
///        <c>CardColors.GetColors(spell.Card).Count == 0</c> (CR 105;
///        empty colour-set = colourless). X in the cost is excluded from
///        coloured-pip count and does not affect this check.
///     3. The spell's mana value ≥ 7 —
///        <c>ManaCost.Parse(spell.Card.ManaCost).TotalValue >= 7</c>
///        (CR 202.3: mana value uses the printed mana cost's total value,
///        where X = 0 per CR 202.3e; but spells are on the stack, not in
///        other zones, so TotalValue is sufficient for the filter).
///   Trigger fires at most once per qualifying cast (CR 603.3).
/// - <b>Optional sac → tutor effect</b>: on resolve, the trigger body
///   consults <see cref="AgentRegistry"/> via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/> tagged
///   <see cref="BotIntent.Tutor"/>. Agent-less calls (no agent registered)
///   default to YES — the upside branch is always beneficial and mirrors
///   the auto-accept pattern of <see cref="MentorOfTheMeekFactory"/>.
///   When YES:
///     a. Sanctum is sacrificed (Battlefield → owner's Graveyard,
///        CR 701.16), using the same zone-move pattern as
///        <see cref="PrismaticVistaFactory.SacrificeToOwnersGraveyard"/>.
///     b. The controller's library is searched for the first card that
///        is both a Creature (CR 302) and colourless
///        (<c>CardColors.GetColors(c).Count == 0</c>). The agent may pick
///        via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>; deterministic
///        fallback takes the first eligible card (CR 701.19a).
///     c. The chosen card moves Library → Hand.
///     d. The library is shuffled via
///        <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a).
///   When NO (declined): Sanctum stays on the battlefield; no tutor.
///   Nothing else changes (CR 603.1 — the triggered ability resolves as
///   a do-nothing).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trigger attached, not
///   registered (no event bus). Suitable for identity / dispatcher tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. Trigger registered with <paramref name="triggers"/> so
///   <see cref="SpellCastEvent"/>s published on the bus route it to the
///   stack automatically.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the tutored card moves Library → Hand without
///   emitting a CardRevealedEvent. Same gap as Stoneforge Mystic / every
///   other tutor factory; unlock when the event is plumbed.
/// - <b>Mana-value X handling</b>: spells cast with an X-component treat
///   X as 0 for the printed-cost check. This is the engine-wide posture
///   (no "cast with X = N" annotation on the SpellCastEvent yet).
/// </summary>
[CardName("Sanctum of Ugin")]
public static class SanctumOfUginFactory
{
    public const string CardName = "Sanctum of Ugin";

    /// <summary>
    /// Construct Sanctum of Ugin with no live event-bus / trigger wiring.
    /// The cast trigger is attached but not registered. Suitable for
    /// identity / dispatcher / shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Sanctum of Ugin with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the cast trigger is
    /// registered so <see cref="SpellCastEvent"/>s published on the bus
    /// automatically route it to the stack.
    /// </summary>
    public static Land Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities don't use the stack. {C} folds into
        // the generic bucket per ManaCost.Parse (ManaCost.cs:170).
        // Same shape as EldraziTempleFactory's first ability.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // "Whenever you cast a colorless spell with mana value 7 or
        //  greater, you may sacrifice this land. If you do, search your
        //  library for a colorless creature card, reveal it, put it into
        //  your hand, then shuffle."
        //
        // CR 603.1 — triggered ability on SpellCastEvent.
        // Predicate:
        //   1. Controller match ("you cast").
        //   2. Colourless spell — CardColors.GetColors(card).Count == 0.
        //   3. Mana value ≥ 7 — ManaCost.Parse(manaCost).TotalValue >= 7.
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var liveController = land.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
                return false;

            var spellCard = e.Spell.Card;

            // Colourless check — CR 105: a card with no coloured pips is
            // colourless. CardColors.GetColors returns an empty set for
            // purely generic / {C} costs.
            if (CardColors.GetColors(spellCard).Count != 0)
                return false;

            // Mana-value check — CR 202.3: the mana value of a spell on
            // the stack uses the printed cost (X = 0 per CR 202.3e when
            // the MV is used as a characteristic filter as here — the
            // trigger condition evaluates at the point the spell is put
            // onto the stack, not after payment).
            var manaCostStr = spellCard.ManaCost;
            if (string.IsNullOrEmpty(manaCostStr)) return false;
            var mv = ManaCost.Parse(manaCostStr).TotalValue;
            return mv >= 7;
        });

        var castEffect = new Effect(
            $"{CardName}: you may sacrifice to tutor a colorless creature to hand",
            async ctx =>
            {
                var controller = land.Controller ?? owner;
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);

                // "You may sacrifice this land. If you do, ..."
                // Default: YES — tutoring is strictly card-advantageous
                // (same auto-accept posture as MentorOfTheMeek).
                bool sacrifice = agent == null
                    ? true
                    : (await agent.ChooseYesNoAsync(
                        $"Sacrifice {CardName} to search for a colorless creature?",
                        BotIntent.Tutor).ConfigureAwait(false));

                if (!sacrifice) return;

                // If we chose to sacrifice, do so only if still on the
                // battlefield (guard against stale events or zone
                // manipulation by another effect).
                if (land.Zone != ZoneType.Battlefield) return;
                SacrificeToOwnersGraveyard(land);

                // Search library for a colourless creature card (CR 302 /
                // CR 105). Consult the agent; fall back to first candidate.
                var candidates = controller.Zones.Library.GetCards()
                    .Where(c => c.HasType(CardType.Creature)
                                && CardColors.GetColors(c).Count == 0)
                    .ToList();

                if (candidates.Count == 0)
                {
                    // CR 701.19a — no candidate is legal; still shuffle
                    // (CR 701.20a) because the search occurred.
                    LibraryShuffle.ShuffleLibrary(controller, "sanctum-of-ugin");
                    return;
                }

                ICard? pick = agent != null
                    ? (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                            candidates: candidates,
                            kindLabel: "colorless creature card").ConfigureAwait(false))
                    : candidates[0];

                if (pick != null)
                {
                    controller.Zones.Library.RemoveCard(pick);
                    controller.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }

                // CR 701.20a — shuffle after the search resolves.
                LibraryShuffle.ShuffleLibrary(controller, "sanctum-of-ugin");
            });

        var castTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return land;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="self"/> from the battlefield to its
    /// owner's graveyard (sacrifice). Mirrors
    /// <see cref="PrismaticVistaFactory"/>'s helper.
    /// </summary>
    private static void SacrificeToOwnersGraveyard(Land self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }
}
