using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Razorgrass Ambush // Razorgrass Field (Modern Horizons 3, {1}{W}).
///
/// Instant. Oracle text (front):
///   "Razorgrass Ambush deals 3 damage to target attacking or blocking
///    creature."
///
/// Back face — <see cref="RazorgrassFieldFactory"/> (Land — "As this land
/// enters, you may pay 3 life. If you don't, it enters tapped."
/// / "{T}: Add {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
/// Two-factory dispatch: casting the front face resolves "Razorgrass
/// Ambush" → this factory → an <see cref="Instant"/> with the combat-
/// damage effect. Playing the back face resolves "Razorgrass Field" →
/// <see cref="RazorgrassFieldFactory"/> → a painland-style <see cref="Land"/>.
///
/// ## Implemented (v1)
/// - Instant identity at {1}{W}, white (mono-W from the {W} pip), mana
///   value 2. Owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Razorgrass Ambush",
///   back = "Razorgrass Field"); starts on the front face.
/// - <b>"Target attacking or blocking creature"</b> — single 1..1
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>).
///   The candidate gatherer is supplied by the caller via
///   <paramref name="combatCreatureLookup"/>:
///     <list type="bullet">
///       <item>Production callers inject a delegate that reads
///         <see cref="CombatManager.CurrentCombat"/> and returns all
///         attacking creatures (from <see cref="Combat.Attackers"/>) plus
///         all blocking creatures (from <see cref="Combat.GetAllBlockers"/>).</item>
///       <item>Test callers inject the list directly — clean without
///         mocking a global registry.</item>
///       <item>When null is supplied the gatherer returns an empty list
///         (shape-only / dispatcher path — no live combat).</item>
///     </list>
///   CR 608.2b — at resolution the target is re-checked: if it is no
///   longer in combat (not on the battlefield, not a Creature) the effect
///   is a no-op.
/// - <b>Resolve — deal 3 damage</b> via <see cref="Fx.DealDamage"/> to
///   the chosen target creature. Routed through
///   <see cref="OracleSpellBinder.DealDamage"/> (Creature path). A
///   non-Creature or off-battlefield target at resolution is a legal
///   no-op (CR 608.2b — illegal target fizzle).
///
/// ## References
/// - MDFC factory pair: <see cref="SunderingEruptionFactory"/> /
///   <see cref="VolcanicFissureFactory"/> (PR #1039).
/// - Combat-creature injection: <see cref="SettleTheWreckageFactory"/>
///   (attackerLookup parameter).
/// - Painland-3 W back face: <see cref="SoporificSpringsFactory"/> /
///   <see cref="VolcanicFissureFactory"/> shape, mana swapped to {W}.
/// </summary>
[CardName("Razorgrass Ambush")]
public static class RazorgrassAmbushFactory
{
    public const string CardName = "Razorgrass Ambush";
    public const string BackName = "Razorgrass Field";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>
    /// Construct the front face of Razorgrass Ambush as an Instant with
    /// owner / controller wired and the <see cref="MdfcState"/> face
    /// tracker attached (starts on the front face).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Razorgrass Field) is observable from the
        // front-face card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for
    /// Razorgrass Ambush.
    ///
    /// CR 608.2b — illegal-target re-check at resolution: if the target
    /// is no longer a Creature on the battlefield the effect is a no-op.
    /// </summary>
    /// <param name="caster">Razorgrass Ambush's controller; used only for
    /// the effect label.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target
    /// token to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    /// <param name="combatCreatureLookup">Returns all attacking and
    /// blocking creatures currently in combat. Production callers wire this
    /// from <see cref="CombatManager.CurrentCombat"/>; test callers inject
    /// a list directly. Passing null or a delegate that returns null / empty
    /// is legal (shape-only / dispatcher path — no legal candidates).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Func<IReadOnlyList<Creature>>? combatCreatureLookup = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target attacking or blocking creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: all attacking and blocking creatures
                    // from the current combat. Injected by the caller so
                    // the factory remains testable without a live game loop.
                    CandidateGatherer: _ =>
                    {
                        if (combatCreatureLookup == null)
                            return Array.Empty<object>();

                        var pool = combatCreatureLookup() ?? Array.Empty<Creature>();
                        return pool.Cast<object>().ToList();
                    }),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: deal 3 damage to target attacking or blocking creature",
                        () => Resolve(resolved)),
                };
            });
    }

    // -------------------------------------------------------------------------
    // Resolution body
    // -------------------------------------------------------------------------

    private static void Resolve(object resolved)
    {
        // CR 608.2b — illegal-target re-check: only deal damage when the
        // resolved target is still a Creature on the battlefield.
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // CR 119 — deal 3 damage to the target creature.
        Fx.DealDamage(target, 3);
    }
}
