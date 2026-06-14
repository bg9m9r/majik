using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Svogthos, the Restless Tomb (Ravnica: City of Guilds).
/// Land.
///
/// Oracle text (verified Scryfall 2026-06-14):
///   "{T}: Add {C}.
///    {3}{B}{G}: Until end of turn, this land becomes a black and green Plant
///    Zombie creature with \"This creature's power and toughness are each equal
///    to the number of creature cards in your graveyard.\" It's still a land."
///
/// A characteristic-defining-P/T (*/*) member of the animate-until-EOT
/// "manland" family. The animated body's power and toughness are each equal to
/// the number of creature cards in the controller's graveyard (CR 604.3 /
/// 613.2 Layer 7a), recomputed live.
///
/// ## Production wiring
/// Lands are NEVER routed through their <c>[CardName]</c> factory in prod
/// (<see cref="Majik.Core.Api.GameFacade"/>'s deck-build gates the factory
/// instance-swap on <c>!shell.HasType(Land)</c>) — the animate ability is
/// bound on the live table by <see cref="ManlandBinder"/>, which recognises
/// the quoted graveyard-creature CDA shape. This factory exists for the
/// (test-only) <c>[CardName]</c> dispatch + to flip <c>IsImplemented</c>;
/// it mirrors the binder's behaviour.
///
/// ## Implemented (v1)
/// - Plain Land + <c>{T}: Add {C}</c> mana ability (CR 605.1).
/// - <b>{3}{B}{G}: animate until EOT</b> — a black and green Plant Zombie whose
///   P/T is the CDA above. Registers a <see cref="ManlandCycleAnimateEffect"/>
///   (Layer 4 — Creature + Plant + Zombie subtypes; Land stays), a
///   <see cref="CdaPowerToughnessEffect"/> (Layer 7a — */* from graveyard
///   creature count), and a <see cref="SetColorsEffect"/> (Layer 5 — black and
///   green), all flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   (CR 514.2).
///
/// ## Deferred (v1 gaps)
/// - <b>Combat math through Compute on the C# Land instance</b> — same
///   structural manland gap (an animated <see cref="Land"/> instance is never a
///   <see cref="Creature"/> attacker), tracked in v1-deferrals.
/// </summary>
[CardName("Svogthos, the Restless Tomb")]
public static class SvogthosTheRestlessTombFactory
{
    public const string CardName = "Svogthos, the Restless Tomb";

    /// <summary>Construct Svogthos with no effects service (shape-only).</summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>Construct Svogthos. When <paramref name="effects"/> is supplied
    /// the animate ability's continuous effects are registered.</summary>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // {T}: Add {C} — CR 605.1 mana ability (no stack).
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // {3}{B}{G}: Until end of turn, this land becomes a black and green
        // Plant Zombie creature with the graveyard-creature CDA. Still a land.
        var animateEffect = new Effect(
            $"{CardName}: becomes a */* black and green Plant Zombie creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // shape-only path

                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Plant, CardSubtype.Zombie },
                    extraTypes: null,
                    expiresAtEndOfTurn: true));

                // CR 604.3 / 613.2 Layer 7a — P/T = creature cards in graveyard.
                effects.Register(new CdaPowerToughnessEffect(
                    source: land,
                    powerOf: GraveyardCreatureCount,
                    toughnessOf: GraveyardCreatureCount,
                    expiresAtEndOfTurn: true));

                // CR 613.1e Layer 5 — black and green body.
                effects.Register(new SetColorsEffect(
                    source: land,
                    scope: pm => ReferenceEquals(pm, land),
                    colors: new[] { ManaColor.Black, ManaColor.Green },
                    expiresAtEndOfTurn: true));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{3}{B}{G}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }

    /// <summary>CR 305.1 — creature cards in the controller's graveyard.</summary>
    private static int GraveyardCreatureCount(Permanent land)
    {
        var ctrl = land.Controller ?? land.Owner;
        if (ctrl == null) return 0;
        return ctrl.Zones.Graveyard.GetCards().Count(c => c.HasType(CardType.Creature));
    }
}
