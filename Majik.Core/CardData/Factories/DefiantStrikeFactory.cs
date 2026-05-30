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
/// Named-card factory for Defiant Strike (Fate Reforged, {W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature gets +1/+0 until end of turn.
///    Draw a card."
///
/// ## Implementation
///
/// A cantrip combat trick — the +X/+0 pump half mirrors
/// <see cref="GiantGrowthFactory"/> (a single 1..1 "target creature"
/// <see cref="TargetRequest"/> that registers a
/// <see cref="PumpUntilEndOfTurnEffect"/> on resolution) reduced to +1/+0,
/// with a "draw a card" rider sharing the same simple top-of-library draw
/// the cantrip spells already use (e.g. <see cref="OptFactory"/>'s draw step).
///
/// Card shape comes from the embedded JSON (<c>defiant-strike.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema) — same posture as
/// <see cref="PlayWithFireFactory"/>.
///
/// On resolution (CR 608.2e — left-to-right clause ordering):
///   1. "Target creature gets +1/+0 until end of turn." When the target is
///      still a <see cref="Creature"/> on the battlefield (CR 608.2b — an
///      illegal pump target no-ops), register a
///      <see cref="PumpUntilEndOfTurnEffect"/>(+1, +0) on its
///      <see cref="Creature.ActiveEffects"/> (CR 613.1g layer 7c; CR 514.2 —
///      expires in cleanup).
///   2. "Draw a card." The caster draws the top card of their library
///      (CR 121.1). An empty library flags the caster for the SBA-driven
///      draw-from-empty loss (CR 704.5b) via
///      <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>. This clause is
///      independent of the pump — it still happens when the pump body no-ops.
/// </summary>
[CardName("Defiant Strike")]
public static class DefiantStrikeFactory
{
    public const string CardName = "Defiant Strike";
    public const string Slug = "defiant-strike";
    public const string PrintedManaCost = "{W}";

    /// <summary>Layer 7c power bonus (CR 613.1g).</summary>
    public const int PumpPower = 1;

    /// <summary>Layer 7c toughness bonus (CR 613.1g) — +0.</summary>
    public const int PumpToughness = 0;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Defiant Strike is
    /// cast. Single 1..1 "target creature" request, no modes, no X. On
    /// resolution: pump the target +1/+0 until end of turn (CR 514.2), then
    /// the caster draws a card (CR 121.1).
    /// </summary>
    /// <param name="caster">The player who cast Defiant Strike; draws the card.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Defiant Strike: target creature gets +1/+0 until end of turn, then draw a card", () =>
                    {
                        // CR 608.2e step 1 — pump the target +1/+0 until end of turn.
                        Pump(target);

                        // CR 608.2e step 2 / CR 121.1 — "Draw a card." Independent
                        // of the pump; still happens when the pump body no-ops.
                        DrawOne(caster);
                    }),
                };
            });
    }

    private static void Pump(object target)
    {
        // CR 608.2b — the pump applies only while the target is still a
        // creature on the battlefield; otherwise the clause no-ops.
        if (target is not Creature creature) return;
        if (creature.Zone != ZoneType.Battlefield) return;
        if (creature.ActiveEffects == null) return;

        // CR 613.1g layer 7c — +1/+0; CR 514.2 — until end of turn.
        creature.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));
    }

    private static void DrawOne(Player caster)
    {
        // CR 121.1 — simple top-of-library draw. Empty library flags the
        // caster for the SBA-driven loss (CR 704.5b).
        var top = caster.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            caster.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        caster.Zones.Library.RemoveCard(top);
        caster.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
