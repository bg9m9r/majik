using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Turntimber Symbiosis // Turntimber, Serpentine Wood
/// (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "As this land enters, you may pay 3 life. If you don't, it enters
///    tapped."
///   "{T}: Add {G}."
///
/// Front face — <see cref="TurntimberSymbiosisFactory"/> (Sorcery
/// {4}{G}{G}{G}).
///
/// ## MDFC infra
///
/// See <see cref="TurntimberSymbiosisFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Turntimber, Serpentine Wood"</c> and lands here. The card is
/// constructed with its <see cref="MdfcState"/> pre-flipped to the back
/// face so the face tracker reads as authoritative.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {G}</b> mana ability are loaded from the
/// embedded JSON definition (<c>turntimber-serpentine-wood.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the ETB
/// "pay 3 life or enters tapped" replacement are attached in code (the JSON
/// schema models neither).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
/// - <b>{T}: Add {G}</b> — single <see cref="ManaAbility"/> producing one
///   green mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>ETB "you may pay 3 life; if you don't, it enters tapped"
///   (CR 614.1c)</b> — modelled via
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate mirrors
///   <see cref="AgadeemTheUndercryptFactory"/>:
///     <list type="bullet">
///       <item>CR 119.4 — life below 3 → no prompt; enters tapped.</item>
///       <item>Otherwise consult the registered <see cref="IPlayerAgent"/>
///         via <see cref="IPlayerAgent.ChooseYesNoAsync"/>. On "yes" the
///         controller loses 3 life (CR 118.8) and the land enters untapped;
///         on "no" / no-agent / agent-throw it enters tapped.</item>
///     </list>
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture).
///
/// ## References
///
/// - <see cref="AgadeemTheUndercryptFactory"/> — identical "pay 3 life or
///   enters tapped" + tap-for-one-mana back-face land; this factory
///   directly mirrors it (swapping {B} → {G}).
/// </summary>
[CardName("Turntimber, Serpentine Wood")]
public static class TurntimberSerpentineWoodFactory
{
    public const string CardName = "Turntimber, Serpentine Wood";
    public const string FrontName = "Turntimber Symbiosis";

    /// <summary>
    /// Construct Turntimber, Serpentine Wood without a
    /// <see cref="ReplacementBus"/>. The ETB-tapped predicate is omitted;
    /// the {T}: Add {G} mana ability (from JSON) is still wired. Suitable
    /// for card-shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Turntimber, Serpentine Wood with an optional
    /// <see cref="ReplacementBus"/> for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "you may pay 3 life;
    /// if you don't, it enters tapped" replacement is registered
    /// (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {G} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("turntimber-serpentine-wood");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (the land is the back face that actually exists on the
        // battlefield).
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

        // CR 119.4 — a payment bringing the total to 0 is legal; below 3
        // the payment is impossible.
        if (controller.LifeTotal < 3) return false;

        var agent = AgentRegistry.Get(controller);
        if (agent == null)
        {
            // No agent registered — decline (default-decline posture,
            // matching AgadeemTheUndercryptFactory).
            return false;
        }

        bool wantsToPay;
        try
        {
            wantsToPay = agent.ChooseYesNoAsync(
                question: "Pay 3 life so Turntimber, Serpentine Wood enters untapped?",
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
