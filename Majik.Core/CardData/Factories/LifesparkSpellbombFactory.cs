using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lifespark Spellbomb (Fifth Dawn).
///
/// Artifact — {1}. Oracle text (Scryfall, verified 2026-06-23):
///   "{G}, Sacrifice this artifact: Until end of turn, target land becomes a
///    3/3 creature that's still a land.
///    {1}, Sacrifice this artifact: Draw a card."
///
/// ## Shape source
///
/// Card identity (name, {1}, Artifact) is loaded from
/// <c>Majik.Core/CardData/Cards/lifespark-spellbomb.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The two activated abilities are wired
/// in code below.
///
/// ## Implemented (v1)
/// - <b>{G}, Sacrifice: Until end of turn, target land becomes a 3/3 creature
///   that's still a land</b> — wired as an <see cref="ActivatedAbility"/> with
///   a <see cref="ManaCostCost"/>("{G}") plus <see cref="AdditionalCost"/>
///   .Sacrifice on the spellbomb itself. A single 1..1 "target land"
///   <see cref="TargetRequest"/> is declared so the activating player's agent
///   picks a land target at activation (CR 602.2b). On resolution the chosen
///   land is animated via the engine's generic
///   <see cref="AnimateLandEffect.Register"/> primitive (CR 613.1c / 701.59a —
///   adds Creature type at Layer 4, base P/T 3/3 at Layer 7b, while the
///   printed Land type stays — "still a land"), flagged
///   <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
///   <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
///   step) lifts the animation at end of turn. The animated body has no
///   creature subtype (the printed card is a typeless 3/3).
/// - <b>{1}, Sacrifice: Draw a card</b> — second
///   <see cref="ActivatedAbility"/> on the same card. <see cref="ManaCostCost"/>("{1}")
///   plus self-sacrifice; resolution moves the spellbomb to its owner's
///   graveyard and draws one card for the controller. Mirrors
///   <see cref="AetherSpellbombFactory"/> / <see cref="NecrogenSpellbombFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter the target to "is a Land" — the resolution-time guard handles
///   illegal targets (CR 608.2b — an effect with an illegal target does
///   nothing).
/// - <b>Combat math through Compute</b>: same gap as every manland —
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row for a Land runtime instance,
///   so the 3/3 is recorded for inspection (and surfaces through
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/>) but does not
///   fully wire through combat resolution yet.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op stub.
///   The effect closure performs the zone move so behavior is observable —
///   same posture as <see cref="AetherSpellbombFactory"/>. Remove the explicit
///   move-to-graveyard once <see cref="AdditionalCost.Pay"/> performs the
///   sacrifice itself.
/// </summary>
[CardName("Lifespark Spellbomb")]
public static class LifesparkSpellbombFactory
{
    public const string CardName = "Lifespark Spellbomb";
    public const string Slug = "lifespark-spellbomb";
    public const int AnimatedPower = 3;
    public const int AnimatedToughness = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Lifespark Spellbomb owned and controlled by
    /// <paramref name="owner"/>. Shape-only — no continuous-effects service or
    /// event bus, so the animate ability resolves to a no-op and the
    /// self-sacrifice cost publishes nothing (legacy posture). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — see <see cref="GhituEncampmentFactory"/>). The service is
    /// threaded into the {G} animate ability so its resolution registers the
    /// Layer 4 + Layer 7b continuous effects, and <c>effects.EventBus</c> is
    /// threaded into the self-sacrifice cost so paying it publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
    /// cost-payer.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var spellbomb = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        spellbomb.SetOwner(owner);
        spellbomb.SetController(owner);

        var eventBus = effects?.EventBus;

        // ----------------------------------------------------------------
        // {G}, Sacrifice this artifact: Until end of turn, target land
        // becomes a 3/3 creature that's still a land.
        //
        // CR 602 — activated ability with a single 1..1 "target land"
        // request. The resolution effect reads ChosenTargets and gates on
        // the Land type at resolution (CR 608.2b — illegal target → effect
        // does nothing). Animation is registered via the generic
        // AnimateLandEffect primitive (CR 613.1c / 701.59a), flagged
        // ExpiresAtEndOfTurn (CR 514.2).
        // ----------------------------------------------------------------
        ActivatedAbility? animateAbility = null;
        var animateEffect = new Effect(
            $"{CardName}: target land becomes a {AnimatedPower}/{AnimatedToughness} creature until EOT (still a land) + sac self",
            () =>
            {
                if (effects != null
                    && animateAbility != null
                    && animateAbility.ChosenTargets.Count > 0
                    && animateAbility.ChosenTargets[0].Count > 0
                    && animateAbility.ChosenTargets[0][0] is Land land)
                {
                    // CR 613.1c / 701.59a — add Creature type at Layer 4,
                    // set base P/T 3/3 at Layer 7b; printed Land type stays
                    // ("still a land"). No creature subtype (typeless 3/3),
                    // no Haste. Until-EOT (CR 514.2).
                    AnimateLandEffect.Register(
                        effects,
                        target: land,
                        subtype: null,
                        basePower: AnimatedPower,
                        baseToughness: AnimatedToughness,
                        grantsHaste: false,
                        expiresAtEndOfTurn: true);
                }

                SacrificeSelf(spellbomb, owner);
            });

        animateAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{G}"),
                AdditionalCost.Sacrifice(spellbomb, eventBus),
            },
            effects: new IEffect[] { animateEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        spellbomb.AddAbility(animateAbility);

        // ----------------------------------------------------------------
        // {1}, Sacrifice this artifact: Draw a card.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card + sac self",
            () =>
            {
                SacrificeSelf(spellbomb, owner);

                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty-library loss handled by SBAs
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Sacrifice(spellbomb, eventBus),
            },
            effects: new IEffect[] { drawEffect });

        spellbomb.AddAbility(drawAbility);

        return spellbomb;
    }

    /// <summary>
    /// Move the spellbomb from the battlefield to its owner's graveyard.
    /// Defensive against double-execution (idempotent if already
    /// sacrificed). Mirrors <see cref="AetherSpellbombFactory"/> — the
    /// generic <see cref="AdditionalCost.Pay"/> sacrifice path is a stub.
    /// </summary>
    private static void SacrificeSelf(Artifact spellbomb, Player owner)
    {
        if (spellbomb.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(spellbomb);
        owner.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);
    }
}
