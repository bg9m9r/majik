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
/// Razorgrass Ambush // Razorgrass Field (Modern Horizons 3).
///
/// Land. Oracle text (back):
///   "As this land enters, you may pay 3 life. If you don't, it enters
///    tapped."
///   "{T}: Add {W}."
///
/// Front face — <see cref="RazorgrassAmbushFactory"/> (Instant {1}{W}
/// — "Razorgrass Ambush deals 3 damage to target attacking or blocking
/// creature.").
///
/// ## MDFC infra
/// See <see cref="RazorgrassAmbushFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land, <see cref="NamedCardFactory"/>
/// resolves the back-face name <c>"Razorgrass Field"</c> and lands here.
/// The card is constructed with its <see cref="MdfcState"/> pre-flipped to
/// the back face so the face tracker reads as authoritative.
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/>, no printed subtype.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="SoporificSpringsFactory"/>'s back-face posture).
/// - <b>{T}: Add {W}</b> — single <see cref="ManaAbility"/> producing one
///   white mana (CR 605.1 — mana ability, no stack).
/// - <b>ETB "you may pay 3 life; if you don't, it enters tapped"
///   (CR 614.1c)</b> — modelled via <see cref="ConditionalEntersTappedReplacement"/>
///   on the supplied <see cref="ReplacementBus"/>. Predicate mirrors
///   <see cref="SoporificSpringsFactory"/>:
///     <list type="bullet">
///       <item>CR 119.4 — life below 3 → no prompt; enters tapped.</item>
///       <item>Otherwise consult the registered <see cref="IPlayerAgent"/>
///         via <see cref="IPlayerAgent.ChooseYesNoAsync"/> with intent
///         <see cref="BotIntent.LoseLife"/> | <see cref="BotIntent.CostToDecline"/>.
///         On "yes" the controller loses 3 life (CR 118.8) and the land
///         enters untapped. On "no" / no-agent / agent-throw the land enters
///         tapped.</item>
///     </list>
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture matching
///   <see cref="ShockLandCycleFactory"/>'s no-bus overload).
///
/// ## References
/// - <see cref="SoporificSpringsFactory"/> — same painland-3 ETB shape
///   ({T}: Add {U} swapped to {T}: Add {W}).
/// - <see cref="VolcanicFissureFactory"/> — same painland-3 ETB shape
///   ({T}: Add {R} swapped to {T}: Add {W}).
/// </summary>
[CardName("Razorgrass Field")]
public static class RazorgrassFieldFactory
{
    public const string CardName = "Razorgrass Field";
    public const string FrontName = "Razorgrass Ambush";

    /// <summary>
    /// Construct Razorgrass Field without a <see cref="ReplacementBus"/>.
    /// The ETB-tapped predicate is omitted; the {T}: Add {W} mana ability
    /// is still wired. Suitable for card-shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Razorgrass Field with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "you may pay 3 life;
    /// if you don't, it enters tapped" replacement is registered
    /// (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Razorgrass Field is a vanilla nonbasic land — no basic land
        // subtype, no supertype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Razorgrass Field is the back face that actually exists
        // on the battlefield). Mirrors SoporificSpringsFactory's posture.
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
        // {T}: Add {W}  (CR 605.1 — mana ability, no stack)
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

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
            // as SoporificSpringsFactory / ShockLandCycleFactory).
            return false;
        }

        bool wantsToPay;
        try
        {
            wantsToPay = agent.ChooseYesNoAsync(
                question: "Pay 3 life so Razorgrass Field enters untapped?",
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
