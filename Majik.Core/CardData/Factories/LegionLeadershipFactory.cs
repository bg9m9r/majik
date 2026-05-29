using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Legion Leadership // Legion Stronghold (Modern Horizons 3, {1}{R/W}).
///
/// Instant. Oracle text (front):
///   "Until end of turn, double target creature's power and it gains
///    first strike."
///
/// Back face — <see cref="LegionStrongholdFactory"/> (Land — "This land
/// enters tapped." / "{T}: Add {R} or {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
/// Two-factory dispatch: casting the front face resolves "Legion Leadership"
/// → this factory → an <see cref="Instant"/> with the power-double +
/// first-strike effect. Playing the back face resolves "Legion Stronghold"
/// → <see cref="LegionStrongholdFactory"/> → a simple tapland.
///
/// ## Implemented (v1)
/// - Instant identity at {1}{R/W} (hybrid pip), mana value 2. Colour
///   identity is red AND white (CR 107.4e hybrid pips contribute both
///   listed colours — same pattern as <see cref="BorosReckonerFactory"/>).
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Legion Leadership",
///   back = "Legion Stronghold"); starts on the front face.
/// - <b>"Double target creature's power"</b> — Layer 7c effect
///   (CR 613.4d): samples the target's current power X at the moment
///   the spell starts resolving, then registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> of +X/+0 (effectively ×2)
///   via the target's <see cref="Creature.ActiveEffects"/> service.
///   Same snapshot-at-resolution pattern as <see cref="BerserkFactory"/>.
/// - <b>"Gains first strike until end of turn"</b> — Layer 6 grant
///   (CR 613.1c) via <see cref="GrantKeywordUntilEndOfTurnEffect"/>
///   ("First strike"). Same pattern as <see cref="BorosCharmFactory"/>
///   mode 2 (double strike swap). CR 514.2 — both effects expire at
///   cleanup.
/// - <b>CR 608.2b guards</b>: non-Creature resolver result, off-
///   battlefield target, or missing <c>ActiveEffects</c> service → no-op.
///
/// ## Deferred (v1 gaps)
/// - The power-double effect adds +X/+0 where X = power at resolution,
///   which equals doubling when no other pump is present. True
///   "double power" (a multiplicative Layer 7c.i modifier) is not yet
///   supported by the engine's continuous-effects pipeline; this
///   additive approximation is functionally equivalent for all practical
///   uses and matches the BerserkFactory snapshot-pump pattern.
/// </summary>
[CardName("Legion Leadership")]
public static class LegionLeadershipFactory
{
    public const string CardName = "Legion Leadership";
    public const string BackName = "Legion Stronghold";
    public const string PrintedManaCost = "{1}{R/W}";

    /// <summary>Granted keyword — CR 702.7 First strike.</summary>
    public const string GrantedKeyword = "First strike";

    /// <summary>
    /// Construct the front face of Legion Leadership as an Instant with
    /// owner / controller wired and the <see cref="MdfcState"/> face
    /// tracker attached (starts on the front face). Suitable for
    /// identity / shape / dispatcher tests.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Legion Stronghold) is observable from the
        // front-face card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Legion
    /// Leadership.
    ///
    /// Single 1..1 "target creature" request. On resolution:
    /// <list type="number">
    ///   <item>CR 613.4d — sample current power X.</item>
    ///   <item>Layer 7c — +X/+0 EOT via
    ///     <see cref="PumpUntilEndOfTurnEffect"/>.</item>
    ///   <item>Layer 6 — First strike EOT via
    ///     <see cref="GrantKeywordUntilEndOfTurnEffect"/>.</item>
    /// </list>
    /// CR 608.2b — non-Creature or off-battlefield target → no-op.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target
    /// token to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

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
                    Intent: BotIntent.CombatTrick),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: double power and gain first strike until end of turn",
                        () => Resolve(resolved)),
                };
            });
    }

    // -------------------------------------------------------------------------
    // Resolution body
    // -------------------------------------------------------------------------

    private static void Resolve(object resolved)
    {
        // CR 608.2b — illegal target: only Creatures on the battlefield
        // with a live continuous-effects service are affected.
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 613.4d — "double target creature's power" samples X (the
        // creature's current power) at the moment Legion Leadership starts
        // resolving. Registers a Layer 7c +X/+0 pump via the target's own
        // ActiveEffects service (same snapshot pattern as BerserkFactory).
        var x = target.GetPower();
        if (x > 0)
        {
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, x, 0));
        }

        // CR 613.1c Layer 6 — grant First strike until end of turn.
        // CR 514.2 — expires at cleanup.
        // Same GrantKeywordUntilEndOfTurnEffect pattern as
        // BorosCharmFactory mode 2 (double-strike) and
        // TemurBattleRageFactory.
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedKeyword));
    }
}
