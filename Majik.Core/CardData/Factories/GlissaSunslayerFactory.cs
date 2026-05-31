using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glissa Sunslayer (Phyrexia: All Will Be One,
/// {B}{G}).
///
/// Legendary Creature — Phyrexian Zombie Elf 3/3. Oracle text (verified
/// against Scryfall):
///   "First strike, deathtouch
///    Whenever Glissa Sunslayer deals combat damage to a player, choose one —
///    • You draw a card and lose 1 life.
///    • Destroy target enchantment.
///    • Remove up to three counters from target permanent."
///
/// The base shape (name, Legendary Creature, Phyrexian/Zombie/Elf subtypes,
/// {B}{G}, 3/3) is materialised from the embedded JSON definition
/// (<c>glissa-sunslayer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON declares no
/// abilities — the keyword markers and the modal combat-damage trigger are
/// layered on here (same posture as <see cref="GuttersnipeFactory"/>).
///
/// ## Implemented (v1)
/// - 3/3 Legendary Creature — Phyrexian Zombie Elf, mana cost {B}{G}, BG.
/// - <b>First strike (CR 702.7) + deathtouch (CR 702.2)</b>:
///   <see cref="KeywordAbility"/> markers read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/> (same
///   wiring as <see cref="PhyrexianCrusaderFactory"/>).
/// - <b>Combat-damage-to-a-player modal trigger (CR 510, CR 603.1,
///   CR 700.2d)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="CombatDamageDealtEvent"/> filtered to this source card and a
///   non-null <see cref="CombatDamageDealtEvent.TargetPlayer"/> (same
///   predicate as <see cref="RagavanNimblePilfererFactory"/>). The "choose
///   one" mode is resolved at resolution via
///   <see cref="IPlayerAgent.ChooseModeAsync"/> when an agent is registered,
///   else the captured <c>mode</c> parameter (same modal posture as
///   <see cref="CharmingPrinceFactory"/>).
///
/// ## Modes
/// - <b>Mode 0 — You draw a card and lose 1 life</b> (CR 121.1 / CR 119.3):
///   identical draw-then-lose body to
///   <see cref="ClingToDustFactory"/>'s "draw 1, lose 1" arm (empty library
///   flags <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> per
///   CR 704.5b; the life loss runs regardless of the draw).
/// - <b>Mode 1 — Destroy target enchantment</b> (CR 701.7): mirrors
///   <see cref="ThrabenCharmFactory"/>'s destroy-enchantment arm —
///   resolution-time legality re-check (CR 608.2b) then
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (indestructible / regeneration
///   gates apply).
/// - <b>Mode 2 — Remove up to three counters from target permanent</b>
///   (CR 122.5 / CR 121.6): removes up to three counters from the target,
///   draining counter types in the order they appear in
///   <see cref="CounterCollection.All"/>. "Up to three" means fewer-present
///   removes all (CR 122 — you can't remove a counter that isn't there).
///   v1 deterministically takes the highest-quantity-first ordering rather
///   than prompting the controller to pick which counters to remove — same
///   no-prompt posture as the modal arms of <see cref="WitherbloomCharmFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven "which counters to remove" prompt (mode 2)</b>: when
///   the target carries a mix of counter types, the choice of which up-to-3
///   to remove is deterministic (collection order), not agent-prompted. Real
///   matches almost always have a single counter type on the target, so this
///   is observationally identical there. Same queue as other "choose N
///   counters" effects.
/// </summary>
[CardName("Glissa Sunslayer")]
public static class GlissaSunslayerFactory
{
    public const string CardName = "Glissa Sunslayer";
    public const string Slug = "glissa-sunslayer";

    /// <summary>Mode index for "You draw a card and lose 1 life."</summary>
    public const int ModeDrawLose = 0;
    /// <summary>Mode index for "Destroy target enchantment."</summary>
    public const int ModeDestroyEnchantment = 1;
    /// <summary>Mode index for "Remove up to three counters from target permanent."</summary>
    public const int ModeRemoveCounters = 2;

    private const int MaxCountersRemoved = 3;
    private const int LifeLoss = 1;

    /// <summary>Printed mode labels, in oracle order (CR 700.2d).</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "You draw a card and lose 1 life.",
        "Destroy target enchantment.",
        "Remove up to three counters from target permanent.",
    };

    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Draw,     // draw a card (life loss is incidental).
        BotIntent.Removal,  // destroy enchantment.
        BotIntent.Removal,  // remove counters (e.g. shrink a +1/+1 threat / planeswalker).
    };

    /// <summary>
    /// Construct Glissa Sunslayer. The combat-damage modal trigger is
    /// attached for shape inspection; supplying a <see cref="TriggerManager"/>
    /// additionally registers it so a <see cref="CombatDamageDealtEvent"/>
    /// automatically queues the ability. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to (via the
    /// <paramref name="mode"/> default).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="mode">Pre-selected mode (0=draw+lose, 1=destroy
    /// enchantment, 2=remove counters). Overridden by a registered
    /// <see cref="IPlayerAgent"/> if one is present in
    /// <see cref="AgentRegistry"/>. Defaults to mode 0 (no target needed).</param>
    /// <param name="triggers">TriggerManager for bus-driven firing. May be
    /// null — the trigger is still attached to the card shape.</param>
    public static Creature Create(Player owner, int mode = ModeDrawLose, TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature, Phyrexian/Zombie/Elf subtypes, {B}{G}, 3/3). The JSON
        // carries no abilities — keywords + the modal trigger are layered on.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.7 — First strike. CR 702.2 — Deathtouch. Both are marker
        // keywords read by CombatAbilities during combat damage assignment.
        card.AddAbility(new KeywordAbility("First strike", card, owner));
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player modal trigger (CR 510, CR 603.1,
        // CR 700.2d).
        //   "Whenever Glissa Sunslayer deals combat damage to a player,
        //    choose one — ..."
        // The predicate captures the damaged player off the event (same
        // closure-capture as Ragavan) so mode 0's "you" / mode arms resolve
        // against the right state. Mode-1/2 declare a 0..1 target slot
        // (MinTargets=0 so the no-target mode 0 doesn't gate the trigger).
        // ----------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var effect = new Effect(
            $"{CardName}: choose one — draw + lose 1; destroy enchantment; remove up to 3 counters",
            async ctx =>
            {
                if (trigger == null) return;

                var controller = card.Controller ?? owner;
                var chosenMode = await PickModeAsync(controller, mode, ctx).ConfigureAwait(false);

                switch (chosenMode)
                {
                    case ModeDrawLose:
                        ExecuteDrawLose(controller);
                        break;
                    case ModeDestroyEnchantment:
                        ExecuteDestroyEnchantment(trigger);
                        break;
                    case ModeRemoveCounters:
                        ExecuteRemoveCounters(trigger);
                        break;
                }
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                return e.TargetPlayer != null;
            }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                // Shared 0..1 target slot for modes 1 (enchantment) and 2
                // (permanent). MinTargets=0 so mode 0 (no target) doesn't
                // gate the trigger (CR 700.2d — only the chosen mode's
                // targeting is relevant). Candidate gatherer offers any
                // battlefield permanent; resolution-time legality is
                // re-checked per mode (CR 608.2b).
                new TargetRequest(
                    Description: "target enchantment / target permanent",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    // ------------------------------------------------------------------
    // Mode resolution
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolve the mode to execute. Consults the registered agent's
    /// <see cref="IPlayerAgent.ChooseModeAsync"/> when available; falls back
    /// to the captured <paramref name="defaultMode"/> (same pattern as
    /// <see cref="CharmingPrinceFactory"/>).
    /// </summary>
    private static async ValueTask<int> PickModeAsync(Player controller, int defaultMode, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        if (agent == null) return defaultMode;

        try
        {
            var pick = await agent.ChooseModeAsync(ctx.Game!, modes: Modes, modeIntents: ModeIntents)
                .ConfigureAwait(false);

            if (pick >= 0 && pick < Modes.Count) return pick;
        }
        catch
        {
            // Agent hard-requires a non-null context or throws — fall back to
            // the deterministic default (same posture as CharmingPrinceFactory).
        }

        return defaultMode;
    }

    // ------------------------------------------------------------------
    // Mode 0 — You draw a card and lose 1 life
    // ------------------------------------------------------------------

    /// <summary>
    /// Mode 0 — "You draw a card and lose 1 life" (CR 121.1 / CR 119.3).
    /// Mirrors <see cref="ClingToDustFactory"/>'s draw-then-lose arm: an
    /// empty library flags the player for state-based loss (CR 704.5b) and
    /// the life loss runs regardless of whether the draw succeeded.
    /// </summary>
    private static void ExecuteDrawLose(Player controller)
    {
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top != null)
        {
            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
        else
        {
            controller.MarkTriedToDrawFromEmptyLibrary();
        }

        controller.LoseLife(LifeLoss);
    }

    // ------------------------------------------------------------------
    // Mode 1 — Destroy target enchantment
    // ------------------------------------------------------------------

    /// <summary>
    /// Mode 1 — "Destroy target enchantment" (CR 701.7). Mirrors
    /// <see cref="ThrabenCharmFactory"/>'s destroy-enchantment arm.
    /// </summary>
    private static void ExecuteDestroyEnchantment(TriggeredAbility trigger)
    {
        var chosen = trigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not ICard card) return;

        // CR 608.2b — target must still be an enchantment on the battlefield.
        if (!card.HasType(CardType.Enchantment)) return;
        if (card.Zone != ZoneType.Battlefield) return;

        // CR 701.7 — destroy. Indestructible (CR 702.12b) and regeneration
        // (CR 701.15) gates are handled by MoveToGraveyard(Destroy).
        OracleSpellBinder.MoveToGraveyard(card, ZoneMoveReason.Destroy);
    }

    // ------------------------------------------------------------------
    // Mode 2 — Remove up to three counters from target permanent
    // ------------------------------------------------------------------

    /// <summary>
    /// Mode 2 — "Remove up to three counters from target permanent"
    /// (CR 122.5 / CR 121.6). Removes up to <see cref="MaxCountersRemoved"/>
    /// counters from the target, draining counter types in the order they
    /// appear in <see cref="CounterCollection.All"/>. "Up to three" means a
    /// target with fewer than three counters simply loses all of them
    /// (CR 122 — you can't remove counters that aren't there).
    /// </summary>
    private static void ExecuteRemoveCounters(TriggeredAbility trigger)
    {
        var chosen = trigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Permanent permanent) return;

        // CR 608.2b — target must still be a permanent on the battlefield.
        if (permanent.Zone != ZoneType.Battlefield) return;

        var remaining = MaxCountersRemoved;

        // Snapshot the counter types before mutating (mutating the bag while
        // enumerating its backing dictionary would throw). v1 deterministic
        // ordering — see the class-level "Deferred" note re: agent prompt.
        var present = permanent.Counters.All
            .Where(kvp => kvp.Value > 0)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var type in present)
        {
            if (remaining <= 0) break;
            var have = permanent.Counters.Count(type);
            var take = Math.Min(have, remaining);
            permanent.Counters.Remove(type, take);
            remaining -= take;
        }
    }
}
