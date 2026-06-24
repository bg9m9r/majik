using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heraldic Banner (Core Set 2020, {3}).
///
/// Artifact (colourless). Oracle text (verified against Scryfall):
///   "As this artifact enters, choose a color.
///    Creatures you control of the chosen color get +1/+0.
///    {T}: Add one mana of the chosen color."
///
/// <para>
/// This is the "choose a color as it enters" mana-rock + colour-scoped anthem.
/// It combines two existing shapes:
/// <list type="bullet">
///   <item><b>Choose a color + {T} add chosen mana</b> — the
///   <see cref="ColdsteelHeartFactory"/> machinery: a shared mutable
///   <see cref="ColorChoice"/> holder (seeded to a deterministic White default
///   until the agent's pick is stamped), a dynamic-output
///   <see cref="ManaAbility"/> reading the holder (CR 605.1a), and the
///   per-card holder stashed in <see cref="ColorChoiceRegistry"/> so the
///   routed-build overlay (<see cref="ChooseColorPermanentBinder"/>) can
///   register the agent-prompting <see cref="ChooseColorReplacement"/> — "as
///   this artifact enters, choose a color" (CR 614.12).</item>
///   <item><b>Colour-scoped anthem (+1/+0)</b> — "Creatures you control of the
///   chosen color get +1/+0." Registered (when a
///   <see cref="ContinuousEffectsService"/> is supplied, the
///   <see cref="HonorOfThePureFactory"/> posture) as a
///   <see cref="ControllerCreatureAnthemEffect"/> at Layer 7c (CR 613.7c).
///   Because the colour isn't a fixed printed gate but the live CHOSEN colour,
///   the anthem is given a <c>colorProvider</c> reading the same
///   <see cref="ColorChoice"/> holder, so the bonus tracks the agent's pick
///   (CR 614.12).</item>
/// </list>
/// </para>
///
/// <para>
/// Heraldic Banner is NOT snow and does NOT enter tapped — the only
/// differences from Coldsteel Heart's shape beyond the anthem are the {3}
/// colourless cost and the absence of the ETB-tapped clause.
/// </para>
///
/// <para>
/// ## Anthem registration scope (shared v1 posture)
/// As with <see cref="HonorOfThePureFactory"/> / <see cref="IntangibleVirtueFactory"/>,
/// the anthem is registered only when a live <see cref="ContinuousEffectsService"/>
/// is supplied (the full overload / test path). The production single-arg
/// <see cref="Create(Player)"/> path builds the colour-choice + mana shape but
/// leaves anthem registration to the live engine's static wiring, sharing the
/// same deferred control-change / LTB-prune caveats documented on those
/// sibling anthem factories.
/// </para>
/// </summary>
[CardName("Heraldic Banner")]
public static class HeraldicBannerFactory
{
    public const string CardName = "Heraldic Banner";
    public const string Slug = "heraldic-banner";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Production single-arg path (the overload the routed factory build
    /// invokes — <see cref="FactoryRouting"/>). Attaches a DYNAMIC-output
    /// <see cref="ManaAbility"/> reading a shared <see cref="ColorChoice"/>
    /// holder (seeded White) and stashes the holder in
    /// <see cref="ColorChoiceRegistry"/> so the routed-build overlay
    /// (<see cref="ChooseColorPermanentBinder"/>) can register the
    /// agent-prompting <see cref="ChooseColorReplacement"/> — "as this artifact
    /// enters, choose a color" (CR 614.12). No anthem is registered here (no
    /// continuous-effects service is available on this path).
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Heraldic Banner.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Optional layers service. When supplied,
    /// the dynamic colour-scoped anthem ("Creatures you control of the chosen
    /// color get +1/+0", CR 613.7c) is registered against it, reading the live
    /// chosen colour from the card's <see cref="ColorChoice"/> holder. When
    /// null, only the colour-choice + mana shape is built (no live anthem).</param>
    public static Artifact Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var artifact = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        artifact.SetOwner(owner);
        artifact.SetController(owner);

        // CR 614.12 — one shared per-card choice holder seeded to a
        // deterministic default (White), so a pre-ETB / no-agent activation
        // produces exactly ONE colour. The overlay ChooseColorReplacement
        // stamps the agent's real pick as the artifact enters.
        var choice = new ColorChoice(ManaColor.White);
        ColorChoiceRegistry.Set(artifact, choice);

        // "{T}: Add one mana of the chosen color." — a single dynamic-output
        // ManaAbility reading the holder (CR 605.1a / 614.12).
        artifact.AddAbility(new ManaAbility(
            source: artifact,
            controller: owner,
            manaGenerator: () => choice.SinglePip(),
            canActivateCheck: () => !artifact.IsTapped,
            printedManaGenerated: choice.SinglePip(),
            spendRestriction: null,
            livePreview: () => choice.SinglePip()));

        if (continuousEffects != null)
        {
            // CR 613.7c / 614.12 — "Creatures you control of the chosen color
            // get +1/+0." Layer 7c P/T modification scoped to the source's
            // controller, gated on the LIVE chosen colour (the colorProvider
            // reads the same ColorChoice holder the mana ability reads, so the
            // anthem and the mana production always agree on the picked colour).
            continuousEffects.Register(new ControllerCreatureAnthemEffect(
                source: artifact,
                power: 1,
                toughness: 0,
                includeSelf: false,
                colorProvider: () => choice.Chosen));
        }

        return artifact;
    }
}
