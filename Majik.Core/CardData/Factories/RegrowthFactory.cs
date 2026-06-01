using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Regrowth (Alpha + many reprints, {1}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-01):
///   "Return target card from your graveyard to your hand."
///
/// ## Why it gets its own factory
/// Regrowth is the bare sorcery version of the same
/// "return target card from your graveyard to your hand" effect carried by
/// the front face of <see cref="BalaGedRecoveryFactory"/> (and the ETB of
/// <see cref="EternalWitnessFactory"/>) — ANY card type, no restriction
/// (CR 700.6, the oracle says "card"). Unlike Bala Ged Recovery it has no
/// MDFC back face, so it is a plain green {1}{G} sorcery.
///
/// The base card shape (name / Sorcery type / {1}{G} cost) is materialised
/// from the embedded JSON definition (<c>regrowth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="ThrillOfPossibilityFactory"/>); the resolve-side graveyard
/// return is built on demand via <see cref="BuildDefinition"/> because the
/// target-request + zone-move body is not expressible in the data-only JSON
/// <c>AbilityDefinition</c> schema. The resolve body mirrors
/// <see cref="BalaGedRecoveryFactory"/>'s graveyard-return posture.
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{1}{G}</c>, mono-green, owner / controller wired.
/// - One 1..1 "target card in your graveyard" request — ANY card type
///   (CR 700.6). Same bespoke graveyard-card target shape as
///   <see cref="BalaGedRecoveryFactory"/> / <see cref="EternalWitnessFactory"/>.
/// - Resolution returns the chosen card Graveyard → Hand via
///   <see cref="ZoneService.MoveCard"/> when supplied (so any zone-change
///   triggers fire per CR 603.6a / CR 701.20), otherwise direct-zone
///   mutation. Validates the chosen card is STILL in the controller's
///   graveyard at resolution (CR 608.2b — illegal-on-resolution → clean
///   no-op); deterministic first-card fallback when no agent-set target is
///   present (single-arg / no-agent dispatcher posture).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real agent-driven target prompt</b>: production callers wire the
///   chosen target from an agent prompt before the spell resolves; the
///   first-card fallback is the dispatcher-path safety net (same posture
///   as Bala Ged Recovery / Eternal Witness).
///
/// ## References
///
/// - <see cref="BalaGedRecoveryFactory"/> — identical graveyard-return
///   effect (this factory mirrors its <c>BuildDefinition</c> /
///   <c>ResolveReturnToHand</c> body for the spell-resolution path).
/// - <see cref="EternalWitnessFactory"/> — the same effect as an ETB.
/// </summary>
[CardName("Regrowth")]
public static class RegrowthFactory
{
    public const string CardName = "Regrowth";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "regrowth";

    public const string PrintedManaCost = "{1}{G}";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Return target card from your graveyard to your hand.";

    /// <summary>
    /// Build the Regrowth sorcery shape from the embedded JSON definition.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildDefinition"/> (same split as
    /// <see cref="ThrillOfPossibilityFactory"/>).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the resolve-time "return target card from your graveyard to
    /// your hand" <see cref="SpellDefinition"/>.
    ///
    /// Single 1..1 "target card in your graveyard" request (ANY card type —
    /// CR 700.6, the oracle says "card" with no restriction). On resolution
    /// the chosen card is moved Graveyard → Hand; an illegal-on-resolution
    /// target (no longer in the controller's graveyard) is a clean no-op
    /// (CR 608.2b).
    /// </summary>
    /// <param name="owner">Spell controller — "your graveyard" is the
    /// controller's graveyard (CR 110.2). The candidate pool is the
    /// controller's graveyard at the time the definition is built;
    /// production callers refresh it at cast time via the agent prompt
    /// (same posture as Bala Ged Recovery).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    /// <param name="zoneService">When supplied, the Graveyard → Hand move
    /// routes through <see cref="ZoneService.MoveCard"/> so any downstream
    /// zone-change triggers fire (CR 603.6a / CR 701.20). When null, a
    /// direct-zone mutation is used.</param>
    public static SpellDefinition BuildDefinition(
        Player owner,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Cast<object>().ToList(),
                    Intent: BotIntent.CardAdvantage),
            },
            EffectFactory: chosen =>
            {
                object? rawTarget = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: return target card from your graveyard to your hand",
                        () => ResolveReturnToHand(owner, rawTarget, targetResolver, zoneService)),
                };
            });
    }

    /// <summary>
    /// Resolution helper for the graveyard return. Honours the agent-set
    /// target when present (production path); falls back to the first card
    /// in the controller's graveyard when none was set (deterministic
    /// single-arg / no-agent posture — mirrors
    /// <see cref="BalaGedRecoveryFactory"/>'s fallback). Validates the chosen
    /// card is STILL in the controller's graveyard at resolution (CR 608.2b —
    /// illegal target → clean no-op). Moves the card Graveyard → Hand via
    /// <see cref="ZoneService.MoveCard"/> when supplied; otherwise
    /// direct-zone mutation.
    /// </summary>
    private static void ResolveReturnToHand(
        Player owner,
        object? rawTarget,
        Func<object, object> targetResolver,
        ZoneService? zoneService)
    {
        ICard? picked = null;

        // 1) Honour the agent-set target if present (production path).
        if (rawTarget != null && targetResolver(rawTarget) is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first card in the controller's
        // graveyard (single-arg dispatcher path / no-agent posture).
        picked ??= owner.Zones.Graveyard.GetCards().FirstOrDefault();

        // Empty graveyard → clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b illegal-on-resolution check — target must still be in
        // the controller's graveyard. (Cards leaving the graveyard between
        // cast and resolution fizzle the return.)
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!owner.Zones.Graveyard.GetCards().Contains(picked)) return;

        // Move Graveyard → Hand. ZoneService path publishes a CardMovedEvent
        // so any "leaves graveyard" triggers fire (CR 603.6a / CR 701.20).
        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Hand, owner);
        }
        else
        {
            owner.Zones.Graveyard.RemoveCard(picked);
            owner.Zones.Hand.AddCard(picked);
            picked.SetZone(ZoneType.Hand);
        }
    }
}
