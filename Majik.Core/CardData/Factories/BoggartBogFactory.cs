using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Boggart Trawler // Boggart Bog (Bloomburrow).
///
/// Land. Oracle text (back):
///   "As this land enters, you may pay 3 life. If you don't, it enters
///    tapped."
///   "{T}: Add {B}."
///
/// Front face — <see cref="BoggartTrawlerFactory"/> (Creature — Goblin
/// {2}{B} 3/1 — "When this creature enters, exile target player's
/// graveyard.").
///
/// ## MDFC infra
/// See <see cref="BoggartTrawlerFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Boggart Bog"</c> and lands here. The card is constructed
/// with its <see cref="MdfcState"/> pre-flipped to the back face so the
/// face tracker reads as authoritative even though the back face is the
/// permanent that actually exists.
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/> with no printed subtype (Boggart Bog
///   is a vanilla nonbasic land, no Swamp subtype).
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="SoporificSpringsFactory"/>'s back-face posture).
/// - <b>{T}: Add {B}</b> — single <see cref="ManaAbility"/> producing
///   one black mana (CR 605.1 — mana ability, no stack).
/// - <b>ETB "you may pay 3 life; if you don't, it enters tapped"
///   (CR 614.1c)</b> — modelled via <see cref="ConditionalEntersTappedReplacement"/>
///   on the supplied <see cref="ReplacementBus"/>. Predicate:
///     <list type="bullet">
///       <item>CR 119.4 — "you can't pay life you don't have." Life total
///         below 3 → no prompt; land enters tapped.</item>
///       <item>Otherwise consult the registered <see cref="IPlayerAgent"/>
///         via <see cref="IPlayerAgent.ChooseYesNoAsync"/> with intent
///         <c>BotIntent.LoseLife | BotIntent.CostToDecline</c>. On a "yes"
///         the controller's life is reduced by 3 via
///         <see cref="Player.LoseLife"/> (CR 118.8) and the land enters
///         untapped. On a "no" / no-agent / agent-throw the land enters
///         tapped (decline path matches <see cref="SoporificSpringsFactory"/>'s
///         posture).</item>
///     </list>
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture). The land
///   enters untapped on that path; the full overload registers the
///   replacement when the bus is supplied.
///
/// ## Deferred (v1 gaps)
/// - <b>Life-payment event provenance</b>: the 3-life payment runs through
///   <see cref="Player.LoseLife"/>, not a dedicated <c>LifePaidEvent</c>.
///   "Whenever a player pays life" triggers don't see this payment — same
///   simplification <see cref="SoporificSpringsFactory"/> and
///   <see cref="ShockLandCycleFactory"/> take.
/// </summary>
[CardName("Boggart Bog")]
public static class BoggartBogFactory
{
    public const string CardName = "Boggart Bog";
    public const string FrontName = "Boggart Trawler";

    /// <summary>
    /// Construct Boggart Bog with no live <see cref="ReplacementBus"/> wiring.
    /// The ETB "pay 3 life or enter tapped" replacement is omitted
    /// (shape-only posture matching the single-arg dispatcher path); the
    /// {T}: Add {B} mana ability is still attached. Suitable for identity /
    /// shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Boggart Bog with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the
    /// "you may pay 3 life; if you don't, it enters tapped" replacement is
    /// registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Boggart Bog is a vanilla nonbasic land — no basic land
        // subtype, no supertype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Boggart Bog is the back face that actually exists on
        // the battlefield). Mirrors SoporificSpringsFactory's back-face
        // posture.
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ----------------------------------------------------------------
        // ETB: "As this land enters, you may pay 3 life. If you don't, it
        // enters tapped." (CR 614.1c)
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    TryPayThreeLifeOrEnterTapped(controller, self)));
        }

        // ----------------------------------------------------------------
        // {T}: Add {B}  (CR 605.1 — mana ability, no stack)
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        return land;
    }

    /// <summary>
    /// Predicate body for the ETB replacement. Honours CR 119.4
    /// ("you can't pay life you don't have"), consults the controller's
    /// registered agent for the optional payment, and deducts 3 life on
    /// the yes path.
    /// </summary>
    /// <returns><c>true</c> ⇒ land enters untapped (life was paid).
    /// <c>false</c> ⇒ enters tapped (declined, no agent, or insufficient
    /// life).</returns>
    private static bool TryPayThreeLifeOrEnterTapped(Player controller, ICard self)
    {
        _ = self;

        // CR 119.4 — life floor at exactly 3 (a payment that brings the
        // total to 0 is legal — SBAs handle the loss-of-game afterward).
        if (controller.LifeTotal < 3) return false;

        var agent = AgentRegistry.Get(controller);
        if (agent == null)
        {
            // No agent registered — decline (same default-decline posture
            // SoporificSpringsFactory / ShockLandCycleFactory use).
            return false;
        }

        bool wantsToPay;
        try
        {
            wantsToPay = agent.ChooseYesNoAsync(
                question: "Pay 3 life so Boggart Bog enters untapped?",
                intent: BotIntent.LoseLife | BotIntent.CostToDecline,
                ct: default)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Defensive: any agent failure → fall back to entering tapped.
            return false;
        }

        if (!wantsToPay) return false;

        // CR 118.8 — pay 3 life.
        controller.LoseLife(3);
        return true;
    }
}
