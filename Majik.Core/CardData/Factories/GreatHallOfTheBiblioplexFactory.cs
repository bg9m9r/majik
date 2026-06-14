using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Great Hall of the Biblioplex (Strixhaven Mystical
/// Archive land cycle). Land.
///
/// Oracle text (verified Scryfall 2026-06-14):
///   "{T}: Add {C}.
///    {T}, Pay 1 life: Add one mana of any color. Spend this mana only to cast
///    an instant or sorcery spell.
///    {5}: If this land isn't a creature, it becomes a 2/4 Wizard creature with
///    \"Whenever you cast an instant or sorcery spell, this creature gets +1/+0
///    until end of turn.\" It's still a land."
///
/// A conditional-animate member of the manland family with a granted cast-pump
/// trigger on the animated body. The animate has no "until end of turn" clause,
/// so the body is PERMANENT once activated (CR 613.1c) — but the activation is
/// a no-op when the land is already a creature ("If this land isn't a creature").
///
/// ## Production wiring
/// Lands are NEVER routed through their <c>[CardName]</c> factory in prod
/// (<see cref="Majik.Core.Api.GameFacade"/>'s deck-build gates the swap on
/// <c>!shell.HasType(Land)</c>) — the animate ability + granted cast-pump are
/// bound on the live table by <see cref="ManlandBinder"/>. This factory exists
/// for the (test-only) <c>[CardName]</c> dispatch + to flip <c>IsImplemented</c>.
///
/// ## Implemented (v1)
/// - Plain Land + <c>{T}: Add {C}</c> mana ability (CR 605.1).
/// - <b>{5}: animate to a 2/4 Wizard</b> (conditional on the land not already
///   being a creature) granting a Kiln-Fiend-shaped cast-pump trigger
///   (<see cref="ManlandCyclePumpEffect"/> +1/+0 EOT on each of
///   the controller's instant/sorcery casts, CR 603.1). Land stays (CR 613.1c).
///
/// ## Deferred (v1 gaps)
/// - <b>"{T}, Pay 1 life: Add one mana of any color, spend only on an instant
///   or sorcery"</b> — the per-slot spend-restriction is the same gate as
///   Cavern of Souls / Boseiju; not modelled here.
/// - <b>Combat math through Compute on the C# Land instance</b> — same
///   structural manland gap, tracked in v1-deferrals.
/// </summary>
[CardName("Great Hall of the Biblioplex")]
public static class GreatHallOfTheBiblioplexFactory
{
    public const string CardName = "Great Hall of the Biblioplex";
    public const int Power = 2;
    public const int Toughness = 4;
    public const int PumpAmount = 1;

    /// <summary>Construct Great Hall with no effects service (shape-only).</summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>Construct Great Hall. When <paramref name="effects"/> is supplied
    /// the animate ability's continuous effects + granted trigger are wired.</summary>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // {T}: Add {C} — CR 605.1 mana ability (no stack).
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // {5}: If this land isn't a creature, it becomes a 2/4 Wizard creature
        // with the quoted cast-pump trigger. It's still a land. (Permanent.)
        var animateEffect = new Effect(
            $"{CardName}: becomes a {Power}/{Toughness} Wizard creature (still a land)",
            () =>
            {
                if (effects == null) return; // shape-only path

                // CR 613.1c — no-op if already a creature.
                if (effects.Compute(land).Types.Contains(CardType.Creature)) return;

                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Wizard },
                    extraTypes: null,
                    expiresAtEndOfTurn: false));
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, Power, Toughness, expiresAtEndOfTurn: false));

                // CR 603.1 — granted "Whenever you cast an instant or sorcery
                // spell, this creature gets +1/+0 until end of turn." trigger.
                var pump = new Effect(
                    $"{CardName}: +{PumpAmount}/+0 until end of turn (cast instant or sorcery)",
                    () => effects.Register(
                        new ManlandCyclePumpEffect(land, PumpAmount, 0)));
                var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
                {
                    var caster = land.Controller ?? owner;
                    return ReferenceEquals(e.Spell.Controller, caster)
                        && (e.Spell.Card.HasType(CardType.Instant)
                            || e.Spell.Card.HasType(CardType.Sorcery));
                });
                land.AddAbility(new TriggeredAbility(
                    source: land,
                    controller: owner,
                    condition: condition,
                    effects: new IEffect[] { pump },
                    activeZones: new[] { ZoneType.Battlefield }));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{5}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}
