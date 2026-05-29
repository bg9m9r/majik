using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Malakir Rebirth // Malakir Mire (Zendikar Rising, {B}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "Choose target creature. You lose 2 life. Until end of turn, that
///    creature gains 'When this creature dies, return it to the battlefield
///    tapped under its owner's control.'"
///
/// Back face — <see cref="MalakirMireFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {B}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="AgadeemsAwakeningFactory"/> / <see cref="AgadeemTheUndercryptFactory"/>
/// and <see cref="ShatterskullSmashingFactory"/> /
/// <see cref="ShatterskullTheHammerPassFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>malakir-rebirth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time spell behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor the grant-a-dies-trigger
/// effect).
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{B}</c>, mono-black, owner / controller wired
///   (from JSON).
/// - <see cref="MdfcState"/> attached (front = "Malakir Rebirth",
///   back = "Malakir Mire"); starts on the front face.
/// - One 1..1 "target creature" <see cref="TargetRequest"/>.
/// - Resolution (CR 608.2):
///     <list type="bullet">
///       <item><b>You lose 2 life</b> (CR 119.3) — the caster loses 2 life
///         unconditionally, even if the target became illegal (the life
///         loss is not contingent on the grant — but per CR 608.2c a spell
///         with a single illegal target doesn't resolve at all; we still
///         model it tolerantly: if the target is illegal at resolution the
///         spell does nothing, matching the printed "Choose target
///         creature" single-target dependency, CR 608.2b/608.2c).</item>
///       <item><b>Grant the dies-trigger until end of turn</b> (CR 514.2 /
///         CR 613.1f) — register a self-sourced
///         <see cref="GrantAbilityEffect"/> on the target creature's
///         <see cref="Creature.ActiveEffects"/> that adds a
///         <see cref="TriggeredAbility"/> over <see cref="Triggers.OnDies"/>
///         (the same Battlefield → Graveyard self condition +
///         <c>activeZones = {Battlefield, Graveyard}</c> shape proven by
///         <see cref="Majik.Core.Keywords.PersistFactory"/>). On the
///         trigger's resolution the creature is returned from the graveyard
///         to the battlefield <b>tapped</b> under its owner's control
///         (CR 701.20 — not a -1/-1 counter, so it is NOT Persist).</item>
///     </list>
///
/// ## Why this is NOT Persist
///
/// Persist returns with a -1/-1 counter under the controller's control and
/// is a permanent intrinsic ability. Malakir Rebirth grants a one-shot,
/// until-end-of-turn "when this dies, return it tapped under its owner's
/// control" trigger with no counter — so the grant uses
/// <see cref="GrantAbilityEffect"/> (expiresAtEndOfTurn) rather than
/// <see cref="Majik.Core.Keywords.PersistFactory"/>, but reuses Persist's
/// validated death-trigger wiring (OnDies + {Battlefield, Graveyard}
/// active zones so the trigger survives the death zone-move).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real targeting prompt</b>: the live cast flow supplies the chosen
///   target through <see cref="ChosenSpellParams.Targets"/>; the resolver
///   maps the token to the live creature. Same posture as
///   <see cref="ApostlesBlessingFactory"/> / <see cref="AgadeemsAwakeningFactory"/>.
/// - <b>EOT-expiry of the granted trigger after a death</b>: the grant is
///   revoked at the cleanup step via
///   <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2). If
///   the creature dies before then the trigger fires; if it survives the
///   turn the grant is cleanly removed.
///
/// ## References
///
/// - <see cref="Majik.Core.Keywords.PersistFactory"/> — the death-trigger
///   wiring this factory mirrors (OnDies + dual active zones + graveyard →
///   battlefield return body).
/// - <see cref="ApostlesBlessingFactory"/> — EOT <see cref="GrantAbilityEffect"/>
///   registered on a target creature's <see cref="Creature.ActiveEffects"/>.
/// - <see cref="AgadeemsAwakeningFactory"/> — companion ZNR black MDFC
///   spell // land pair (JSON-loaded identity + code-attached MdfcState).
/// </summary>
[CardName("Malakir Rebirth")]
public static class MalakirRebirthFactory
{
    public const string CardName = "Malakir Rebirth";
    public const string BackName = "Malakir Mire";

    /// <summary>Life the caster loses on resolution (CR 119.3).</summary>
    public const int LifeLoss = 2;

    /// <summary>
    /// Construct Malakir Rebirth as an Instant (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("malakir-rebirth");
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name is observable from the front-face card object.
        card.MdfcState = new MdfcState(CardName, BackName);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "Choose target creature. You lose 2 life. Until
    /// end of turn, that creature gains 'When this creature dies, return it
    /// to the battlefield tapped under its owner's control.'"
    /// <see cref="SpellDefinition"/>.
    /// </summary>
    /// <param name="caster">Spell controller — loses 2 life on resolution
    /// (CR 119.3).</param>
    /// <param name="resolver">Target resolver — maps the chosen target token
    /// to the live game object (expected to be a <see cref="Creature"/> on
    /// the battlefield).</param>
    /// <param name="zoneService">Optional. When supplied the granted dies
    /// trigger's graveyard → battlefield return routes through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers fire (CR 603.6a).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: lose {LifeLoss} life; grant target creature a dies → return-tapped trigger EOT",
                        () => Resolve(caster, raw, resolver, zoneService)),
                };
            });
    }

    /// <summary>
    /// Resolve Malakir Rebirth against <paramref name="rawTarget"/>. Exposed
    /// for direct invocation by tests / bots without driving the full cast
    /// flow.
    /// </summary>
    /// <returns>The creature that received the grant, or <c>null</c> when the
    /// target was illegal at resolution (CR 608.2b/608.2c — clean no-op,
    /// including no life loss, since a single-target spell with its only
    /// target illegal doesn't resolve).</returns>
    public static Creature? Resolve(
        Player caster,
        object? rawTarget,
        Func<object, object> resolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        var live = rawTarget is null ? null : resolver(rawTarget);

        // CR 608.2b — target must still be a creature on the battlefield.
        if (live is not Creature creature || creature.Zone != ZoneType.Battlefield)
        {
            // CR 608.2c — a spell whose only target is illegal doesn't
            // resolve; none of its effects (including the life loss) happen.
            return null;
        }
        if (creature.ActiveEffects is null)
        {
            // Shape-only no-op: without a continuous-effects service we can't
            // register the EOT grant. Don't take the life payment either.
            return null;
        }

        // CR 119.3 — "You lose 2 life."
        caster.LoseLife(LifeLoss);

        // CR 514.2 / CR 613.1f — grant the dies-trigger until end of turn.
        var grant = new GrantAbilityEffect(
            source: creature,
            target: creature,
            ability: BuildReturnTappedDeathTrigger(creature, zoneService),
            expiresAtEndOfTurn: true);
        creature.ActiveEffects.Register(grant);
        // Sync immediately so the trigger is on the creature's Abilities list
        // (and thus observable by the TriggerManager) on the same priority
        // window (CR 117.5).
        grant.Sync();

        return creature;
    }

    /// <summary>
    /// Build the granted "When this creature dies, return it to the
    /// battlefield tapped under its owner's control" trigger.
    ///
    /// Mirrors <see cref="Majik.Core.Keywords.PersistFactory"/>'s validated
    /// death-trigger shape: <see cref="Triggers.OnDies"/> (Battlefield →
    /// Graveyard self) with <c>activeZones = {Battlefield, Graveyard}</c> so
    /// the trigger survives the death zone-move (ZoneService sets the card's
    /// zone before publishing the event, so a Battlefield-only active-zone
    /// set would not match at evaluation time).
    ///
    /// Differences from Persist (CR 702.79): no -1/-1 counter, returns under
    /// the OWNER's control (CR 109.5 / the printed "its owner's control"),
    /// and enters TAPPED (CR 701.20 / the printed "tapped").
    /// </summary>
    private static TriggeredAbility BuildReturnTappedDeathTrigger(
        Creature creature,
        ZoneService? zoneService)
    {
        var owner = creature.Owner
            ?? throw new InvalidOperationException("Malakir Rebirth target must have an owner.");
        var controller = creature.Controller ?? owner;

        var effect = new Effect(
            "Malakir Rebirth — return creature to battlefield tapped under owner's control",
            () =>
            {
                // Guard: creature must still be in the graveyard at resolution
                // (a later effect could have moved it elsewhere — CR 608.2).
                if (creature.Zone != ZoneType.Graveyard) return;

                var cardOwner = creature.Owner;
                if (cardOwner == null) return;

                // CR 701.20 — graveyard → battlefield under its owner's
                // control. ZoneService-routed when supplied so ETB triggers
                // fire (CR 603.6a); raw-zone fallback otherwise.
                Fx.ReturnFromGraveyardToBattlefield(creature, cardOwner, zoneService);

                // "tapped" (CR 701.20). Tap after the move so the permanent is
                // on the battlefield.
                if (creature.Zone == ZoneType.Battlefield)
                {
                    Fx.Tap(creature);
                }
            });

        return new TriggeredAbility(
            source: creature,
            controller: controller,
            condition: Triggers.OnDies(creature),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }
}
