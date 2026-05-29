using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hanweir Battlements (Eldritch Moon, Land).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {R}, {T}: Target creature gains haste until end of turn.
///    {3}{R}{R}, {T}: If you both own and control this land and a creature
///    named Hanweir Garrison, exile them, then meld them into Hanweir, the
///    Writhing Township."
///
/// Same "utility land — {T}: Add + activated combat ability" family as
/// <see cref="ArenaOfGloryFactory"/> / <see cref="DenOfTheBugbearFactory"/>
/// for the mana + haste-grant halves; the target-creature haste grant reuses
/// the <see cref="TargetRequest"/> + <see cref="GrantKeywordUntilEndOfTurnEffect"/>
/// pattern shared with <see cref="AbolethSpawnFactory"/> / <see cref="KikiJikiMirrorBreakerFactory"/>.
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain <see cref="Land"/>, no supertype, no
///   printed subtype (Hanweir Battlements is a nonbasic, non-typed land).
/// - <b>{T}: Add {C}</b> — vanilla colourless <see cref="ManaAbility"/>
///   (CR 605.1; {C} stored as one Generic pip, same posture as
///   <see cref="AetherHubFactory"/>).
/// - <b>{R}, {T}: Target creature gains haste until end of turn</b> —
///   an <see cref="ActivatedAbility"/> (CR 602) with two costs:
///   a <see cref="ManaCostCost"/> of {R} plus <see cref="AdditionalCost.Tap"/>.
///   One <see cref="TargetRequest"/> declares a 1..1 "target creature"
///   (Intent <see cref="BotIntent.Buff"/>; gathers every creature on the
///   battlefield — any creature is a legal target, unrestricted). On
///   resolution the effect reads <see cref="ActivatedAbility.ChosenTargets"/>,
///   re-checks the target is still a battlefield <see cref="Creature"/>
///   (CR 608.2b), and registers a Layer-6
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Haste") on the target's
///   <see cref="Creature.ActiveEffects"/> (CR 702.10 / CR 514 end-of-turn
///   expiry). Null-guarded for the shape-only path where the chosen creature
///   has no continuous-effects service (mirrors Aboleth Spawn / Reckless
///   Charge).
///
/// ## Deferred (v1 gaps)
/// - <b>Meld clause — "{3}{R}{R}, {T}: If you both own and control this land
///   and a creature named Hanweir Garrison, exile them, then meld them into
///   Hanweir, the Writhing Township." (CR 701.41 — Meld)</b> — the engine
///   has NO meld primitive (no melded-permanent representation, no
///   double-faced-back combine path). The third activated ability is attached
///   structurally with its printed cost ({3}{R}{R} + {T}) so the card surface
///   is inspectable, but its resolution effect is a documented no-op. This is
///   the same "shape attached, resolution deferred" posture used for the
///   destroy/untap-target JSON stubs in
///   <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///   Implementing meld is a separate engine slice (a new keyword-action
///   primitive + the meld-target back-face Hanweir, the Writhing Township).
/// - <b>Choose-time legality filter</b> for the haste target is gathered from
///   the live battlefield via <see cref="TargetRequest.CandidateGatherer"/>;
///   resolve-time recheck guards zone + type (same posture as Aboleth Spawn /
///   Kiki-Jiki).
/// </summary>
[CardName("Hanweir Battlements")]
public static class HanweirBattlementsFactory
{
    public const string CardName = "Hanweir Battlements";
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// Construct Hanweir Battlements owned and controlled by
    /// <paramref name="owner"/>. The mana ability, the {R},{T} haste-grant
    /// ability, and the meld-stub ability are all attached so the card
    /// surface is complete; the haste grant resolves against the chosen
    /// creature's own continuous-effects service when one is wired, and the
    /// meld clause is a documented no-op (see class xmldoc). Suitable for the
    /// <see cref="NamedCardFactory"/> dispatcher and shape tests.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}  (CR 605.1 — mana ability, no stack). {C} is stored
        // as one Generic pip (same as AetherHub).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {R}, {T}: Target creature gains haste until end of turn.
        // CR 602 — ordinary activated ability (uses the stack). Costs =
        // {R} (ManaCostCost) + {T} (AdditionalCost.Tap).
        // ----------------------------------------------------------------
        land.AddAbility(BuildHasteAbility(land, owner));

        // ----------------------------------------------------------------
        // {3}{R}{R}, {T}: meld — documented v1 stub (no meld primitive).
        // Attached for surface completeness; resolution is a no-op.
        // ----------------------------------------------------------------
        land.AddAbility(BuildMeldStubAbility(land, owner));

        return land;
    }

    /// <summary>
    /// The "{R}, {T}: Target creature gains haste until end of turn"
    /// activated ability built on <paramref name="land"/>. Exposed for tests
    /// that drive the ability's targets + effects directly.
    /// </summary>
    public static ActivatedAbility HasteAbility(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return land.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<ManaCostCost>()
                .Any() && a.TargetRequests.Count == 1);
    }

    /// <summary>
    /// The "{3}{R}{R}, {T}: meld" stub activated ability built on
    /// <paramref name="land"/>. Exposed for tests that inspect its printed
    /// cost shape.
    /// </summary>
    public static ActivatedAbility MeldAbility(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return land.Abilities.OfType<ActivatedAbility>()
            .First(a => a.TargetRequests.Count == 0);
    }

    private static ActivatedAbility BuildHasteAbility(Land land, Player owner)
    {
        ActivatedAbility? ability = null;

        var grantEffect = new Effect(
            $"{CardName}: target creature gains haste until end of turn (CR 702.10)",
            () =>
            {
                if (ability == null) return;
                var chosen = ability.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality recheck: target must
                // still be a creature on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 702.10 / CR 514 — Layer-6 Haste grant with end-of-turn
                // expiry. Skipped silently when the target has no live
                // continuous-effects service (shape-only test path — mirrors
                // Aboleth Spawn / Reckless Charge).
                if (target.ActiveEffects != null)
                {
                    target.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(target, GrantedKeyword));
                }
            });

        ability = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{R}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { grantEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    // Any creature on the battlefield is a legal target
                    // (unrestricted "target creature").
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        return ability;
    }

    private static ActivatedAbility BuildMeldStubAbility(Land land, Player owner)
    {
        // CR 701.41 — Meld. No engine primitive yet (no melded-permanent
        // representation, no back-face combine path). Resolution is a
        // documented no-op; the printed cost is wired so the surface is
        // inspectable. See class xmldoc.
        var meldStub = new Effect(
            $"{CardName}: meld with Hanweir Garrison into Hanweir, the Writhing Township (stub — no meld primitive yet)",
            () => { /* meld deferred — CR 701.41 */ });

        return new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}{R}{R}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { meldStub });
    }
}
