using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Coldsteel Heart (Coldsnap).
///
/// Snow Artifact, {2}. Oracle text (verified against Scryfall):
///   "This artifact enters tapped.
///    As this artifact enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// <para>
/// The Snow Artifact identity ({2}, Snow supertype, owner / controller
/// wiring) is declared in
/// <c>Majik.Core/CardData/Cards/coldsteel-heart.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. This is the artifact mana-rock analogue
/// of <see cref="TempleOfTheDragonQueenFactory"/>'s "choose a color as it
/// enters" land shape — only the {T} ability's produced color isn't known
/// until the "as this artifact enters, choose a color" decision is made, so
/// the mana ability is wired in the factory once the chosen color is supplied,
/// not declared in JSON.
/// </para>
///
/// <para>
/// ## Choose a color (CR 614.12 — "as this enters" replacement)
/// The production single-arg <see cref="Create(Player)"/> path is AGENT-GATED:
/// it attaches a dynamic-output <see cref="ManaAbility"/> that reads a shared
/// <see cref="ColorChoice"/> holder and stashes the holder in
/// <see cref="ColorChoiceRegistry"/>, so the routed-build overlay
/// (<see cref="ChooseColorPermanentBinder"/>) registers an agent-prompting
/// <see cref="ChooseColorReplacement"/> that stamps the controller's pick onto
/// the holder as the artifact enters (the same machinery the land members of the
/// family — Sunken Citadel, Temple of the Dragon Queen — use via
/// <see cref="ChooseColorLandBinder"/>). Until the choice resolves the holder
/// sits at its seeded default (White), so exactly ONE colour is producible —
/// strictly narrower than the old over-permissive five-WUBRG modelling and never
/// the wrong quantity.
/// </para>
///
/// <para>
/// The explicit-color full overload
/// (<see cref="Create(Player, ManaColor, ReplacementBus?)"/>) is the
/// up-front-resolved test path: callers pass an already-chosen color and the
/// {T} ability produces exactly that color (CR 605.1a — mana abilities don't use
/// the stack), with the unconditional ETB-tapped registered directly when a bus
/// is supplied.
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c)
/// "This artifact enters tapped." is an unconditional ETB-tapped clause. On the
/// production load path it is registered automatically by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (the seed oracle text
/// matches its sentence pattern with no conditional qualifier). When a
/// <see cref="ReplacementBus"/> is supplied to the full overload here, an
/// <see cref="EntersTappedReplacement"/> is registered directly so the
/// behaviour is exercisable in isolation (mirrors the ETB-tapped wiring in
/// <see cref="TempleOfTheDragonQueenFactory"/>, minus the conditional predicate).
/// </para>
/// </summary>
[CardName("Coldsteel Heart")]
public static class ColdsteelHeartFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("coldsteel-heart");

    /// <summary>
    /// Production single-arg path (the overload the routed factory build
    /// invokes — <see cref="FactoryRouting"/>). Attaches a DYNAMIC-output
    /// <see cref="ManaAbility"/> reading a shared <see cref="ColorChoice"/>
    /// holder (seeded White) and stashes the holder in
    /// <see cref="ColorChoiceRegistry"/> so the routed-build overlay
    /// (<see cref="ChooseColorPermanentBinder"/>) can register an agent-prompting
    /// <see cref="ChooseColorReplacement"/> — "as this artifact enters, choose a
    /// color" (CR 614.12). The unconditional ETB-tapped clause is wired in prod
    /// by <see cref="Majik.Core.CardData.EntersTappedBinder"/> via the same
    /// overlay, so it is NOT registered here (no bus is available on this path).
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var artifact = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // CR 614.12 — one shared per-card choice holder seeded to a
        // deterministic default (White), so a pre-ETB / no-agent activation
        // produces exactly ONE colour. The overlay ChooseColorReplacement
        // stamps the agent's real pick as the artifact enters.
        var choice = new ColorChoice(ManaColor.White);
        ColorChoiceRegistry.Set(artifact, choice);

        // "{T}: Add one mana of the chosen color." — a single dynamic-output
        // ManaAbility reading the holder (CR 605.1a). The printed seed (the
        // current chosen colour's pip) lets pre-activation inspectors see a real
        // colour; livePreview keeps ManaGenerated tracking the live choice.
        artifact.AddAbility(new ManaAbility(
            source: artifact,
            controller: owner,
            manaGenerator: () => choice.SinglePip(),
            canActivateCheck: () => !artifact.IsTapped,
            printedManaGenerated: choice.SinglePip(),
            spendRestriction: null,
            livePreview: () => choice.SinglePip()));

        return artifact;
    }

    /// <summary>
    /// Construct a fully-wired Coldsteel Heart.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenColor">The color chosen "as this artifact enters"
    /// (CR 614.12). Must be one of W/U/B/R/G — the {T} ability adds one mana of
    /// that color.</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> for the
    /// unconditional "enters tapped" wiring (CR 614.1c). When <c>null</c>, only
    /// the mana ability is attached (the production load path wires ETB-tapped
    /// via <see cref="Majik.Core.CardData.EntersTappedBinder"/> instead).</param>
    public static Artifact Create(
        Player owner,
        ManaColor chosenColor,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);
        // Validate the colour up front (throws for colourless / generic) so the
        // explicit-color overload's contract is unchanged.
        if (chosenColor is not (ManaColor.White or ManaColor.Blue or ManaColor.Black
            or ManaColor.Red or ManaColor.Green))
        {
            throw new ArgumentOutOfRangeException(
                nameof(chosenColor), chosenColor,
                "Coldsteel Heart's chosen color must be one of W/U/B/R/G (CR 105.1).");
        }

        // Build the dynamic-mana shape, then stamp the already-chosen colour onto
        // the shared holder it reads (CR 614.12 — the choice resolves up front on
        // this test path). The single dynamic ManaAbility then produces exactly
        // that colour; no second static ability is added.
        var artifact = Create(owner);
        ColorChoiceRegistry.Get(artifact)!.Choose(chosenColor);

        // "This artifact enters tapped." — unconditional ETB-tapped (CR 614.1c).
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(artifact));
        }

        return artifact;
    }
}
