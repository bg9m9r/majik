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
/// Named-card factory for Distortion Strike (Rise of the Eldrazi, {U}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Target creature gets +1/+0 until end of turn and can't be blocked
///    this turn.
///    Rebound (If you cast this spell from your hand, exile it as it
///    resolves. At the beginning of your next upkeep, you may cast this
///    card from exile without paying its mana cost.)"
///
/// Distortion Strike composes three shapes the engine already supports:
/// - The <b>+1/+0 pump</b> mirrors <see cref="DefiantStrikeFactory"/> /
///   <see cref="MonstrousGrowthFactory"/> — a single 1..1 "target creature"
///   <see cref="TargetRequest"/> that registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, +0) on the target's
///   <see cref="Creature.ActiveEffects"/> (CR 613.1g layer 7c; CR 514.2 —
///   expires in cleanup).
/// - The <b>can't-be-blocked-this-turn rider</b> mirrors
///   <see cref="EarthshakerKhenraFactory"/> /
///   <see cref="RoguesPassageFactory"/> — a single-target
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/> (CR 509.1c / CR 702.x)
///   registered on the target's <see cref="Creature.ActiveEffects"/>. The
///   default <c>expiresAtEndOfTurn = true</c> matches the printed "this
///   turn" rider (CR 514.2).
/// - The <b>Rebound rider</b> (CR 702.88) is attached only as a
///   <see cref="KeywordAbility"/>("Rebound") marker, matching the
///   <see cref="StaggershockFactory"/> / <see cref="EphemerateFactory"/>
///   convention (see "Deferred" below).
///
/// Card shape comes from the embedded JSON (<c>distortion-strike.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema) — same posture as
/// <see cref="DefiantStrikeFactory"/>.
///
/// On resolution the pump and the can't-be-blocked grant share a single
/// resolution clause (CR 608.2e); both no-op when the target is no longer a
/// creature on the battlefield (CR 608.2b — an illegal target fizzles).
///
/// ## Deferred (v1 gap)
/// - <b>Rebound mechanic</b> (CR 702.88): "If you cast this spell from your
///   hand, exile it as it resolves. At the beginning of your next upkeep,
///   you may cast this card from exile without paying its mana cost."
///   Requires (1) a cast-from-hand replacement that routes Stack → Exile
///   instead of Stack → Graveyard on resolution (CR 702.88a), and (2) a
///   delayed triggered ability registered on resolve that fires on the
///   controller's next upkeep and offers a free-cast prompt from exile
///   (CR 702.88b). Neither half exists as a reusable primitive today, so
///   the rider is deferred and only the keyword marker is attached — the
///   same posture as <see cref="StaggershockFactory"/> (the marker becomes
///   the wiring point once the "cast from exile without paying" primitive
///   lands). The pump + can't-be-blocked body is shape-correct without
///   Rebound.
/// </summary>
[CardName("Distortion Strike")]
public static class DistortionStrikeFactory
{
    public const string CardName = "Distortion Strike";
    public const string Slug = "distortion-strike";
    public const string PrintedManaCost = "{U}";

    /// <summary>Layer 7c power bonus (CR 613.1g).</summary>
    public const int PumpPower = 1;

    /// <summary>Layer 7c toughness bonus (CR 613.1g) — +0.</summary>
    public const int PumpToughness = 0;

    /// <summary>
    /// Build the card shape from the embedded JSON definition and attach the
    /// Rebound keyword marker (CR 702.88 — rider deferred, see class xmldoc).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(def, owner);

        // CR 702.88 — Rebound marker. The actual rider (exile-on-resolve +
        // next-upkeep free cast from exile) is deferred; the marker is
        // attached so oracle audits / KeywordRegistry consumers detect the
        // keyword without inspecting the SpellDefinition shape.
        card.AddAbility(new KeywordAbility("Rebound", card, owner));

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Distortion Strike is
    /// cast. Single 1..1 "target creature" request, no modes, no X. On
    /// resolution: pump the target +1/+0 until end of turn (CR 514.2) and grant
    /// it can't-be-blocked-this-turn (CR 509.1c / CR 702.x). The Rebound
    /// exile-on-resolve rider is NOT modelled at this surface (see class
    /// xmldoc gap).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
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
                    new Effect(
                        "Distortion Strike: target creature gets +1/+0 until end of turn and can't be blocked this turn",
                        () => ApplyToTarget(target)),
                };
            });
    }

    private static void ApplyToTarget(object target)
    {
        // CR 608.2b — both clauses apply only while the target is still a
        // creature on the battlefield; otherwise the spell fizzles (no-op).
        if (target is not Creature creature) return;
        if (creature.Zone != ZoneType.Battlefield) return;
        if (creature.ActiveEffects == null) return;

        // CR 613.1g layer 7c — +1/+0; CR 514.2 — until end of turn.
        creature.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));

        // CR 509.1c / CR 702.x — single-target "can't be blocked" restriction.
        // expiresAtEndOfTurn defaults to true → "this turn" (CR 514.2).
        creature.ActiveEffects.Register(
            new CombatRestrictionEffect(CombatRestriction.CannotBeBlocked, creature));
    }
}
