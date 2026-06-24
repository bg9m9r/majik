using System.Linq;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Strangled Cemetery (Duskmourn: House of Horror —
/// the B/G member of the "fast-life" surveil-adjacent dual-land cycle).
///
/// Land. Oracle text (verified against the embedded Scryfall seed):
///   "This land enters tapped unless a player has 13 or less life.
///    {T}: Add {B} or {G}."
///
/// <para>
/// Type line: Land (nonbasic, no supertype, no printed subtype). Colorless
/// card (no mana cost); B/G colour identity.
/// </para>
///
/// <para>
/// The base card surface — name, Land type, the two single-colour mana
/// abilities {B}/{G} (CR 605.1 — mana abilities don't use the stack) — is
/// declared declaratively in <c>Majik.Core/CardData/Cards/strangled-cemetery.json</c>
/// and materialized via <see cref="CardDefinitionFactory"/>, mirroring the
/// JSON-driven posture of <see cref="JungleHollowFactory"/>. "Add {B} or {G}"
/// is modelled as two separate <see cref="Majik.Core.Abilities.ManaAbility"/>
/// entries (one per produced colour), matching every other "Add {X} or {Y}"
/// dual land in the pool.
/// </para>
///
/// <para>
/// <b>ETB tapped unless a player has 13 or less life (CR 614.1c)</b> — layered
/// on here via <see cref="ConditionalEntersTappedReplacement"/> on the supplied
/// <see cref="ReplacementBus"/>. Unlike the "unless you control …" conditional
/// lands, the gate is on a GLOBAL game-state fact ("a player" — any player,
/// CR 102.1 includes the controller), not the controller's own board. The
/// predicate therefore reads the live seated player set off
/// <see cref="GamePlayersRegistry.AllPlayers"/> (the same ambient registry the
/// mana-ability "each opponent" riders use) and returns "enters untapped" iff
/// ANY player in the game currently has 13 or less life. The
/// <see cref="ConditionalEntersTappedBinder"/> regex does NOT claim this oracle
/// form, so this named-factory registration is the only wiring (no double-bind).
/// </para>
///
/// <para>
/// Shape-only single-arg dispatcher path: no <see cref="ReplacementBus"/> wired,
/// so the ETB-tapped replacement is omitted (the land enters untapped on that
/// path). Same posture as <see cref="JungleHollowFactory"/> /
/// <see cref="AgnaQelaFactory"/> and the rest of the ETB-replacement factories.
/// </para>
/// </summary>
[CardName("Strangled Cemetery")]
public static class StrangledCemeteryFactory
{
    public const string CardName = "Strangled Cemetery";
    public const string Slug = "strangled-cemetery";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Threshold from the oracle text: a player having THIS much life or less
    /// causes Strangled Cemetery to enter untapped (CR 614.1c).
    /// </summary>
    public const int LifeThreshold = 13;

    /// <summary>
    /// Construct Strangled Cemetery owned and controlled by
    /// <paramref name="owner"/> (shape-only path — the conditional enters-tapped
    /// replacement is omitted, no <see cref="ReplacementBus"/> to register
    /// against). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Strangled Cemetery with optional replacement-bus wiring so the
    /// "enters tapped unless a player has 13 or less life" restriction
    /// (CR 614.1c) is registered against <paramref name="replacements"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the conditional enters-tapped
    /// replacement is registered; when null the registration is skipped
    /// (shape-only path).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB tapped unless a player has 13 or less life (CR 614.1c).
        // "a player" (CR 102.1) — ANY player in the game, including the
        // controller. Reads the live seated player set off the ambient
        // GamePlayersRegistry so the predicate sees real game state at ETB
        // (empty set on shape-only paths ⇒ no player qualifies ⇒ enters
        // tapped, the safe default).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    GamePlayersRegistry.AllPlayers
                        .Any(p => p != null && p.LifeTotal <= LifeThreshold)));
        }

        return land;
    }
}
