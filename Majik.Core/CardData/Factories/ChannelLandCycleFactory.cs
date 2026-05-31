using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the Kamigawa: Neon Dynasty
/// legendary-land Channel cycle (CR 702.74).
///
/// Each member shares the same shape — a legendary Land with
/// <c>{T}: Add {color}</c> + a Channel activated ability whose cost is
/// <c>&lt;mana&gt; + Discard this card</c>. Only the produced colour, the
/// Channel mana cost, and the Channel effect body differ across members,
/// so one factory class handles the cycle:
/// <code>
/// [CardName("Otawara, Soaring City",          "U", "2U", "bounce-nonland")]
/// [CardName("Eiganjo, Seat of the Empire",    "W", "1W", "destroy-attacking-blocking")]
/// [CardName("Takenuma, Abandoned Mire",       "B", "2B", "dig-4-creature-pw")]
/// [CardName("Sokenzan, Crucible of Defiance", "R", "2R", "two-spirit-haste-tokens")]
/// </code>
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = produced mana colour (single-letter Scryfall code)</c>,
/// <c>[2] = Channel mana cost (e.g. "2U")</c>,
/// <c>[3] = effect tag</c> (one of: <c>bounce-nonland</c>,
/// <c>destroy-attacking-blocking</c>, <c>dig-4-creature-pw</c>).
///
/// ## Channel — activated-from-hand surface (CR 702.74)
///
/// Channel is an activated ability the player activates while the card is
/// in their hand. The Channel cost includes "Discard this card" so the
/// card moves Hand → Graveyard as part of paying costs, then the
/// Channel-effect body resolves. The existing engine already supports
/// this end-to-end without a dedicated "active zones" flag on
/// <see cref="ActivatedAbility"/>:
///
/// - <see cref="DiscardSelfCost"/> gates payment to the controller's
///   <see cref="ZoneType.Hand"/> (CR 702.74a) — activation outside the
///   hand fails at cost-payment time.
/// - <see cref="Rules.ActionValidator.ValidateActivateAbility"/> does
///   NOT introspect the source's zone for activations, so non-battlefield
///   activations are not blocked at the validator surface.
/// - <see cref="Services.AbilityActivator"/> doesn't gate on the source
///   zone either — it puts the ability on the stack after costs are paid.
///
/// The combination is exactly Channel: the discard-self cost provides the
/// "in hand" gate; everything else is the standard activated-ability
/// pipeline. No new surface needed.
///
/// ## Implemented (v1)
/// - Land identity (Legendary, no subtype) + correct printed name per
///   cycle member.
/// - <c>{T}: Add {color}</c> mana ability (CR 605 mana ability — never goes
///   on the stack).
/// - Channel activated ability with <see cref="ManaCostCost"/> +
///   <see cref="DiscardSelfCost"/>; targets per cycle member.
/// - Otawara — 1..1 <c>target nonland permanent</c> TargetRequest;
///   resolves to <see cref="Primitives.Fx.BounceToHand"/>.
/// - Eiganjo — 1..1 <c>target attacking or blocking creature</c>
///   TargetRequest; resolves to <see cref="Primitives.Fx.MoveToGraveyard"/>
///   with <see cref="ZoneMoveReason.Destroy"/>. The "attacking or blocking"
///   gate is structural on the request — combat-state predicate gating
///   is checked at resolve.
/// - Takenuma — no targets; on resolve, looks at top 4 of controller's
///   library and consults the registered agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) to move one
///   creature or planeswalker card to hand; the rest go to graveyard.
///   Falls back to the first matching card when no agent is registered.
/// - Sokenzan — no targets; on resolve, creates two 1/1 red Spirit
///   creature tokens with haste under the controller via
///   <see cref="Tokens.TokenFactory.CreateOnBattlefield"/>
///   (<see cref="CardSubtype.Spirit"/> + <c>"Haste"</c> keyword stamp;
///   <see cref="ManaColor.Red"/> colour identity). Per-token routing
///   through <see cref="ZoneService"/> is deferred — the v1 factory
///   does not have a <c>ZoneService</c> reference at activation time
///   (mirrors Takenuma's raw-zone resolve body).
///
/// ## Deferred (v1 gaps)
/// - Eiganjo combat-state target gate: live "attacking or blocking" set
///   filtering at resolve is permissive (any creature passed in
///   <see cref="ChosenTargets"/> is destroyed). Combat-state predicates
///   land alongside the targeting-system upgrade.
/// - Takenuma "may" rider on the creature/planeswalker pick — v1
///   auto-accepts (same simplification as Sneak Attack / Through the
///   Breach).
///
/// ## Cycle members not shipped here
/// - Boseiju, Who Endures — shipped via the JSON-driven
///   <see cref="BoseijuFactory"/> (different effect body — destroy
///   target artifact / enchantment / nonbasic land).
/// </summary>
[CardName("Otawara, Soaring City",          "U", "2U", "bounce-nonland")]
[CardName("Eiganjo, Seat of the Empire",    "W", "1W", "destroy-attacking-blocking")]
[CardName("Takenuma, Abandoned Mire",       "B", "2B", "dig-4-creature-pw")]
[CardName("Sokenzan, Crucible of Defiance", "R", "2R", "two-spirit-haste-tokens")]
public static class ChannelLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Otawara, Soaring City.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Otawara, Soaring City", "U", "2U", "bounce-nonland" });

    /// <summary>
    /// Construct the Channel land identified by <paramref name="args"/>.
    /// </summary>
    /// <param name="owner">The player who owns the land.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c>,
    /// <c>[1] = produced mana colour (single-letter)</c>,
    /// <c>[2] = Channel mana cost (e.g. "2U")</c>,
    /// <c>[3] = effect tag</c>.
    /// </param>
    public static Land Create(Player owner, string[] args)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 4)
        {
            throw new ArgumentException(
                $"ChannelLandCycleFactory needs args = [name, color, channelCost, effectTag] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var color = args[1];
        var channelCost = args[2];
        var effectTag = args[3];

        var land = new Land(cardName, supertypes: new[] { CardSupertype.Legendary }, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // {T}: Add {color}. CR 605 mana ability (never goes on the stack).
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(color)));

        // Channel — <channelCost>, Discard this card: <effect>.
        AttachChannelAbility(land, owner, channelCost, effectTag);

        return land;
    }

    /// <summary>
    /// Build the Channel activated ability — shared cost shape (mana +
    /// discard-self) + a per-effect-tag resolve body. The ability is
    /// stitched together with a forward declaration of <c>channel</c> so
    /// the resolve closure can read <see cref="ActivatedAbility.ChosenTargets"/>
    /// for target-bearing effects (same pattern as
    /// <see cref="NihilSpellbombFactory"/>).
    /// </summary>
    private static void AttachChannelAbility(Land land, Player controller, string channelCost, string effectTag)
    {
        ActivatedAbility? channel = null;
        var (effect, targetRequests) = effectTag switch
        {
            "bounce-nonland" => BuildBounceNonland(land, () => channel!),
            "destroy-attacking-blocking" => BuildDestroyAttackingBlocking(land, () => channel!),
            "dig-4-creature-pw" => BuildDigForCreatureOrPlaneswalker(land, controller),
            "two-spirit-haste-tokens" => BuildTwoSpiritHasteTokens(land, controller),
            _ => throw new ArgumentException(
                $"ChannelLandCycleFactory: unknown effect tag '{effectTag}'.",
                nameof(effectTag)),
        };

        channel = new ActivatedAbility(
            source: land,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost(channelCost),
                new DiscardSelfCost(land),
            },
            effects: new IEffect[] { effect },
            targetRequests: targetRequests);

        land.AddAbility(channel);
    }

    // -----------------------------------------------------------------------
    // Per-cycle-member effect builders
    // -----------------------------------------------------------------------

    /// <summary>
    /// Otawara — "Return target nonland permanent to its owner's hand."
    /// 1..1 TargetRequest for "target nonland permanent"; on resolve,
    /// <see cref="Primitives.Fx.BounceToHand"/> moves it to its owner's
    /// hand (CR 701.20).
    /// </summary>
    private static (IEffect effect, TargetRequest[] requests) BuildBounceNonland(
        Land land, Func<ActivatedAbility> abilityAccessor)
    {
        var requests = new[]
        {
            new TargetRequest(
                Description: "target nonland permanent",
                MinTargets: 1,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Bounce),
        };

        var effect = new Effect(
            $"{land.Name} (Channel): return target nonland permanent to owner's hand",
            () =>
            {
                var ability = abilityAccessor();
                if (ability.ChosenTargets.Count == 0 || ability.ChosenTargets[0].Count == 0) return;
                if (ability.ChosenTargets[0][0] is not ICard target) return;
                if (target.HasType(CardType.Land)) return; // nonland gate (CR 608.2b — illegal target → no effect)
                Primitives.Fx.BounceToHand(target);
            });

        return (effect, requests);
    }

    /// <summary>
    /// Eiganjo — "Destroy target attacking or blocking creature."
    /// 1..1 TargetRequest; on resolve,
    /// <see cref="Primitives.Fx.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7). Combat-state
    /// gating is deferred — v1 destroys any creature passed in.
    /// </summary>
    private static (IEffect effect, TargetRequest[] requests) BuildDestroyAttackingBlocking(
        Land land, Func<ActivatedAbility> abilityAccessor)
    {
        var requests = new[]
        {
            new TargetRequest(
                Description: "target attacking or blocking creature",
                MinTargets: 1,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal),
        };

        var effect = new Effect(
            $"{land.Name} (Channel): destroy target attacking or blocking creature",
            () =>
            {
                var ability = abilityAccessor();
                if (ability.ChosenTargets.Count == 0 || ability.ChosenTargets[0].Count == 0) return;
                if (ability.ChosenTargets[0][0] is not Creature creature) return;
                Primitives.Fx.MoveToGraveyard(creature, ZoneMoveReason.Destroy);
            });

        return (effect, requests);
    }

    /// <summary>
    /// Takenuma — "Look at the top four cards of your library. Put one
    /// creature or planeswalker card from among them into your hand and
    /// the rest into your graveyard." No targets. Auto-pick via the
    /// registered agent (falls back to first match).
    /// </summary>
    private static (IEffect effect, TargetRequest[] requests) BuildDigForCreatureOrPlaneswalker(
        Land land, Player controller)
    {
        var effect = new Effect(
            $"{land.Name} (Channel): look at top 4, creature/planeswalker → hand, rest → graveyard",
            async ctx =>
            {
                var top4 = controller.Zones.Library.GetCards().Take(4).ToList();
                if (top4.Count == 0) return;

                var eligible = top4.Where(c =>
                    c.HasType(CardType.Creature) || c.HasType(CardType.Planeswalker)).ToList();

                ICard? pick = null;
                if (eligible.Count > 0)
                {
                    var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                    pick = agent != null
                        ? (await agent.ChooseLibraryPickAsync( ctx: ctx.Game, eligible, "creature or planeswalker card").ConfigureAwait(false))
                        : eligible[0];
                }

                foreach (var card in top4)
                {
                    controller.Zones.Library.RemoveCard(card);
                    if (ReferenceEquals(card, pick))
                    {
                        controller.Zones.Hand.AddCard(card);
                        card.SetZone(ZoneType.Hand);
                    }
                    else
                    {
                        controller.Zones.Graveyard.AddCard(card);
                        card.SetZone(ZoneType.Graveyard);
                    }
                }
            });

        return (effect, Array.Empty<TargetRequest>());
    }

    /// <summary>
    /// Sokenzan, Crucible of Defiance — "Create two 1/1 red Spirit
    /// creature tokens with haste." No targets. On resolve creates two
    /// independent Spirit tokens under <paramref name="controller"/> via
    /// <see cref="Tokens.TokenFactory.CreateOnBattlefield"/> (CR 111.1 /
    /// CR 111.6 — token enters as a new object, sickness flag stamped by
    /// TokenFactory; Haste lifts the can't-attack rider per CR 702.10c).
    /// </summary>
    private static (IEffect effect, TargetRequest[] requests) BuildTwoSpiritHasteTokens(
        Land land, Player controller)
    {
        var effect = new Effect(
            $"{land.Name} (Channel): create two 1/1 red Spirit creature tokens with haste",
            () =>
            {
                // Two independent token instances — each token is a
                // distinct game object (CR 111.1) so a Doubling Season /
                // Parallel Lives doubler stacks per-creation event.
                for (var i = 0; i < 2; i++)
                {
                    Majik.Core.Tokens.TokenFactory.CreateOnBattlefield(
                        new Majik.Core.Tokens.TokenFactory.TokenSpec(
                            Name: "Spirit",
                            Power: 1,
                            Toughness: 1,
                            Subtypes: new[] { CardSubtype.Spirit },
                            Keywords: new[] { "Haste" },
                            Colors: new[] { ManaColor.Red }),
                        controller);
                }
            });

        return (effect, Array.Empty<TargetRequest>());
    }
}
