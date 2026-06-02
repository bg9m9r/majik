using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wooded Ridgeline (Bloomburrow common dual land).
/// Oracle text (verified against Scryfall):
///   "({T}: Add {R} or {G}.)
///    This land enters tapped."
///
/// Type line: <c>Land — Mountain Forest</c>.
///
/// <para>
/// This is the plain, unconditional tapped R/G dual — strictly simpler than a
/// "gainland" (no lifegain ETB rider) and simpler than a "battle land" (no
/// conditional "unless you control N basics"). Structurally it is
/// <see cref="ScatteredGrovesFactory"/> minus cycling: both printed land
/// subtypes (Mountain / Forest), two {R}/{G} mana abilities, and an
/// unconditional enters-tapped replacement.
/// </para>
///
/// <para>
/// The Land shell — both printed land subtypes plus the two mana abilities
/// (CR 605.1 — mana abilities don't use the stack) — is declared declaratively
/// in <c>Majik.Core/CardData/Cards/wooded-ridgeline.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="CinderGladeFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped." (CR 614.1c) is wired as an unconditional
/// <see cref="EntersTappedReplacement"/> when a <see cref="ReplacementBus"/>
/// is supplied. The single-arg dispatcher path constructs without a bus — the
/// ETB-tapped replacement is omitted (shape-only posture matching every other
/// ETB-replacement factory's single-arg path); the mana abilities are still
/// attached. On the production load path the unconditional tapped clause is
/// also matched by <see cref="Majik.Core.CardData.EntersTappedBinder"/> off the
/// oracle text.
/// </para>
/// </summary>
[CardName("Wooded Ridgeline")]
public static class WoodedRidgelineFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("wooded-ridgeline");

    /// <summary>Construct Wooded Ridgeline owned and controlled by
    /// <paramref name="owner"/> (shape-only path — no ETB-tapped replacement
    /// wired).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Wooded Ridgeline with an optional
    /// <see cref="ReplacementBus"/> for full unconditional "this land enters
    /// tapped" wiring (CR 614.1c).</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as ScatteredGrovesFactory / CinderGladeFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
