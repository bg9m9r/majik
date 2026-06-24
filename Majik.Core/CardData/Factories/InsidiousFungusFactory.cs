using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Insidious Fungus (Modern Horizons 3, {G}).
///
/// Creature — Fungus 1/2. Oracle text (verified against Scryfall 2026-06-24):
///   "{2}, Sacrifice this creature: Choose one —
///     • Destroy target artifact.
///     • Destroy target enchantment.
///     • Draw a card. Then you may put a land card from your hand onto the
///       battlefield tapped."
///
/// ## Shape source
/// Card identity (name, {G}, 1/2, Creature — Fungus) is loaded from the
/// embedded JSON definition (<c>insidious-fungus.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three modal activated
/// abilities are attached in code below.
///
/// ## Implemented (v1)
/// - <b>"{2}, Sacrifice this creature: Choose one —" activated ability</b>
///   (CR 602, CR 700.2): modelled as THREE separate
///   <see cref="ActivatedAbility"/>s sharing the same cost shape
///   (<c>{2}</c> + sacrifice self). Same v1 pattern as
///   <see cref="GoblinCratermakerFactory"/> / <see cref="UmezawasJitteFactory"/>
///   — each "mode" is a standalone activation. The activating player picks
///   which activation to use (= which mode); CR 700.2's "each mode at most
///   once per activation" is trivially satisfied because each activation
///   triggers exactly one of the three abilities. (The engine's
///   <see cref="ActivatedAbility"/> has no per-ability modal-choice prompt;
///   splitting modes into separate abilities is the established idiom.)
///   - <b>Mode A — Destroy target artifact</b>: 1..1 "target artifact"
///     <see cref="TargetRequest"/>. Resolution-time legality (CR 608.2b):
///     target must still be an artifact permanent on the battlefield, then
///     <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///     <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) — Indestructible
///     (CR 702.12) / regeneration (CR 701.15) honoured via the Destroy reason.
///   - <b>Mode B — Destroy target enchantment</b>: identical shape over
///     "target enchantment".
///   - <b>Mode C — Draw a card, then you may put a land from hand onto the
///     battlefield tapped</b>: no target. Draws one card (CR 121.1) via
///     <see cref="Fx.DrawCards"/> (per-draw replacement bus; empty library
///     stamps the SBA loss flag — CR 704.5b), then runs the optional
///     "you may put a land card from your hand onto the battlefield tapped"
///     rider (CR 305.9 / 113.6c — putting a land onto the battlefield is NOT
///     a land drop, so it never touches <see cref="Majik.Core.Game.LandDropTracker"/>),
///     cribbed from <see cref="ArborealGrazerFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: same gap as Goblin Cratermaker /
///   the spellbomb family — the generic <see cref="AdditionalCost"/> sacrifice
///   payment is a no-op stub, so the effect closure performs the zone move via
///   <see cref="SacrificeSelf"/> so behaviour is observable. The
///   effects-aware overload threads the <c>EventBus</c> so the sacrifice
///   publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
/// - <b>Mode C land-play through ZoneService</b>: the production
///   (<c>ContinuousEffectsService</c>) build supplies no
///   <see cref="ZoneService"/>, so the played land's Hand → Battlefield move
///   uses a raw zone move (ETB triggers / replacements on that land don't
///   fire) — same posture as Arboreal Grazer's shape path.
/// - <b>"You may" auto-accepts</b> Mode C's land play when no agent is
///   registered — consistent with the optional-ETB factory family.
/// </summary>
[CardName("Insidious Fungus")]
public static class InsidiousFungusFactory
{
    public const string CardName = "Insidious Fungus";
    public const string Slug = "insidious-fungus";

    /// <summary>CR 121.1 — Mode C draws one card.</summary>
    public const int DrawAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Shape-only build (no event bus). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to for shape / dispatcher
    /// tests — the self-sacrifice publishes nothing.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload). Threads <c>effects.EventBus</c> into the self-sacrifice cost
    /// so paying it publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a) crediting the cost-payer.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into all three modes' self-sacrifice <see cref="AdditionalCost"/>
    /// + the resolve-path <see cref="SacrificeSelf"/> fallback so the sacrifice
    /// publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a). Null
    /// preserves the publish-nothing posture.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        AttachMode(card, owner, eventBus, CardType.Artifact, "target artifact");
        AttachMode(card, owner, eventBus, CardType.Enchantment, "target enchantment");
        AttachDrawLandMode(card, owner, eventBus);

        return card;
    }

    /// <summary>
    /// Attach a "{2}, Sacrifice this creature: Destroy target &lt;type&gt;"
    /// mode (Mode A / Mode B). On resolution the chosen target is destroyed
    /// (CR 701.7) iff it is still a permanent of <paramref name="requiredType"/>
    /// on the battlefield (CR 608.2b), then the source is sacrificed.
    /// </summary>
    private static void AttachMode(
        Creature card, Player owner, IEventBus? eventBus,
        CardType requiredType, string targetDescription)
    {
        ActivatedAbility? ability = null;
        var effect = new Effect(
            $"{CardName} — destroy {targetDescription} + sac self",
            () =>
            {
                if (ability != null
                    && ability.ChosenTargets.Count > 0
                    && ability.ChosenTargets[0].Count > 0
                    && ability.ChosenTargets[0][0] is Permanent target
                    && target.Zone == ZoneType.Battlefield
                    && target.HasType(requiredType))
                {
                    // CR 701.7 — Destroy. Indestructible (CR 702.12) /
                    // regeneration (CR 701.15) honoured via the Destroy reason;
                    // Insidious Fungus does not print "can't be regenerated".
                    Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                }

                SacrificeSelf(card, owner, eventBus);
            });

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Sacrifice(card, eventBus),
            },
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: targetDescription,
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(ability);
    }

    /// <summary>
    /// Attach Mode C — "{2}, Sacrifice this creature: Draw a card. Then you
    /// may put a land card from your hand onto the battlefield tapped." No
    /// target. Draws one card (CR 121.1), runs the optional land play, then
    /// sacrifices the source.
    /// </summary>
    private static void AttachDrawLandMode(Creature card, Player owner, IEventBus? eventBus)
    {
        var effect = new Effect(
            $"{CardName} — draw a card, may put a land from hand tapped + sac self",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 121.1 — draw one. Per-draw replacement bus; empty library
                // stamps the SBA loss flag (CR 704.5b).
                Fx.DrawCards(controller, DrawAmount);

                // "Then you may put a land card from your hand onto the
                // battlefield tapped." CR 305.9 / 113.6c — this is NOT a land
                // drop. No ZoneService is threaded through the activated-ability
                // resolve, so the move uses the raw fallback (same posture as
                // Arboreal Grazer's shape path).
                await PutLandFromHandTappedAsync(controller, ctx).ConfigureAwait(false);

                SacrificeSelf(card, owner, eventBus);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Sacrifice(card, eventBus),
            },
            effects: new IEffect[] { effect });

        card.AddAbility(ability);
    }

    /// <summary>
    /// Resolve the "you may put a land card from your hand onto the
    /// battlefield tapped" rider. Candidate set = every land card in
    /// <paramref name="player"/>'s hand. Consults the agent for the optional
    /// opt-in and the which-land pick; deterministic first-land fallback when
    /// no agent is registered. Cribbed from <see cref="ArborealGrazerFactory"/>.
    /// </summary>
    private static async ValueTask PutLandFromHandTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Hand.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();
        if (candidates.Count == 0) return; // No lands → "may" no-op.

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        // "You may" — CR 117.1a optional gesture. No-agent fallback auto-accepts.
        if (agent != null)
        {
            var optIn = await agent.ChooseYesNoAsync(
                    "Put a land card from your hand onto the battlefield tapped?",
                    BotIntent.Ramp).ConfigureAwait(false);
            if (!optIn) return;
        }

        ICard? land;
        if (agent != null)
        {
            land = await agent.ChooseFromHandAsync(player, candidates, BotIntent.Ramp).ConfigureAwait(false);
            // Re-validate at resolution (CR 608.2b).
            if (land == null || !candidates.Contains(land)) return;
        }
        else
        {
            land = candidates[0];
        }

        // Hand → Battlefield (raw move — no ZoneService in the activated-ability
        // resolve path). CR 701.18 — apply the printed "tapped" rider after.
        player.Zones.Hand.RemoveCard(land);
        player.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.SetController(player);
        if (land is Permanent perm && !perm.IsTapped) perm.Tap();
    }

    /// <summary>
    /// Move <paramref name="fungus"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield. When
    /// <paramref name="eventBus"/> is supplied the move routes through
    /// <see cref="Fx.Sacrifice(ICard, Player, IEventBus)"/>, publishing a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a). In the live
    /// activation path the cost already moved + published, so this no-ops.
    /// </summary>
    private static void SacrificeSelf(Creature fungus, Player owner, IEventBus? eventBus)
    {
        if (fungus.Zone != ZoneType.Battlefield) return;

        if (eventBus != null)
        {
            Fx.Sacrifice(fungus, fungus.Controller ?? owner, eventBus);
            return;
        }

        owner.Zones.Battlefield.RemoveCard(fungus);
        owner.Zones.Graveyard.AddCard(fungus);
        fungus.SetZone(ZoneType.Graveyard);
    }
}
