using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Sejiri Shelter // Sejiri Glacier (Zendikar Rising, {1}{W}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "Target creature you control gains protection from the color of your
///    choice until end of turn."
///
/// Back face — <see cref="SejiriGlacierFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="MalakirRebirthFactory"/> / <see cref="MalakirMireFactory"/>
/// (the companion ZNR instant // tapped-land MDFC pair).
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>sejiri-shelter.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time spell behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor the protection-grant effect).
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{1}{W}</c>, mono-white, owner / controller wired
///   (from JSON).
/// - <see cref="MdfcState"/> attached (front = "Sejiri Shelter",
///   back = "Sejiri Glacier"); starts on the front face, with a castable
///   land back-face descriptor (cast-either-face).
/// - One 1..1 "target creature you control" <see cref="TargetRequest"/>;
///   the candidate gather is scoped to the caster's battlefield creatures
///   (printed "creature you control").
/// - Resolution (CR 608.2):
///     <list type="bullet">
///       <item><b>CR 608.2b</b> — target must still be a creature on the
///         battlefield under the caster's control.</item>
///       <item><b>Colour pick</b> — "protection from the color of your choice"
///         is chosen by the controller (CR 700.2a / "of your choice"). v1
///         supplies the colour via an injectable
///         <see cref="ColorPicker"/> Func; the dispatcher path defaults to
///         white (an arbitrary but legal WUBRG colour). Same posture as
///         <see cref="ApostlesBlessingFactory"/>'s quality picker.</item>
///       <item><b>Grant</b> — register a self-sourced
///         <see cref="GrantAbilityEffect"/> on the target creature's
///         <see cref="Creature.ActiveEffects"/> that adds a
///         <see cref="ProtectionAbility"/> with the chosen colour until end
///         of turn (CR 514.2 / CR 613.1f). Mirrors
///         <see cref="ApostlesBlessingFactory"/>'s creature grant path.</item>
///     </list>
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent-side colour prompt</b>: CR 601.2c+ — "of your choice" is a
///   choice the controller makes. v1 uses an injectable
///   <see cref="ColorPicker"/>; the dispatcher path defaults to white. When
///   the cast flow grows an agent prompt for free-text colour choices the
///   picker can move out of the factory. Same posture as
///   <see cref="ApostlesBlessingFactory.QualityPicker"/>.
/// - <b>Real targeting prompt</b>: the live cast flow supplies the chosen
///   target through <see cref="ChosenSpellParams.Targets"/>; the resolver
///   maps the token to the live creature. Same posture as
///   <see cref="ApostlesBlessingFactory"/> / <see cref="MalakirRebirthFactory"/>.
///
/// ## References
///
/// - <see cref="ApostlesBlessingFactory"/> — the protection-grant body this
///   factory mirrors (EOT <see cref="GrantAbilityEffect"/> +
///   <see cref="ProtectionAbility"/> registered on a target creature's
///   <see cref="Creature.ActiveEffects"/>).
/// - <see cref="MalakirRebirthFactory"/> — companion ZNR MDFC instant // land
///   pair (JSON-loaded identity + code-attached castable-land MdfcState).
/// </summary>
[CardName("Sejiri Shelter")]
public static class SejiriShelterFactory
{
    public const string CardName = "Sejiri Shelter";
    public const string BackName = "Sejiri Glacier";

    /// <summary>
    /// Resolve-time picker for the protection colour. Receives the caster and
    /// the chosen target; returns one of <see cref="QualityWhite"/>,
    /// <see cref="QualityBlue"/>, <see cref="QualityBlack"/>,
    /// <see cref="QualityRed"/>, <see cref="QualityGreen"/>. Default (when no
    /// picker is supplied) returns <see cref="QualityWhite"/>.
    /// </summary>
    public delegate string ColorPicker(Player caster, ICard target);

    public const string QualityWhite = "white";
    public const string QualityBlue = "blue";
    public const string QualityBlack = "black";
    public const string QualityRed = "red";
    public const string QualityGreen = "green";

    /// <summary>
    /// Construct Sejiri Shelter as an Instant (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached (front face, castable
    /// land back face). The resolve-time <see cref="SpellDefinition"/> is
    /// built on demand via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("sejiri-shelter");
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker WITH a castable land
        // back-face descriptor. The back face is the LAND back face played
        // with no stack; the cast flow offers the controller a face choice at
        // cast time and materializes a fresh back-face land instance when
        // chosen. No transform happens. Mirrors MalakirRebirthFactory.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, _) =>
                SejiriGlacierFactory.Create(landOwner));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "Target creature you control gains protection
    /// from the color of your choice until end of turn."
    /// <see cref="SpellDefinition"/>. Single 1..1 "target creature you control"
    /// request; the resolve closure grants the chosen colour protection until
    /// end of turn.
    /// </summary>
    /// <param name="caster">Spell controller — read at resolve time to scope
    /// the target gather ("you control") and select the protection colour.</param>
    /// <param name="resolver">Target resolver — maps the chosen target token
    /// to the live game object (expected to be a <see cref="Creature"/> on the
    /// battlefield under the caster's control).</param>
    /// <param name="colorPicker">Optional resolve-time picker for the
    /// protection colour; defaults to <see cref="QualityWhite"/>.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ColorPicker? colorPicker = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);
        colorPicker ??= (_, _) => QualityWhite;

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    CandidateGatherer: _ => caster.Zones.Battlefield.GetCards()
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;

                return new IEffect[]
                {
                    Majik.Core.Primitives.Fx.Inline(
                        $"{CardName}: grant target creature protection from the chosen colour EOT",
                        () => Resolve(caster, raw, resolver, colorPicker)),
                };
            });
    }

    /// <summary>
    /// Resolve Sejiri Shelter against <paramref name="rawTarget"/>. Exposed
    /// for direct invocation by tests / bots without driving the full cast
    /// flow.
    /// </summary>
    /// <returns>The creature that received the grant, or <c>null</c> when the
    /// target was illegal at resolution (CR 608.2b/608.2c — clean no-op).</returns>
    public static Creature? Resolve(
        Player caster,
        object? rawTarget,
        Func<object, object> resolver,
        ColorPicker? colorPicker = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);
        colorPicker ??= (_, _) => QualityWhite;

        var live = rawTarget is null ? null : resolver(rawTarget);

        // CR 608.2b — target must still be a creature on the battlefield under
        // the caster's control (printed "creature you control").
        if (live is not Creature creature || creature.Zone != ZoneType.Battlefield)
        {
            // CR 608.2c — a spell whose only target is illegal doesn't resolve.
            return null;
        }
        if (creature.Controller != caster)
        {
            return null;
        }
        if (creature.ActiveEffects is null)
        {
            // Shape-only no-op: without a continuous-effects service we can't
            // register the EOT grant.
            return null;
        }

        var color = colorPicker(caster, creature);
        if (string.IsNullOrWhiteSpace(color)) color = QualityWhite;

        // CR 514.2 / CR 613.1f — grant protection from the chosen colour until
        // end of turn. Self-sourced GrantAbilityEffect on the target's
        // ActiveEffects so EOT cleanup runs through the continuous-effects
        // layer. Mirrors ApostlesBlessingFactory's creature grant path.
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
