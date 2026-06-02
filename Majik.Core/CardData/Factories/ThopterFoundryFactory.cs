using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thopter Foundry (Conflux, {W/B}{U}).
///
/// Artifact. Oracle text (Scryfall, verified):
///   "{1}, Sacrifice a nontoken artifact: Create a 1/1 blue Thopter
///    artifact creature token with flying. You gain 1 life."
///
/// Thopter Foundry is the Sword-of-the-Meek / Time Sieve engine piece:
/// it turns spare artifacts into a stream of flying blue Thopters plus an
/// incidental life trickle. Every shape it needs is an existing engine
/// primitive: the activation reuses <see cref="ManaCostCost"/>("{1}") +
/// <see cref="SacrificeAnArtifactCost"/> with its <c>requireNontoken</c>
/// rider (CR 111.8 — "nontoken artifact"); the token mirrors
/// <see cref="PiaNalaarFactory"/>'s flying Thopter, recoloured blue
/// (CR 105.2 / CR 111.4); and the life gain reuses
/// <see cref="Player.GainLife"/> (CR 119).
///
/// The base shape (name, Artifact, {W/B}{U}) is materialised from the
/// embedded JSON definition (<c>thopter-foundry.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries no
/// abilities — the single activated ability is layered on here (same
/// posture as <see cref="CauldronFamiliarFactory"/>, whose JSON is
/// shape-only).
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {W/B}{U}, owner / controller
///   wired).
/// - <b>{1}, Sacrifice a nontoken artifact activated ability
///   (CR 602.1 / CR 118.5)</b>: costs <see cref="ManaCostCost"/>("{1}") +
///   <see cref="SacrificeAnArtifactCost"/>(<c>requireNontoken: true</c>).
///   The nontoken rider excludes token artifacts from the picker
///   (CR 111.8) — including Thopter Foundry's own Thopter tokens, so the
///   engine can't feed the Foundry its own output. Thopter Foundry IS a
///   nontoken artifact, so when no other nontoken artifact is available
///   the cost can pay itself (the source is not excluded — matching the
///   printed text, which does not say "other than ~"). On resolution:
///   <list type="number">
///     <item>mint a 1/1 <b>blue</b> <see cref="CardSubtype.Thopter"/>
///       creature token with Flying (CR 702.9) via
///       <see cref="TokenFactory.CreateOnBattlefield"/>, then additively
///       stamp <see cref="CardType.Artifact"/> so it reports
///       Artifact + Creature — Thopter (CR 111.1; same multi-type stamp
///       as Pia Nalaar's / Whirler Virtuoso's Thopters); and</item>
///     <item>the controller gains 1 life (CR 119.3 — a discrete life
///       event, fired unconditionally per the printed "You gain 1
///       life").</item>
///   </list>
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The activated ability is
///   attached for shape observability; the Thopter token enters via the
///   no-<see cref="ZoneService"/> branch of
///   <see cref="TokenFactory.CreateOnBattlefield"/> (no
///   <see cref="Majik.Core.Events.CardMovedEvent"/> for the token). This
///   is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?)"/> — token ETBs publish
///   <see cref="Majik.Core.Events.CardMovedEvent"/> via ZoneService so
///   downstream ETB listeners (Soul Warden etc.) fire.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-a-nontoken-artifact target prompt</b>: the
///   <see cref="SacrificeAnArtifactCost"/> picks the first eligible
///   nontoken artifact deterministically (shared v1 sacrifice-picker
///   posture — same gap as Arcbound Ravager / Pia Nalaar). Agent-driven
///   artifact selection is the shared gap, not specific to this card.
/// </summary>
[CardName("Thopter Foundry")]
public static class ThopterFoundryFactory
{
    public const string CardName = "Thopter Foundry";
    public const string Slug = "thopter-foundry";
    public const string PrintedManaCost = "{W/B}{U}";
    public const string ActivationManaCost = "{1}";

    public const string ThopterTokenName = "Thopter";
    public const int ThopterPower = 1;
    public const int ThopterToughness = 1;
    public const int LifeGain = 1;

    /// <summary>
    /// Construct Thopter Foundry with no live runtime services. The
    /// activated ability is attached for shape observability; the Thopter
    /// token enters via direct-zone mutation (no
    /// <see cref="ZoneService"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, zones: null);

    /// <summary>
    /// Construct Thopter Foundry with optional runtime services. When
    /// <paramref name="zones"/> is supplied, each Thopter token's ETB
    /// publishes <see cref="Majik.Core.Events.CardMovedEvent"/> (Soul
    /// Warden etc.).
    /// </summary>
    public static Artifact Create(Player owner, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact,
        // {W/B}{U}). The JSON carries no abilities — the activated ability
        // is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {1}, Sacrifice a nontoken artifact: Create a 1/1 blue Thopter
        // artifact creature token with flying. You gain 1 life.
        // CR 602.1 activated ability; CR 118.5 (sacrifice as a cost);
        // CR 111.8 (nontoken rider); CR 111.1 (artifact-creature token);
        // CR 702.9 (Flying); CR 119.3 (lifegain is a discrete event).
        // ----------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: create a 1/1 blue flying Thopter + gain 1 life",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 111.1 / CR 105.2 — 1/1 BLUE Thopter artifact creature
                // with flying. TokenFactory mints a Creature shell with the
                // explicit blue colour set; stamp Artifact additively for
                // the artifact-creature multi-type (same pattern as Pia
                // Nalaar's / Whirler Virtuoso's Thopters).
                var spec = new TokenFactory.TokenSpec(
                    Name: ThopterTokenName,
                    Power: ThopterPower,
                    Toughness: ThopterToughness,
                    Subtypes: new[] { CardSubtype.Thopter },
                    Keywords: new[] { "Flying" },
                    Colors: new[] { ManaColor.Blue });

                var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);
                token.AddCardType(CardType.Artifact);

                // CR 119.3 — lifegain is a discrete event, fired
                // unconditionally per the printed "You gain 1 life".
                controller.GainLife(LifeGain);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                // CR 111.8 — "Sacrifice a nontoken artifact". The nontoken
                // rider excludes token artifacts (including the Foundry's
                // own Thopter output). The source is NOT excluded — Thopter
                // Foundry is itself a nontoken artifact and the printed text
                // does not say "other than ~", so it can pay itself when no
                // other nontoken artifact is available.
                new SacrificeAnArtifactCost(requireNontoken: true),
            },
            effects: new IEffect[] { effect });

        card.AddAbility(ability);

        return card;
    }
}
