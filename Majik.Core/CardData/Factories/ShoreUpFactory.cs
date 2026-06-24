using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shore Up (Dominaria, {U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Target creature you control gets +1/+1 and gains hexproof until end of
///    turn. Untap it. (It can't be the target of spells or abilities your
///    opponents control.)"
///
/// ## Implemented (v1)
/// - Instant identity at {U} (blue, mana value 1), built from the embedded JSON
///   def (<c>shore-up.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="BuildDefinition"/> wires the resolve body: one 1..1
///   "target creature you control" <see cref="TargetRequest"/>. On resolution
///   (CR 608.2b — only a Creature still on the battlefield with a live
///   continuous-effects service is affected):
///   1. <b>+1/+1 until end of turn</b> — Layer 7c fixed pump (CR 613.4d) via
///      <see cref="PumpUntilEndOfTurnEffect"/>(+1, +1).
///   2. <b>gains hexproof until end of turn</b> — Layer 6 grant (CR 613.1c) of
///      "Hexproof" (CR 702.11b — "can't be the target of spells or abilities
///      your opponents control") via
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/>. The printed reminder
///      clause resolves through the engine's existing Hexproof handling in
///      <see cref="Majik.Core.Targeting.TargetLegality"/>.
///   3. <b>Untap it</b> — CR 701.21a; <see cref="Permanent.Untap"/> on the same
///      target. A no-op if the creature is already untapped.
/// - CR 514.2 — both continuous effects expire at cleanup; the untap is a
///   one-shot and does not need to be undone.
///
/// Mirrors the hexproof-grant + pump shape of
/// <see cref="VinesOfVastwoodFactory"/>, plus the resolve-time untap idiom of
/// the combat-trick / untap factories.
/// </summary>
[CardName("Shore Up")]
public static class ShoreUpFactory
{
    public const string CardName = "Shore Up";
    public const string Slug = "shore-up";
    public const string PrintedManaCost = "{U}";

    /// <summary>Granted keyword — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    /// <summary>Pump applied to the target (CR 613.4d).</summary>
    public const int PumpPower = 1;
    public const int PumpToughness = 1;

    /// <summary>
    /// Build Shore Up as an Instant from the embedded JSON def, with owner /
    /// controller wired. Suitable for identity / shape / dispatcher tests.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Shore Up.
    ///
    /// One 1..1 "target creature you control" request. On resolution the chosen
    /// target that is still a Creature on the battlefield (CR 608.2b) gets +1/+1
    /// and hexproof until end of turn, then is untapped (CR 701.21a).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.CombatTrick),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: +1/+1, gains hexproof until end of turn, untap it",
                        () => Resolve(targetResolver(raw))),
                };
            });
    }

    private static void Resolve(object resolved)
    {
        // CR 608.2b — illegal target: only a Creature on the battlefield with a
        // live continuous-effects service is affected.
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 613.4d Layer 7c — fixed +1/+1 until end of turn (CR 514.2 expiry).
        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));

        // CR 613.1c Layer 6 — grant Hexproof until end of turn (CR 702.11b).
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedHexproof));

        // CR 701.21a — "Untap it." One-shot; no-op if already untapped.
        if (target.IsTapped) target.Untap();
    }
}
