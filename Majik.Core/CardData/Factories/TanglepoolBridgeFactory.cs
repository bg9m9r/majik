using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tanglepool Bridge (Murders at Karlov Manor
/// Commander — the GU member of the artifact "Bridge" tapland cycle).
///
/// Type line: <c>Artifact Land</c>. Oracle text (verified against Scryfall):
/// <code>
/// This land enters tapped.
/// Indestructible
/// {T}: Add {G} or {U}.
/// </code>
///
/// Mirrors <see cref="RazortideBridgeFactory"/> (the WU member of the same
/// cycle) — same Artifact Land shell, printed Indestructible, unconditional
/// enters-tapped, and one <see cref="ManaAbility"/> per produced colour;
/// only the two produced colours change (W/U -> G/U).
///
/// <para>
/// The data-expressible shell — the <c>Artifact Land</c> typing (CR 301.1 /
/// 305.1; primary <see cref="Cards.Types.CardType.Land"/> plus the additively
/// flagged <see cref="Cards.Types.CardType.Artifact"/>) plus the two mana
/// abilities {G}/{U} (CR 605.1 — mana abilities don't use the stack) — is
/// declared declaratively in
/// <c>Majik.Core/CardData/Cards/tanglepool-bridge.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as the Guildgate
/// factories.
/// </para>
///
/// <para>
/// The two clauses the JSON schema cannot yet express are wired in code here:
/// <list type="bullet">
///   <item><b>Indestructible</b> (CR 702.12) — a <see cref="KeywordAbility"/>
///     marker read by the non-creature destroy gate (mirrors
///     Darksteel Citadel / Razortide Bridge).</item>
///   <item><b>Enters-tapped</b> (CR 614.1c) — unconditional "This land enters
///     tapped." registered via <see cref="EntersTappedReplacement"/> on the
///     supplied <see cref="ReplacementBus"/>. The shape-only path (null bus)
///     skips registration, mirroring <see cref="RazortideBridgeFactory"/>. On
///     the production load path the tapped clause is also matched by
///     <see cref="EntersTappedBinder"/> off the printed oracle text.</item>
/// </list>
/// </para>
/// </summary>
[CardName("Tanglepool Bridge")]
public static class TanglepoolBridgeFactory
{
    public const string CardName = "Tanglepool Bridge";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("tanglepool-bridge");

    /// <summary>
    /// Construct Tanglepool Bridge owned and controlled by
    /// <paramref name="owner"/>. Single-arg path — no bus wiring (shape
    /// observability only; the enters-tapped replacement is omitted).
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Tanglepool Bridge with an optional <see cref="ReplacementBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "This land enters tapped." replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Artifact Land shell + the two {G}/{U} mana abilities come from the
        // JSON definition (CR 301.1 / 305.1 / 605.1).
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — destroy gates read
        // KeywordAbility off Permanent. Not expressible in the JSON schema.
        // ----------------------------------------------------------------
        land.AddAbility(new KeywordAbility("Indestructible", land, owner));

        // ----------------------------------------------------------------
        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as RazortideBridgeFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
