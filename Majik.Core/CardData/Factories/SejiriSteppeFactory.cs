using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sejiri Steppe (Zendikar, colourless cost — a {W}
/// utility land).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, target creature you control gains protection from
///    the color of your choice until end of turn.
///    {T}: Add {W}."
///
/// ## Implementation
///
/// The land shell — name, Land type, and the single <b>{T}: Add {W}</b> mana
/// ability (CR 605.1 — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/sejiri-steppe.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, mirroring the
/// JSON-driven posture of <see cref="SejiriGlacierFactory"/> (the structurally
/// identical "{T}: Add {W}" tapped land).
///
/// The <b>ETB triggered ability</b> — "When this land enters, target creature
/// you control gains protection from the color of your choice until end of
/// turn" (CR 603.1) — is attached in code (the JSON schema models no
/// protection-grant effect). It carries a single 1..1 "target creature you
/// control" <see cref="TargetRequest"/> scoped to the controller's battlefield
/// creatures, and its resolve body reuses the same protection-grant shape as
/// <see cref="SejiriShelterFactory"/> — the Zendikar Rising instant with the
/// byte-identical "target creature you control gains protection from the color
/// of your choice until end of turn" line. See <see cref="Resolve"/>.
///
/// ## Enters tapped (CR 614.1c)
///
/// "This land enters tapped." is an unconditional enters-tapped replacement.
/// On the production load path it is matched off the printed oracle text by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/>; this factory builds
/// the land WITHOUT that replacement — same test-convenience posture as the
/// Refuge cycle and <see cref="SejiriGlacierFactory"/>.
///
/// ## Deferred (v1 gaps) — same posture as <see cref="SejiriShelterFactory"/>
///
/// - <b>Agent-side colour prompt</b>: CR 601.2c+ / CR 700.2a — "of your choice"
///   is a choice the controller makes. v1 uses an injectable
///   <see cref="ColorPicker"/>; the default returns white (an arbitrary but
///   legal WUBRG colour).
/// - <b>Real targeting prompt</b>: the live trigger flow supplies the chosen
///   target through <see cref="TriggeredAbility.SetChosenTargets"/>; the
///   resolver maps the token to the live creature.
/// </summary>
[CardName("Sejiri Steppe")]
public static class SejiriSteppeFactory
{
    public const string CardName = "Sejiri Steppe";
    public const string Slug = "sejiri-steppe";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Resolve-time picker for the protection colour. Receives the controller
    /// and the chosen target; returns one of <see cref="QualityWhite"/>,
    /// <see cref="QualityBlue"/>, <see cref="QualityBlack"/>,
    /// <see cref="QualityRed"/>, <see cref="QualityGreen"/>. Default (when no
    /// picker is supplied) returns <see cref="QualityWhite"/>.
    /// </summary>
    public delegate string ColorPicker(Player controller, ICard target);

    public const string QualityWhite = "white";
    public const string QualityBlue = "blue";
    public const string QualityBlack = "black";
    public const string QualityRed = "red";
    public const string QualityGreen = "green";

    /// <summary>
    /// Construct Sejiri Steppe owned and controlled by <paramref name="owner"/>.
    /// Identity + the {T}: Add {W} mana ability come from JSON; the ETB
    /// "target creature you control gains protection from the color of your
    /// choice" triggered ability is attached in code. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, colorPicker: null);

    /// <summary>
    /// Construct Sejiri Steppe with an optional resolve-time
    /// <paramref name="colorPicker"/> for the protection colour (defaults to
    /// <see cref="QualityWhite"/>).
    /// </summary>
    public static Land Create(Player owner, ColorPicker? colorPicker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ---------------------------------------------------------------
        // ETB triggered ability (CR 603.1):
        //   "When this land enters, target creature you control gains
        //    protection from the color of your choice until end of turn."
        // Single 1..1 "target creature you control" request, gathered live
        // from the controller's battlefield creatures (CR 109.5 — controller
        // resolved at gather time so a control change carries the trigger).
        // ---------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var grantEffect = Fx.Inline(
            $"{CardName}: target creature you control gains protection from the chosen colour EOT",
            () =>
            {
                var controller = land.Controller ?? owner;

                var raw = trigger != null
                    && trigger.ChosenTargets.Count > 0
                    && trigger.ChosenTargets[0].Count > 0
                        ? trigger.ChosenTargets[0][0]
                        : null;

                // The live trigger flow hands us already-resolved live objects
                // via SetChosenTargets, so the resolver is the identity here.
                Resolve(controller, raw, o => o, colorPicker);
            });

        trigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { grantEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    CandidateGatherer: _ =>
                        (land.Controller ?? owner).Zones.Battlefield.GetCards()
                            .Where(c => c.HasType(CardType.Creature))
                            .Cast<object>()
                            .ToList()),
            });

        land.AddAbility(trigger);

        return land;
    }

    /// <summary>
    /// Resolve the protection grant against <paramref name="rawTarget"/>.
    /// Exposed for direct invocation by tests / bots without driving the full
    /// trigger flow.
    /// </summary>
    /// <param name="controller">The trigger's controller — read to enforce
    /// "you control" and to pick the protection colour.</param>
    /// <param name="rawTarget">The chosen target token (or null).</param>
    /// <param name="resolver">Maps the chosen token to the live game object
    /// (expected to be a <see cref="Creature"/> on the battlefield under
    /// <paramref name="controller"/>).</param>
    /// <param name="colorPicker">Optional resolve-time colour picker; defaults
    /// to <see cref="QualityWhite"/>.</param>
    /// <returns>The creature that received the grant, or <c>null</c> when the
    /// target was illegal at resolution (CR 608.2b/608.2c — clean no-op).</returns>
    public static Creature? Resolve(
        Player controller,
        object? rawTarget,
        Func<object, object> resolver,
        ColorPicker? colorPicker = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);
        colorPicker ??= (_, _) => QualityWhite;

        var live = rawTarget is null ? null : resolver(rawTarget);

        // CR 608.2b — target must still be a creature on the battlefield under
        // the controller's control (printed "creature you control").
        if (live is not Creature creature || creature.Zone != ZoneType.Battlefield)
        {
            // CR 608.2c — an ability whose only target is illegal doesn't
            // resolve.
            return null;
        }
        if (creature.Controller != controller)
        {
            return null;
        }
        if (creature.ActiveEffects is null)
        {
            // Shape-only no-op: without a continuous-effects service we can't
            // register the EOT grant.
            return null;
        }

        var color = colorPicker(controller, creature);
        if (string.IsNullOrWhiteSpace(color)) color = QualityWhite;

        // CR 514.2 / CR 613.1f — grant protection from the chosen colour until
        // end of turn. Self-sourced GrantAbilityEffect on the target's
        // ActiveEffects so EOT cleanup runs through the continuous-effects
        // layer. Mirrors SejiriShelterFactory's grant body.
        var grant = new GrantAbilityEffect(
            source: creature,
            target: creature,
            ability: new ProtectionAbility(color),
            expiresAtEndOfTurn: true);
        creature.ActiveEffects.Register(grant);
        // Sync immediately so the protection reads on the same priority window
        // (CR 117.5 / CR 700.2a) — the service computes lazily otherwise.
        grant.Sync();

        return creature;
    }
}
