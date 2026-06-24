using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spectacle Summit (Tarkir: Dragonstorm, the U/R
/// member of the "spire" surveil-on-activation land cycle). Oracle text
/// (verified against Scryfall 2026-06-24):
///   "This land enters tapped.
///    {T}: Add {U} or {R}.
///    {2}{U}{R}, {T}: Surveil 1. (Look at the top card of your library. You may
///    put it into your graveyard.)"
///
/// <para>
/// Distinct from the Murders-at-Karlov-Manor surveil dual cycle
/// (<see cref="SurveilLandCycleFactory"/>): those surveil on enter-the-battlefield
/// (an <c>etb_self</c> trigger), whereas Spectacle Summit's surveil is a paid
/// <em>activated</em> ability — <c>{2}{U}{R}, {T}: Surveil 1</c> (CR 701.42).
/// </para>
///
/// <para>
/// The full card surface — name, Land type, the two {U}/{R} mana abilities
/// (CR 605.1a — mana abilities don't use the stack), and the activated
/// <c>{2}{U}{R}, {T}: Surveil 1</c> ability (a <c>mana</c> + <c>tap_self</c>
/// cost with a <c>surveil_self</c> effect) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/spectacle-summit.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture of
/// <see cref="SinisterStarfishFactory"/> (the same activated-surveil shape) and
/// <see cref="TranquilCoveFactory"/> (the same enters-tapped land posture).
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed oracle
/// text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the two-arg
/// <see cref="Create(Player, ReplacementBus?)"/> path also registers an
/// <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>. The shape-only single-arg path skips the
/// registration (no bus available) — same posture as
/// <see cref="TranquilCoveFactory"/>.
/// </para>
/// </summary>
[CardName("Spectacle Summit")]
public static class SpectacleSummitFactory
{
    public const string Slug = "spectacle-summit";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Spectacle Summit owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted, no
    /// <see cref="ReplacementBus"/> to register against). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Spectacle Summit with optional replacement-bus wiring so
    /// the unconditional enters-tapped restriction (CR 614.1c) is registered
    /// against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same posture
        // as TranquilCoveFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
