using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spectral Procession (Shadowmoor, {(2/W)}{(2/W)}{(2/W)}).
///
/// Sorcery. Oracle text:
///   "Create three 1/1 white Spirit creature tokens with flying."
///
/// ## Implemented (v1)
/// - Sorcery shape with printed twobrid mana cost {2/W}{2/W}{2/W}
///   (CR 107.4e / CR 202.3f). <see cref="ManaCost.Parse"/> reads each
///   <c>{2/W}</c> pip as a <see cref="HybridPip"/> with
///   <c>GenericAlternative = 2</c>; <see cref="ManaCost.TotalValue"/>
///   takes the higher generic alternative per pip so Spectral Procession
///   reports a mana value of 6. Cast-time payment can satisfy each pip
///   with either 2 generic mana or 1 white mana (cost-payer / mana-cost
///   solver responsibility — the engine's hybrid handling already covers
///   this for Boros Reckoner / Kitchen Finks / Manamorphose).
/// - Resolve effect (<see cref="BuildResolveEffect"/>): create three 1/1
///   white Spirit creature tokens with Flying via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. Same token shape as
///   <see cref="LingeringSoulsFactory.CreateSpiritToken"/> — explicit
///   White colour stamp via <see cref="TokenFactory.TokenSpec.Colors"/>,
///   Flying added as a granted <see cref="KeywordAbility"/> via the
///   spec's Keywords list (CR 702.9).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only path. Card is constructed
///   without a resolve effect body bound; callers (tests, definition
///   binders) build the spell body via <see cref="BuildResolveEffect"/>
///   and splice it into a <see cref="Majik.Core.Game.SpellDefinition"/>
///   or <see cref="Majik.Core.Spells.Spell"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Hybrid-pip cost optimisation</b>: the engine's mana-payer chooses
///   how to satisfy each {2/W} pip at cast time. v1 inherits whatever
///   policy <see cref="Majik.Core.Costs.ManaCostCost"/> + the mana-pool
///   solver enforce (typically "prefer the cheaper option that fits the
///   pool"). Spectral Procession is the canonical "three white mana for
///   three flyers" payoff when the controller can fully pay
///   {W}{W}{W}; the {2}{2}{2} fallback is the all-generic path. Both
///   are valid under CR 202.3f.
/// </summary>
[CardName("Spectral Procession")]
public static class SpectralProcessionFactory
{
    public const string CardName = "Spectral Procession";

    /// <summary>
    /// Printed mana cost — three twobrid pips {(2/W)} (CR 107.4e).
    /// <see cref="ManaCost.Parse"/> turns each pip into a
    /// <see cref="HybridPip"/> with GenericAlternative=2. TotalValue = 6.
    /// </summary>
    public const string PrintedManaCost = "{2/W}{2/W}{2/W}";

    public const int TokensCreated = 3;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Oracle text reference. Spectral Procession has no flashback / kicker
    /// rider — the printed mana cost itself is the only cost surface.
    /// </summary>
    public const string OracleText =
        "Create three 1/1 white Spirit creature tokens with flying.";

    /// <summary>
    /// Construct the Spectral Procession sorcery shape with no resolve
    /// effect bound. Use <see cref="BuildResolveEffect"/> to compose the
    /// create-three-Spirits body into a
    /// <see cref="Majik.Core.Game.SpellDefinition"/> or
    /// <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Spectral Procession's resolve effect — create three 1/1
    /// white Spirit creature tokens with Flying under
    /// <paramref name="caster"/>.
    /// </summary>
    /// <param name="caster">The resolving caster — token controller.</param>
    /// <param name="zoneService">Optional zone service so each spawned
    /// Spirit token publishes <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// on ETB. When null, tokens use raw zone moves.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create {TokensCreated} 1/1 white Spirit tokens with flying",
                () =>
                {
                    for (var i = 0; i < TokensCreated; i++)
                    {
                        CreateSpiritToken(caster, zoneService);
                    }
                }),
        };
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 white Spirit creature token with
    /// Flying under <paramref name="controller"/>. Mirrors
    /// <see cref="LingeringSoulsFactory.CreateSpiritToken"/> so Spirit-
    /// token minting stays uniform across the two W-sources.
    /// </summary>
    public static Creature CreateSpiritToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Spirit",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Spirit },
            Keywords: new[] { "Flying" },
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
