using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// Convenience builder for the handful of cards tests construct without
/// hitting the DB. Produces typed Card subclasses with the minimum
/// ability set (basic lands get a tap-for-mana ability inline; other
/// cards are vanilla).
///
/// Production code paths go through <see cref="ScryfallCardFactory"/>
/// instead — that route runs the full data-driven binders against
/// real Scryfall rows.
/// </summary>
public static class NamedCardFactory
{
    public static ICard Create(string name, Player owner)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
        if (owner == null) throw new ArgumentNullException(nameof(owner));

        ICard card = name switch
        {
            // Basic lands — given an inline mana ability so the simplest
            // tests don't need a fake repo just to pay {R}, etc.
            "Mountain" => Land(name, CardSubtype.Mountain),
            "Forest"   => Land(name, CardSubtype.Forest),
            "Plains"   => Land(name, CardSubtype.Plains),
            "Island"   => Land(name, CardSubtype.Island),
            "Swamp"    => Land(name, CardSubtype.Swamp),
            "Wastes"   => Land(name, CardSubtype.Wastes),

            // A few common vanilla creatures the test suite relies on.
            "Grizzly Bears"   => new Creature(name, "1G", 2, 2),
            "Runeclaw Bear"   => new Creature(name, "1G", 2, 2),
            "Hill Giant"      => new Creature(name, "3R", 3, 3),
            "Centaur Courser" => new Creature(name, "2G", 3, 3),

            // Named-card factories — fully-wired cards with real abilities.
            "Walking Ballista" => WalkingBallistaFactory.Create(owner),

            // Creature — Plant Wall {1}{G} 0/5 (WallOfRootsFactory).
            // Mirrodin / many reprints. "Defender. Put a -0/-1 counter on
            // Wall of Roots: Add {G}. Activate only once each turn."
            // Defender keyword marker (CR 702.3) wired. The mana ability is
            // built on the new no-tap ManaAbility overload — the activation
            // cost is the place-counter-on-self side-effect alone, no {T}.
            // canActivateCheck enforces the once-per-turn lock via an
            // int[1] closure (CR 602.5e); additionalCostPayer stamps one
            // CounterType.MinusZeroMinusOne and flips the closure to
            // "used". The single-arg dispatcher path attaches the mana
            // ability with the gate active but never resets the closure;
            // use the (owner, eventBus) overload to wire the
            // TurnStartedEvent reset (CR 500.1). -0/-1 toughness reduction
            // surfaces via ContinuousEffectsService's layer 7c handler
            // (CR 122.1g — counter-handler extended this PR). Modern
            // Yawgmoth combo ramp / Amulet Titan colour-fixer.
            "Wall of Roots" => WallOfRootsFactory.Create(owner),
            "Dryad Arbor"      => DryadArborFactory.Create(owner),
            "Yawgmoth, Thran Physician" => YawgmothFactory.Create(owner),
            "Boseiju, Who Endures" => BoseijuFactory.Create(owner),
            // Artifact — {2}. Static suppression wired via TorporOrbFactory.Create(owner,
            // triggerManager, eventBus) when runtime services are available; this path
            // produces the correct card shape without live suppression (test/vanilla use).
            "Torpor Orb" => TorporOrbFactory.Create(owner),

            // U/B dual surveil land — Murders at Karlov Manor (UndergroundMortuaryFactory).
            // {T}: Add {U} or {B} — two ManaAbility instances wired.
            // ETB trigger: surveil 1 (default-all-graveyard decision) — wired.
            // ETB-tapped restriction + untapped gate on surveil + player prompt deferred.
            "Underground Mortuary" => UndergroundMortuaryFactory.Create(owner),

            // Legendary Creature — Halfling Citizen 1/2 (DelightedHalflingFactory).
            // {T}: Add one mana of any color — 5 ManaAbility instances (one per WUBRG) wired.
            // Usage restriction (legendary-only mana) + "spell can't be countered" rider deferred.
            "Delighted Halfling" => DelightedHalflingFactory.Create(owner),

            // G/B dual land — Bloomburrow (WastewoodVergeFactory).
            // {T}: Add {G} and {T}: Add {B} — two ManaAbility instances wired.
            // {B} activation restriction ("if you control a Swamp or Forest") deferred.
            "Wastewood Verge" => WastewoodVergeFactory.Create(owner),

            // Land — Bloomburrow (SpymastersVaultFactory).
            // {T}: Add {B} — wired.
            // ETB-tapped restriction + connive activated ability deferred.
            "Spymaster's Vault" => SpymastersVaultFactory.Create(owner),

            // Artifact — {1} (VexingBaubleFactory).
            // {1}, {T}, Sacrifice: Draw a card — wired.
            // "Counter free spells" triggered ability deferred.
            "Vexing Bauble" => VexingBaubleFactory.Create(owner),

            // Enchantment — {1}{G} (DredgersInsightFactory).
            // ETB: mill 4, auto-pick first artifact/creature/land → hand.
            // Lifegain trigger: artifact/creature leaves controller's graveyard → gain 1 life.
            // "You may put …" opt-out + batched simultaneous-leavers deferred.
            "Dredger's Insight" => DredgersInsightFactory.Create(owner),

            // Creature — Insect Warrior {1}{G} 3/2 (KraulHarpoonerFactory).
            // Reach keyword wired.
            // ETB Undergrowth: +X/+0 EOT, X = creature cards in controller's graveyard — wired.
            // Targeting (flying creature you don't control) + fight step + "you may" prompt deferred.
            "Kraul Harpooner" => KraulHarpoonerFactory.Create(owner),

            // Creature — Bear {G} 1/1 (BadgermoleCubFactory). Shell only.
            // Earthbend 1 ETB (animate-land mechanic) deferred — no land→creature infra.
            // "Whenever you tap a creature for mana, add {G}" deferred — no tap-watcher infra.
            "Badgermole Cub" => BadgermoleCubFactory.Create(owner),

            // Creature — Orc Archer {1}{B} 1/1 (OrcishBowmastersFactory).
            // Flash keyword wired.
            // ETB damage trigger + opponent-draw watcher + amass Orcs 1 deferred.
            "Orcish Bowmasters" => OrcishBowmastersFactory.Create(owner),

            // Artifact — {2} (AgathasSoulCauldronFactory).
            // {T}: Exile first card from controller's graveyard; if creature, +1/+1 counter
            // on first creature on controller's battlefield — wired (v1 auto-pick).
            // Static mana-color-substitute + ability-grant via imprint deferred.
            "Agatha's Soul Cauldron" => AgathasSoulCauldronFactory.Create(owner),

            // Artifact — {0} (MishrasBaubleFactory).
            // {T}, Sacrifice this artifact: Look at top of target player's library;
            // draw a card at the beginning of the next turn's upkeep — wired.
            // The single-arg overload omits TriggerManager wiring so the delayed
            // draw is no-op (suitable for shape tests). Use the 2-arg overload
            // for fully-wired behavior. Real targeting prompt for "target
            // player's library" deferred.
            "Mishra's Bauble" => MishrasBaubleFactory.Create(owner),

            // Land — Mishra's Workshop (Antiquities, MishrasWorkshopFactory).
            // Printed: "{T}: Add {C}{C}{C}. Spend this mana only to cast
            // artifact spells." v1 ships the 3-colourless tap ability;
            // the "spend only on artifact spells" restriction is
            // structural-only — enforcement deferred until a per-mana
            // provenance ledger exists (CR 106.4). See factory xmldoc.
            "Mishra's Workshop" => MishrasWorkshopFactory.Create(owner),

            // Enchantment — {1}{R} (GoblinBombardmentFactory).
            // Sacrifice a creature: This enchantment deals 1 damage to any
            // target — wired. The shell uses Create(owner) (no pre-bound
            // ping); BuildPingAbility / CreateForBot wire a concrete
            // sacrifice+target activation. Real prompt-driven targeting +
            // ad-hoc cost composition deferred.
            "Goblin Bombardment" => GoblinBombardmentFactory.Create(owner),

            // Legendary Planeswalker — Grist {1}{B}{G} loyalty 3 (GristFactory).
            // Creature type + Insect subtype added unconditionally (v1 simplification).
            // "Only when not on battlefield" conditional layer-4 effect deferred.
            // Loyalty abilities (+1, -2, -5) wired by OracleLoyaltyAbilityBinder.
            "Grist, the Hunger Tide" => GristFactory.Create(owner),

            // DFC front face — Creature — Cat {1}{W} 1/1 (AjaniNacatlPariahFactory).
            // Vigilance keyword marker wired. End-step "may sacrifice another
            // creature, transform" trigger wired against an attached MdfcState
            // (CR 711 / CR 701.28). The single-arg dispatcher path attaches
            // the trigger to the card shape without TriggerManager wiring.
            // Use the (owner, triggers) overload for live trigger firing.
            // Back face (Ajani, Nacatl Avenger PW loyalty 3) tracked via
            // MdfcState.BackFaceName only — back-face loyalty abilities
            // and Layer 0 per-face hot-swap are deferred.
            "Ajani, Nacatl Pariah" => AjaniNacatlPariahFactory.Create(owner),

            // Creature — Elemental Incarnation {3}{W}{W} 3/2 (SolitudeFactory).
            // Flash + Lifelink + Evoke keyword markers wired. ETB exile-target-
            // creature trigger wired (lifegain to target's controller equal to
            // exiled creature's power). Evoke alt-cost = "exile a white card
            // from hand" via EvokeAlternativeCost; printed evoke-sacrifice
            // trigger fires when Solitude enters if evoke was paid (CR 702.74b).
            // Opponent pitch-back ("controller may exile a non-Elemental,
            // non-Incarnation white card from their hand to return") deferred.
            "Solitude" => SolitudeFactory.Create(owner),

            // Creature — Elemental Incarnation {3}{R} 3/3 (FuryFactory).
            // Double strike + Evoke keyword markers wired. Evoke alt-cost =
            // "exile a red card from hand" via EvokeAlternativeCost; printed
            // evoke-sacrifice trigger fires when Fury enters if evoke was paid
            // (CR 702.74b). ETB damage-distribution trigger: X = cards in
            // controller's hand; default distribution sends all X to the first
            // chosen target. Real distribute-damage prompt (CR 601.2d / CR
            // 119.4) is deferred — production callers supply a Func<Player,
            // int, IReadOnlyDictionary<Permanent, int>> via the 2-arg overload.
            "Fury" => FuryFactory.Create(owner),

            // Creature — Elemental Incarnation {2}{B} 3/2 (GriefFactory).
            // Menace + Evoke keyword markers wired. ETB reveal-and-discard
            // trigger wired ("target opponent reveals their hand; you choose
            // a nonland card from it; that player discards it" — v1
            // deterministic first-nonland pick). Evoke alt-cost = "exile a
            // black card from hand" via EvokeAlternativeCost; printed
            // evoke-sacrifice trigger fires when Grief enters if evoke was
            // paid (CR 702.74b). Opponent pitch-back ("counter this triggered
            // ability by exiling a non-Elemental, non-Incarnation black
            // card") deferred.
            "Grief" => GriefFactory.Create(owner),

            // Creature — Elemental Incarnation {1}{G}{G} 3/4 (EnduranceFactory).
            // Flash + Reach + Evoke keyword markers wired. ETB graveyard-to-
            // library trigger wired ("target player shuffles their graveyard
            // into their library" — CR 701.19c). Evoke alt-cost = "exile a
            // green card from hand" via EvokeAlternativeCost; printed evoke-
            // sacrifice trigger fires when Endurance enters if evoke was paid
            // (CR 702.74b).
            "Endurance" => EnduranceFactory.Create(owner),

            // Creature — Human Shaman {1}{G}{G} 2/1 (EternalWitnessFactory).
            // Fifth Dawn + many reprints. ETB triggered ability (CR 603.6a)
            // wired via Triggers.OnEnterBattlefieldSelf with a bespoke 1..1
            // "target card in your graveyard" TargetRequest (ANY card type,
            // distinct from Animate Dead's creature-only graveyard target).
            // On resolution returns the chosen card Graveyard → Hand;
            // single-arg dispatcher path falls back to first-card-in-grave
            // pick when no agent-set target is present (mirrors Wishclaw
            // Talisman / Tasigur). Empty graveyard / illegal target → clean
            // no-op (CR 608.2b). "You may" auto-accepted at v1 (same
            // posture as Tireless Tracker / Phlage / Snapcaster Mage).
            // Use the (owner, zoneService, eventBus, triggers) overload
            // for bus-driven firing + ZoneService-routed return.
            "Eternal Witness" => EternalWitnessFactory.Create(owner),

            // Legendary Artifact — Vehicle {3}{G} 4/4 (EsikasChariotFactory).
            // Kaldheim. ETB trigger: create two 2/2 Cat creature tokens.
            // Attack trigger: create a token that's a copy of target token
            // you control (CR 706 — copiable values snapshotted; v1 lossy,
            // matches existing CopyEffect semantics). Crew 4 (CR 702.122)
            // surfaced via CrewAction integration. Single-arg dispatcher
            // path uses raw zone moves + deterministic first-token-creature
            // fallback for the attack-copy target; use the (owner,
            // zoneService, eventBus, triggers, copyTargetPicker) overload
            // for bus-driven trigger firing + agent-driven token picks.
            // Token colour identity (green) deferred — same gap as
            // Crashing Footfalls' green Rhinos and Wurmcoil's colourless
            // Wurms.
            "Esika's Chariot" => EsikasChariotFactory.Create(owner),

            // Sorcery — {R} (FaithlessLootingFactory). Innistrad / Modern
            // Horizons. "Draw two cards, then discard two cards. Flashback
            // {2}{R}." Card shape only here; the resolve effect (draw 2 +
            // discard 2 via deterministic last-2-in-hand) is built on
            // demand via FaithlessLootingFactory.BuildResolveEffect, and
            // the flashback alt-cost (parsed by FlashbackOracleParser) is
            // exposed via FaithlessLootingFactory.BuildFlashbackCost.
            // Real agent-driven "choose 2 cards to discard" prompt
            // deferred — same queue as Connive / Liliana / Yawgmoth.
            "Faithless Looting" => FaithlessLootingFactory.Create(owner),

            // Sorcery — {1}{R} (FaithlessSalvagingFactory). Phyrexia: All
            // Will Be One. "Discard a card, then draw a card. Flashback—
            // Discard a creature card." Card shape only here; the resolve
            // effect (discard 1 + draw 1 via deterministic first-card-in-
            // hand pick) is built on demand via
            // FaithlessSalvagingFactory.BuildResolveEffect. Flashback alt-
            // cost is non-mana — printed cost is "Discard a creature card"
            // — so v1 splits it (mirrors Cabal Therapy): the
            // FlashbackAlternativeCost carries ManaCost.Zero and the
            // discard rider rides as a paired
            // DiscardACreatureCardAdditionalCost via
            // FaithlessSalvagingFactory.BuildFlashbackAdditionalCosts.
            // Real agent-driven "choose a card to discard" prompt deferred
            // — same queue as Faithless Looting / Liliana / Connive /
            // Psychic Frog.
            "Faithless Salvaging" => FaithlessSalvagingFactory.Create(owner),

            // U/R Horizon Canopy painless dual — Modern Horizons (FieryIsletFactory).
            // {T}, Pay 1 life: Add {U} or {R} — two ManaAbility instances, each with
            // a life-cost activation gate (CR 119.4) and a LoseLife side-effect.
            // {1}, {T}, Sacrifice this land: Draw a card — wired.
            // Sacrifice-cost zone movement deferred (see HorizonLandBinder.AttachSacDraw).
            "Fiery Islet" => FieryIsletFactory.Create(owner),

            // R/W Horizon Canopy painless dual — Modern Horizons (SunbakedCanyonFactory).
            // Same shape as Fiery Islet; only colour differs.
            "Sunbaked Canyon" => SunbakedCanyonFactory.Create(owner),

            // U/R surveil land — Foundations (ThunderingFallsFactory).
            // {T}: Add {U} or {R} — two ManaAbility instances wired.
            // ETB trigger: surveil 1 — default-all-graveyard decision wired.
            // ETB-tapped + surveil player prompt deferred (mirrors Underground Mortuary).
            "Thundering Falls" => ThunderingFallsFactory.Create(owner),

            // R/W surveil land — Foundations (ElegantParlorFactory).
            // Same shape as Thundering Falls; only colour differs.
            "Elegant Parlor" => ElegantParlorFactory.Create(owner),

            // R/W fastland — Kaladesh (InspiringVantageFactory).
            // {T}: Add {R} or {W} — two ManaAbility instances wired.
            // ETB-tapped-unless-two-or-fewer-other-lands handled via
            // ConditionalEntersTappedBinder in the production load path.
            "Inspiring Vantage" => InspiringVantageFactory.Create(owner),

            // U/R fastland — Kaladesh (SpirebluffCanalFactory).
            // {T}: Add {U} or {R} — two ManaAbility instances wired.
            // ETB-tapped-unless-two-or-fewer-other-lands handled via
            // ConditionalEntersTappedBinder in the production load path.
            "Spirebluff Canal" => SpirebluffCanalFactory.Create(owner),

            // W/B fastland — Kaladesh (ConcealedCourtyardFactory).
            // {T}: Add {W} or {B} — two ManaAbility instances wired.
            // ETB-tapped-unless-two-or-fewer-other-lands handled via
            // ConditionalEntersTappedBinder in the production load path.
            "Concealed Courtyard" => ConcealedCourtyardFactory.Create(owner),

            // G/U fastland — Kaladesh (BotanicalSanctumFactory).
            // {T}: Add {G} or {U} — two ManaAbility instances wired.
            // ETB-tapped-unless-two-or-fewer-other-lands handled via
            // ConditionalEntersTappedBinder in the production load path.
            "Botanical Sanctum" => BotanicalSanctumFactory.Create(owner),

            // B/G fastland — Kaladesh (BloomingMarshFactory).
            // {T}: Add {B} or {G} — two ManaAbility instances wired.
            // ETB-tapped-unless-two-or-fewer-other-lands handled via
            // ConditionalEntersTappedBinder in the production load path.
            "Blooming Marsh" => BloomingMarshFactory.Create(owner),

            // Enchantment — {2}{R} (BloodMoonFactory). "Nonbasic lands are
            // Mountains." CR 305.6 / 613.1d. Implemented as a Layer 4
            // SetSubtypesEffect scoped to every nonbasic Land on the
            // battlefield (PR #151) — combined with EffectiveManaAbilities
            // (PR #155) the affected lands lose their printed abilities and
            // tap for {R}. Lifecycle wiring (RetypeLandsStaticEffect ↔
            // CardMovedEvent) requires the live ContinuousEffectsService +
            // EventBus and goes through
            // BloodMoonFactory.Create(owner, effects, eventBus). The
            // single-arg dispatcher path here produces the correct card
            // shape without a live Layer 4 effect (suitable for test /
            // shape-only use).
            "Blood Moon" => BloodMoonFactory.Create(owner),

            // Creature — Human Wizard {2}{R} 2/2 (MagusOfTheMoonFactory).
            // Same "Nonbasic lands are Mountains" Layer 4 effect as Blood
            // Moon (CR 305.6 / 613.1d), wired via the shared
            // RetypeLandsStaticEffect binder. Lifecycle requires the live
            // ContinuousEffectsService + EventBus via
            // MagusOfTheMoonFactory.Create(owner, effects, eventBus); the
            // single-arg dispatcher path here produces the correct card
            // shape only.
            "Magus of the Moon" => MagusOfTheMoonFactory.Create(owner),

            // Creature — Wizard {1}{U} 2/2 (HarbingerOfTheSeasFactory).
            // "Nonbasic lands are Islands" — same scope as Blood Moon,
            // retypes to {Island} instead of {Mountain}. Printed creature
            // type "Merfolk Wizard" — now that CardSubtype.Merfolk exists,
            // a follow-up PR can wire both subtypes; v1 keeps only Wizard.
            // Full lifecycle via HarbingerOfTheSeasFactory.Create(owner, effects, eventBus).
            "Harbinger of the Seas" => HarbingerOfTheSeasFactory.Create(owner),

            // Creature — Merfolk {U}{U} 2/2 (LordOfAtlantisFactory).
            // "Other Merfolk get +1/+1 and have Islandwalk." Symmetric
            // (allPlayers: true) — opponents' Merfolk are also buffed.
            // Islandwalk keyword marker wired (CR 702.14); combat-validator
            // enforcement deferred. Full lifecycle via
            // LordOfAtlantisFactory.Create(owner, continuousEffects).
            "Lord of Atlantis" => LordOfAtlantisFactory.Create(owner),

            // Creature — Merfolk {U}{U} 2/2 (MasterOfThePearlTridentFactory).
            // "Other Merfolk you control get +1/+1 and have Islandwalk."
            // Controller-scoped (allPlayers: false) — opponent's Merfolk
            // are not buffed. Islandwalk keyword marker wired (CR 702.14);
            // combat-validator enforcement deferred. Full lifecycle via
            // MasterOfThePearlTridentFactory.Create(owner, continuousEffects).
            "Master of the Pearl Trident" => MasterOfThePearlTridentFactory.Create(owner),

            // Enchantment — {2}{W}{W} (ConversionFactory).
            // "All Mountains are Plains." Scope: any Land whose subtype
            // set contains Mountain (basic or nonbasic). Retypes to
            // {Plains}. The original card's upkeep "sacrifice unless you
            // pay {W}{W}" clause is deferred — only the Layer 4
            // type-change ships here. Full lifecycle via
            // ConversionFactory.Create(owner, effects, eventBus).
            "Conversion" => ConversionFactory.Create(owner),

            // Legendary Land — Planar Chaos (UrborgTombOfYawgmothFactory).
            // "Each land is a Swamp in addition to its other types."
            // CR 305.7 / 613.1d. Implemented as a Layer 4
            // AddSubtypeToPermanentsEffect scoped to every Land on the
            // battlefield, additively granting {Swamp}. Combined with
            // EffectiveManaAbilities' additive-vs-replacement detection,
            // each affected land keeps its printed mana ability AND gains
            // a {T}: Add {B}. Urborg self-applies (no printed mana →
            // taps for {B} via the granted Swamp subtype). Lifecycle
            // wiring (GrantLandSubtypeStaticEffect ↔ CardMovedEvent)
            // requires the live ContinuousEffectsService + EventBus via
            // UrborgTombOfYawgmothFactory.Create(owner, effects,
            // eventBus). The single-arg dispatcher path here produces
            // the correct card shape only.
            "Urborg, Tomb of Yawgmoth" => UrborgTombOfYawgmothFactory.Create(owner),

            // Legendary Land — Dominaria United (YavimayaCradleOfGrowthFactory).
            // "Each land is a Forest in addition to its other types."
            // Same Layer 4 additive grant machinery as Urborg, only the
            // granted subtype differs ({Forest} instead of {Swamp}).
            // Full lifecycle via
            // YavimayaCradleOfGrowthFactory.Create(owner, effects, eventBus).
            "Yavimaya, Cradle of Growth" => YavimayaCradleOfGrowthFactory.Create(owner),

            // Enchantment — Aura {1}{U} (SpreadingSeasFactory).
            // "Enchant land. When Spreading Seas enters, draw a card.
            //  Enchanted land is an Island and has '{T}: Add {U}'."
            // CR 303.4 / 305.6 / 613.1d — Layer 4 retype scoped to the
            // aura's single attachment target, via
            // AttachedAuraRetypeStaticEffect. PR #155's
            // EffectiveManaAbilities derives {T}: Add {U} from the granted
            // Island subtype. Full lifecycle via
            // SpreadingSeasFactory.Create(owner, effects, eventBus). The
            // single-arg dispatcher path here produces the correct card
            // shape only. Cast-time targeting + attach flow is deferred —
            // tests manually AttachTo() the bearer.
            "Spreading Seas" => SpreadingSeasFactory.Create(owner),

            // Enchantment — Aura {U} (SeasClaimFactory).
            // "Enchant land. Enchanted land is an Island."
            // Same retype machinery as Spreading Seas, no ETB draw.
            "Sea's Claim" => SeasClaimFactory.Create(owner),

            // Creature — Human Wizard {1}{U} 2/1 (SnapcasterMageFactory).
            // Flash keyword + ETB trigger that grants flashback (CR 702.33)
            // to target instant/sorcery in controller's graveyard until EOT;
            // granted cost = the target's printed mana cost. The single-arg
            // dispatcher path here produces the correct card shape without
            // bus-driven EOT-cleanup wiring (suitable for test / shape-only
            // use). Use the (owner, eventBus) overload to enable automatic
            // grant expiration on the next Cleanup step.
            "Snapcaster Mage" => SnapcasterMageFactory.Create(owner),

            // Creature — Lhurgoyf {1}{G} (TarmogoyfFactory).
            // CR 604.3 / 613.2 — Layer 7a characteristic-defining P/T.
            // Power = number of distinct card types across all graveyards;
            // toughness = power + 1. Wired via
            // TarmogoyfFactory.Create(owner, effects, eventBus, graveyardSource)
            // when runtime services are available; the single-arg dispatcher
            // path here produces the correct card shape only (printed 0/1
            // seed, no live CDA).
            "Tarmogoyf" => TarmogoyfFactory.Create(owner),

            // Legendary Creature — Human Shaman {4}{B/G} 4/5
            // (TasigurTheGoldenFangFactory). Khans of Tarkir.
            // Delve marker keyword wired (CR 702.66 — mechanic lives in
            // DelveCost + SpellCastFlow, same as Treasure Cruise / Dig
            // Through Time / Murktide Regent). Activated ability
            // {B}{G}{U}: target opponent picks a card in controller's
            // graveyard → controller's hand. Opponent's IPlayerAgent is
            // consulted via ChooseLibraryPickAsync; first-card fallback
            // when no agent is registered (mirrors Wishclaw Talisman).
            // Single-arg dispatcher path leaves the opponent-choose path
            // as a no-op (no allPlayersResolver); use the
            // (owner, allPlayersResolver, opponentChooser) overload for
            // fully-wired behavior. "Activate only as a sorcery" gate
            // deferred — same gap as Wishclaw Talisman / Priest of Fell
            // Rites (no per-activated-ability sorcery-speed gate yet).
            "Tasigur, the Golden Fang" => TasigurTheGoldenFangFactory.Create(owner),

            // Creature — Human Wizard {1}{B} 2/1 (DarkConfidantFactory).
            // Upkeep trigger: reveal top of controller's library, put it
            // into hand, lose life equal to its mana value. The single-arg
            // path produces the correct card shape without bus-driven
            // trigger registration (suitable for shape tests). Use the
            // (owner, bus, triggers) overload for fully-wired behavior.
            "Dark Confidant" => DarkConfidantFactory.Create(owner),

            // Legendary Planeswalker — Liliana {1}{B}{B} loyalty 3
            // (LilianaOfTheVeilFactory). +1 each-player-discards-a-card,
            // -2 target-player-sacs-a-creature, -6 ultimate (deferred).
            // The single-arg path uses no allPlayersResolver so the +1/-2
            // effects no-op (loyalty change still applies); use the
            // (owner, allPlayersResolver) overload to enable full
            // multi-player effects.
            "Liliana of the Veil" => LilianaOfTheVeilFactory.Create(owner),

            // Legendary Planeswalker — Oko {1}{G}{U} loyalty 4
            // (OkoThiefOfCrownsFactory). +2 create-Food-token, +1
            // target-artifact-or-creature-becomes-3/3-Elk + lose-all-
            // abilities (Layer 4 type-set to Elk Creature + Layer 6 strip
            // + Layer 7b 3/3; v1 colour-set-to-green deferred — no
            // Layer 5 colour-changing primitive yet), -5 exchange-control
            // of target opp-permanent + target your-creature (counter
            // removal deferred — no CR 611 counter surface on Permanent
            // yet). Single-arg dispatcher path attaches all three loyalty
            // bodies; +1/-5 effect bodies gate on the (effects /
            // battlefieldResolver / allPlayersResolver) wiring and no-op
            // when the resolvers aren't supplied (loyalty changes still
            // apply per CR 606.3). The +2 still spawns a Food token even
            // on the single-arg path because TokenFactory operates on
            // controller zones directly.
            "Oko, Thief of Crowns" => OkoThiefOfCrownsFactory.Create(owner),

            // Legendary Planeswalker — Wrenn {R}{G} loyalty 3
            // (WrennAndSixFactory). +1 return land card from graveyard
            // to hand (auto-pick), -1 lands-ping (deferred no-op), -7
            // retrace emblem (structural shell).
            "Wrenn and Six" => WrennAndSixFactory.Create(owner),

            // Legendary Planeswalker — Wrenn {3}{G} loyalty 4
            // (WrennAndRealmbreakerFactory). +1 mill 3 + may-return-
            // land-from-graveyard (auto-pick), -2 reanimate target
            // nonland permanent card from a graveyard (auto-pick,
            // controller's graveyard via the single-arg dispatcher
            // path; multi-graveyard scan via the (owner, zoneService,
            // allPlayersResolver) overload), -7 mints a structural
            // emblem (the basic-land tutor rider on the emblem is
            // deferred — see factory xmldoc).
            "Wrenn and Realmbreaker" => WrennAndRealmbreakerFactory.Create(owner),

            // Sorcery — {R} (WrennsResolveFactory). Murders at Karlov Manor.
            // "Draw two cards. Exile cards drawn this way at the next end
            // step." Card shape only here; the resolve effect (draw 2 +
            // optional delayed-EOT exile rider) is built on demand via
            // WrennsResolveFactory.BuildResolveEffect(caster, triggers?).
            // The single-arg BuildResolveEffect overload draws the two
            // cards without registering the exile rider (suitable for
            // shape tests). Pass a TriggerManager to BuildResolveEffect
            // to wire the DelayedTriggeredAbility that fires on the next
            // End step's StepStartedEvent and exiles any captured cards
            // still in the caster's hand (cards played, discarded, or
            // otherwise relocated are skipped — "cards drawn this way"
            // tracks identity, not current zone).
            "Wrenn's Resolve" => WrennsResolveFactory.Create(owner),

            // Legendary Planeswalker — Karn {7} loyalty 6
            // (KarnLiberatedFactory). +4 target-player-exiles-a-card-from-
            // hand (auto-pick first opponent + first card), -3 exile-
            // target-permanent (auto-pick first candidate). -14 restart-
            // the-game ultimate is DEFERRED as a no-op — CR 720 game
            // restart is engine-foundational. The single-arg dispatcher
            // path here passes no resolvers so +4/-3 no-op; loyalty
            // changes still apply (CR 606.3). Use the (owner,
            // allPlayersResolver, targetResolver) overload to enable the
            // full +4/-3 effects.
            "Karn Liberated" => KarnLiberatedFactory.Create(owner),

            // Legendary Planeswalker — Karn {4} loyalty 5
            // (KarnScionOfUrzaFactory). +1 reveal-top-2 + deterministic
            // split (higher-mv to hand, other to bottom of library;
            // collapses the opponent's pile-separator + controller's
            // pick-pile prompts to an auto-pick, same posture as the
            // rest of the planeswalker family). -1 is DEFERRED to a
            // no-op (loyalty change still applies per CR 606.3) since
            // "exiled with this source" tag tracking isn't wired yet.
            // -2 creates a 0/0 colorless Construct artifact creature
            // token with a CDA Layer 7a "+1/+1 for each artifact you
            // control" effect registered on the supplied
            // ContinuousEffectsService. Single-arg dispatcher path
            // produces the card shape with no live effects service
            // (token is created as a 0/0 — a same-turn SBA pass will
            // graveyard it); use the (owner, zoneService, effects)
            // overload for fully-wired behavior.
            "Karn, Scion of Urza" => KarnScionOfUrzaFactory.Create(owner),

            // Sorcery — {7}{U} (TreasureCruiseFactory).
            // CR 702.66 — Delve. "Delve" marker keyword wired; the cost
            // mechanic itself lives in DelveCost + SpellCastFlow. Resolve
            // effect ("Draw three cards") is built on demand via
            // TreasureCruiseFactory.BuildResolveEffect — the shell here
            // omits it so the dispatcher path produces a shape-only card.
            "Treasure Cruise" => TreasureCruiseFactory.Create(owner),

            // Instant — {6}{U}{U} (DigThroughTimeFactory).
            // Same Delve marker pattern as Treasure Cruise. Resolve
            // effect ("Look at 7, hand 2, bottom 5") wired via
            // DigThroughTimeFactory.BuildResolveEffect with a deterministic
            // default selector (first-two-to-hand); production code can
            // supply a custom selector when agent-driven choose-2 ships.
            "Dig Through Time" => DigThroughTimeFactory.Create(owner),

            // Legendary Planeswalker — Teferi {1}{W}{U} loyalty 4
            // (TeferiTimeRavelerFactory). Printed static "Each opponent
            // can cast spells only any time they could cast a sorcery"
            // (CR 117.1a) — wired via SorcerySpeedRestrictionEffect when
            // the runtime (owner, opponentResolver, targetResolver,
            // effects, eventBus) overload is used. -3 bounce target
            // artifact/creature/enchantment + draw a card (v1 auto-pick).
            // +1 cast-sorceries-as-flash deferred (no controller-keyed
            // cast-time speed modifier yet). The single-arg dispatcher
            // path here produces the correct card shape only.
            "Teferi, Time Raveler" => TeferiTimeRavelerFactory.Create(owner),

            // Creature — Human Wizard {1}{U} 1/2 (LibrarySurveyorFactory).
            // Synthetic Surveil keyword fixture. ETB: surveil 2 (CR 701.42).
            // No printed Modern-legal Surveil card had a clean enough
            // isolated trigger to ship inside the v1 keyword scope; this
            // synthetic creature exercises the surveil_self effect path
            // alongside the existing Underground Mortuary / Thundering
            // Falls / Elegant Parlor surveil-lands.
            "Library Surveyor" => LibrarySurveyorFactory.Create(owner),

            // Creature — Faerie Dragon {U}{R} 1/1 (SpriteDragonFactory).
            // Ikoria: Lair of Behemoths. Flying KeywordAbility marker
            // (CR 702.9). Cast-noncreature-spell trigger (CR 603.1 / 122.1)
            // fires on a SpellCastEvent whose ISpell.Controller matches
            // Sprite Dragon's controller AND whose ISpell.Card lacks
            // CardType.Creature; effect adds a CounterType.PlusOnePlusOne
            // counter on Sprite Dragon (same predicate shape as
            // ProwessFactory, but counters instead of pump — accumulates
            // across turns with no per-turn cap). Single-arg dispatcher
            // path attaches the trigger without TriggerManager registration;
            // (owner, triggers) overload wires bus-driven firing.
            // Introduces CardSubtype.Faerie (Dragon already present).
            "Sprite Dragon" => SpriteDragonFactory.Create(owner),

            // Creature — Bird Advisor {1}{U} 1/3 (LedgerShredderFactory).
            // Flying keyword wired. Two triggered abilities surfaced on the
            // card for shape: (1) "whenever you cast the second spell each
            // turn, surveil 1" — predicate increments a per-turn closure
            // counter and matches on the exact 2nd cast; (2) "whenever
            // Ledger Shredder surveils, put a +1/+1 counter on it" — chained
            // inline off trigger 1's effect (no SurveilEvent on the bus yet).
            // The single-arg dispatcher path here produces the correct card
            // shape without bus-driven turn reset or trigger-manager wiring.
            // Use the (owner, bus, triggers) overload for fully-wired behavior.
            "Ledger Shredder" => LedgerShredderFactory.Create(owner),

            // Enchantment — {1}{G} (UpTheBeanstalkFactory).
            // Two triggered abilities surfaced on the card: (1) ETB →
            // controller draws a card (CardMovedEvent → battlefield); and
            // (2) whenever the controller casts a spell with mana value
            // 5+ → controller draws a card (SpellCastEvent gated on
            // controller + ISpell.Card.ManaCostValue.TotalValue >= 5).
            // Single-arg dispatcher produces the correct card shape without
            // trigger-manager wiring; use the (owner, triggers) overload
            // for end-to-end firing.
            "Up the Beanstalk" => UpTheBeanstalkFactory.Create(owner),

            // Creature — Human Rogue {1}{U} 1/1 (TestConniverFactory).
            // Synthetic Connive keyword fixture. ETB: connive (CR 701.50).
            // Draws + discards + +1/+1 counter if nonland was discarded.
            // No printed Modern-legal Connive card had a clean enough
            // isolated trigger — Ledger Shredder's second-spell-per-turn
            // and Raffine's combat riders go beyond the v1 keyword scope.
            "Test Conniver" => TestConniverFactory.Create(owner),

            // Creature — Zombie {1}{B} 1/1 (LazotepRecruitFactory).
            // Synthetic Amass keyword fixture (loosely modelled on the
            // War of the Spark Lazotep cycle). ETB: amass Zombies 1
            // (CR 701.49) — creates a 0/0 black Zombie Army token when
            // the controller has no Army, then places a +1/+1 counter on it.
            "Lazotep Recruit" => LazotepRecruitFactory.Create(owner),

            // Instant — {3}{U}{U} (ForceOfWillFactory).
            // "You may pay 1 life and exile a blue card from your hand
            //  rather than pay this spell's mana cost. Counter target spell."
            // Card shape only here; the counter-target SpellDefinition is
            // attached via ForceOfWillFactory.BuildDefinition at the
            // SpellCastFlow resolver wire-up site. Pitch alt-cost (CR 118.9)
            // = exile a blue card from hand + 1 life when it's not your
            // turn (PitchAlternativeCost). Bot probe via PitchAltCostProbe.
            "Force of Will" => ForceOfWillFactory.Create(owner),

            // Instant — {1}{U}{U}{U} (CrypticCommandFactory).
            // CR 700.2d — modal "Choose two —" with 4 printed modes
            // (counter spell / bounce permanent / tap-all-opponents-creatures /
            // draw a card). The single-arg dispatcher path produces the
            // correct card shape; the bound SpellDefinition is built on
            // demand via CrypticCommandFactory.BuildDefinition(caster,
            // targetResolver, stack). Multi-pick relies on the caller
            // populating ChosenSpellParams.ModeIndexes — the runtime
            // honours either the list or the scalar ModeIndex shape.
            "Cryptic Command" => CrypticCommandFactory.Create(owner),

            // Instant — {1}{U}{U} (ForceOfNegationFactory).
            // "If it's not your turn, you may exile a blue card from your
            //  hand rather than pay this spell's mana cost. Counter target
            //  noncreature spell."
            // Same shape as Force of Will minus the life rider, and the
            // counter is gated to noncreature spells (CR 608.2b illegal-
            // target check at resolution).
            "Force of Negation" => ForceOfNegationFactory.Create(owner),

            // Instant — {1}{R} (TemurBattleRageFactory). Khans of Tarkir.
            // "Target creature gains double strike until end of turn.
            //  Ferocious — That creature also gains trample until end of
            //  turn if you control a creature with power 4 or greater."
            // Card shape only here; the resolve-time SpellDefinition
            // (double-strike grant + ferocious trample rider) is built on
            // demand via TemurBattleRageFactory.BuildSpellDefinition.
            "Temur Battle Rage" => TemurBattleRageFactory.Create(owner),

            // Instant — {1}{U} (NegateFactory). Various sets.
            // "Counter target noncreature spell."
            // Card shape only here; the resolve-time SpellDefinition
            // (counter target noncreature spell) is built on demand via
            // NegateFactory.BuildSpellDefinition.
            "Negate" => NegateFactory.Create(owner),

            // Instant — {2}{G}{G} (ForceOfVigorFactory).
            // "If it's not your turn, you may exile a green card from your
            //  hand rather than pay this spell's mana cost. Destroy up to
            //  two target artifacts and/or enchantments."
            // Same pitch pattern as Force of Negation but green-flavoured
            // (no life rider). Resolve effect delegates to the shared
            // DestroyUpToArtifactEnchantmentSpell — identical behaviour to
            // the data-driven oracle-template binding.
            "Force of Vigor" => ForceOfVigorFactory.Create(owner),

            // Instant — {0} (PactOfNegationFactory). Future Sight.
            // "Counter target spell.
            //  At the beginning of your next upkeep, pay {3}{U}{U}.
            //  If you don't, you lose the game."
            // Card shape only here; the resolve-time SpellDefinition is
            // built on demand via PactOfNegationFactory.BuildDefinition,
            // which counters the target spell (CR 701.5) and — when a
            // TriggerManager is supplied — registers a delayed upkeep
            // trigger (CR 603.7) that calls PayMana({3}{U}{U}) and falls
            // back to MarkLost() on failure (CR 104.3 / CR 118.3).
            "Pact of Negation" => PactOfNegationFactory.Create(owner),

            // Instant — {0} (SlaughterPactFactory). Future Sight.
            // "Destroy target nonblack creature.
            //  At the beginning of your next upkeep, pay {2}{B}.
            //  If you don't, you lose the game."
            // Card shape only here; the resolve-time SpellDefinition is
            // built on demand via SlaughterPactFactory.BuildDefinition,
            // which destroys the target nonblack creature (CR 701.7 +
            // CR 105 color check via CardColors) and — when a
            // TriggerManager is supplied — registers a delayed upkeep
            // trigger (CR 603.7) that calls PayMana({2}{B}) and falls
            // back to MarkLost() on failure (CR 104.3 / CR 118.3).
            "Slaughter Pact" => SlaughterPactFactory.Create(owner),

            // Instant — {0} (PactOfTheTitanFactory). Future Sight.
            // "Create a 4/4 red Giant creature token.
            //  At the beginning of your next upkeep, pay {4}{R}.
            //  If you don't, you lose the game."
            // Card shape only here; the resolve-time SpellDefinition is
            // built on demand via PactOfTheTitanFactory.BuildDefinition,
            // which creates the 4/4 Giant token under the caster
            // (CR 111 / CR 111.6) and — when a TriggerManager is
            // supplied — registers a delayed upkeep trigger (CR 603.7)
            // that calls PayMana({4}{R}) and falls back to MarkLost()
            // on failure (CR 104.3 / CR 118.3). Token "red" colour
            // identity deferred — same gap as Crashing Footfalls'
            // "green" tokens.
            "Pact of the Titan" => PactOfTheTitanFactory.Create(owner),

            // Instant — {B/P} (SurgicalExtractionFactory).
            // "Choose target card in a graveyard other than a basic land
            //  card. Search its owner's graveyard, hand, and library for
            //  any number of cards with the same name as that card and
            //  exile them. Then that player shuffles."
            // Phyrexian-mana alt cost (2 life) via
            // PhyrexianManaAlternativeCost. Card shape only here; the
            // resolve effect is built on demand via
            // SurgicalExtractionFactory.BuildDefinition at the
            // SpellCastFlow resolver wire-up site.
            "Surgical Extraction" => SurgicalExtractionFactory.Create(owner),

            // Artifact Creature — Horror {2} 0/4 (SpellskiteFactory).
            // "{U/P}: Change the target of target spell or ability with a
            //  single target to Spellskite."
            // CR 107.4f / 118.8 — Phyrexian pip ({U/P}) modelled as two
            // parallel ActivatedAbilities: pay {U} (ManaCostCost) or pay
            // 2 life (AdditionalCost.PayLife). v1 redirects only spells
            // (single-target), rewriting Spell.ChosenTargets — ability-
            // target redirect deferred (same gap as RedirectTemplate).
            "Spellskite" => SpellskiteFactory.Create(owner),

            // Instant — {U} (VaporSnagFactory). New Phyrexia.
            // "Return target creature to its owner's hand. Its controller
            //  loses 1 life." Bounce effect + 1 life loss wired via
            //  VaporSnagFactory.BuildDefinition. Single-arg dispatcher
            //  produces the correct card shape; pass a ZoneService to
            //  BuildDefinition for replacement-bus-aware zone moves.
            "Vapor Snag" => VaporSnagFactory.Create(owner),

            // Instant — {R/P} (GutShotFactory). New Phyrexia.
            // "({R/P} can be paid with either {R} or 2 life.)
            //  Gut Shot deals 1 damage to any target." Main cost {R};
            //  Phyrexian alt-cost (2 life) via GutShotFactory.PhyrexianAlternativeCost.
            //  1 damage to any target via OracleSpellBinder.DealDamage in
            //  GutShotFactory.BuildDefinition.
            "Gut Shot" => GutShotFactory.Create(owner),

            // Instant — {1}{B/P}{B/P} (DismemberFactory). New Phyrexia.
            // "({B/P} can be paid with either {B} or 2 life each.)
            //  Target creature gets -5/-5 until end of turn." Main cost
            //  {1}{B}{B}; Phyrexian alt-cost (4 life + {1}) via
            //  DismemberFactory.PhyrexianAlternativeCost. -5/-5 EOT via
            //  PumpUntilEndOfTurnEffect in DismemberFactory.BuildDefinition.
            "Dismember" => DismemberFactory.Create(owner),

            // Sorcery — {2}{R} (RiftBoltFactory). 3 damage to any target;
            // Suspend 1—{R} (CR 702.62). Spell-def and suspend alt cost
            // built on demand via RiftBoltFactory.BuildSpellDefinition /
            // BuildSuspendCost — caller wires them through SpellCastFlow
            // and SuspendedCardRegistry.
            "Rift Bolt" => RiftBoltFactory.Create(owner),

            // Sorcery — {2}{G} (SearchForTomorrowFactory). Time Spiral.
            // "Search your library for a basic land card, put it onto
            //  the battlefield, then shuffle your library.
            //  Suspend 2—{G}." (CR 702.62). The land enters untapped
            // (unlike Path to Exile / Scapeshift). Spell-def and suspend
            // alt cost built on demand via
            // SearchForTomorrowFactory.BuildSpellDefinition /
            // BuildSuspendCost. Library shuffle deferred — same gap as
            // other search effects (no IZone.Shuffle).
            "Search for Tomorrow" => SearchForTomorrowFactory.Create(owner),

            // Instant — {W} (PathToExileFactory). Conflux. "Exile target
            // creature. Its controller may search their library for a
            // basic land card, put that card onto the battlefield
            // tapped, then shuffle their library." CR 701.21 (exile) +
            // CR 701.19a (search). Card shape only here; the resolve-
            // time SpellDefinition (target-creature request + exile +
            // basic-land tutor offered to the exiled creature's
            // controller) is built on demand via
            // PathToExileFactory.BuildSpellDefinition. Shuffle deferred
            // — same MVP gap as every other tutor (no IZone.Shuffle).
            "Path to Exile" => PathToExileFactory.Create(owner),

            // Instant — {R} (UnholyHeatFactory). Modern Horizons 2.
            // "Unholy Heat deals 2 damage to any target. Delirium —
            //  Unholy Heat deals 4 damage to that target instead if
            //  there are four or more card types among cards in your
            //  graveyard." CR 702.105. Card shape only here; the
            // resolve-time SpellDefinition (with the delirium gate
            // sampling the controller's graveyard) is built on demand
            // via UnholyHeatFactory.BuildSpellDefinition. Reuses
            // TarmogoyfFactory.CountDistinctCardTypes for the type count.
            "Unholy Heat" => UnholyHeatFactory.Create(owner),

            // Instant — {R} (BurstLightningFactory). Zendikar / Modern Masters.
            // "Kicker {4}. Burst Lightning deals 2 damage to any target. If
            //  Burst Lightning was kicked, it deals 4 damage to that target
            //  instead." CR 702.33. Card shape only here; the resolve-time
            // SpellDefinition is built on demand via
            // BurstLightningFactory.BuildSpellDefinition(resolver, wasKicked).
            // Kicker primitive is DEFERRED (see factory xmldoc) — no
            // IAdditionalCost shape for Kicker yet, no "was kicked" bit
            // plumbed through SpellCastFlow, and no OracleSpellBinder /
            // KeywordAnalyzer awareness. Production casts ship as
            // not-kicked (2 damage); the wasKicked branch is structural.
            "Burst Lightning" => BurstLightningFactory.Create(owner),

            // Instant — {R}{W} (LightningHelixFactory). Ravnica: City of Guilds /
            // Modern Horizons. "Lightning Helix deals 3 damage to any target and
            // you gain 3 life." Card shape only here; the resolve-time
            // SpellDefinition (single any-target damage + controller lifegain)
            // is built on demand via LightningHelixFactory.BuildSpellDefinition.
            // Damage is dispatched through SearingBlazeFactory.DealDamageWithPlaneswalker
            // so Player / Creature / Planeswalker targets all work.
            "Lightning Helix" => LightningHelixFactory.Create(owner),

            // Sorcery — {2}{R} (WheelOfFortuneFactory). Limited Edition Alpha /
            // Revised. "Each player discards their hand, then draws seven cards."
            // Card shape only here; the resolve-time effect is built on demand
            // via WheelOfFortuneFactory.BuildResolveEffect(allPlayers). Distinct
            // from the shuffle-wheel template (Day's Undoing / Time Reversal /
            // Echo of Eons / Emergency Powers) which routes through
            // SpellTemplates.Templates.Library.WheelTemplate — Wheel of Fortune
            // discards into graveyard rather than shuffling hand+graveyard into
            // library, so it needs its own factory.
            "Wheel of Fortune" => WheelOfFortuneFactory.Create(owner),

            // Instant — {R}{R} (SearingBlazeFactory). Worldwake / Modern Horizons.
            // "Searing Blaze deals 1 damage to target player or planeswalker
            //  and 1 damage to target creature that player or that
            //  planeswalker's controller controls. Landfall — If you had a
            //  land enter the battlefield under your control this turn,
            //  Searing Blaze deals 3 damage to that player or planeswalker
            //  and 3 damage to that creature instead." CR 702.142. Card
            // shape only here; the resolve-time SpellDefinition (with the
            // landfall gate sampling the controller's TurnState) is built
            // on demand via SearingBlazeFactory.BuildSpellDefinition.
            // The 2nd target's "controlled by" relationship is V1-relaxed
            // (declared as "target creature") — the engine's targeting
            // prompt can't yet express "controlled by the previous target".
            "Searing Blaze" => SearingBlazeFactory.Create(owner),

            // Instant — {R} (GalvanicDischargeFactory). Modern Horizons 3.
            // "Galvanic Discharge deals X damage to any target, where X is
            //  1 plus the number of charge counters on artifacts and/or
            //  lands you control." Card shape only here; the resolve-time
            // SpellDefinition is built on demand via
            // GalvanicDischargeFactory.BuildSpellDefinition. Counts charge
            // counters on every Permanent the controller controls whose
            // type set includes Artifact or Land (artifact creatures +
            // artifact lands both count); opponent permanents and non-
            // artifact/land creatures are excluded.
            "Galvanic Discharge" => GalvanicDischargeFactory.Create(owner),

            // Creature — Zombie {B}{B}{B} 3/2 (GeralfsMessengerFactory).
            // Dark Ascension. "Geralf's Messenger enters tapped. When
            // Geralf's Messenger enters, target opponent loses 2 life.
            // Undying." (CR 614.1c + CR 603.6a + CR 119.3 + CR 702.93.)
            // ETB-tapped replacement registered via EntersTappedReplacement
            // on the supplied ReplacementBus (single-arg dispatcher path
            // omits the replacement — Messenger enters untapped on shape-
            // only paths, mirroring Creeping Tar Pit / Valakut). ETB
            // triggered ability with a 1..1 "target opponent" TargetRequest
            // (mirrors Hidetsugu's Second Rite). Undying via the shared
            // UndyingFactory.Build helper. Undying return re-applies the
            // enters-tapped replacement per CR 614.1c. Use the
            // (owner, eventBus, triggers, replacements) overload for fully-
            // wired behavior.
            "Geralf's Messenger" => GeralfsMessengerFactory.Create(owner),

            // Sorcery — {1}{R} (TribalFlamesFactory). Onslaught / Modern Horizons 2.
            // "Tribal Flames deals X damage to any target, where X is the
            //  number of basic land types among lands you control." CR 702.16
            //  (Domain). Card shape only here; the resolve-time SpellDefinition
            //  is built on demand via TribalFlamesFactory.BuildSpellDefinition,
            //  which uses ContinuousEffectsService.Compute(land).Subtypes when
            //  a live layers service is supplied so layer-4 retypes (Blood
            //  Moon, Spreading Seas, Urborg, Yavimaya) feed through.
            "Tribal Flames" => TribalFlamesFactory.Create(owner),

            // Instant — {R} (TibaltsTrickeryFactory). Kaldheim.
            // "Counter target spell. Its controller mills three cards, then
            //  exiles cards from the top of their library until they exile
            //  a nonland card that shares a card type with it. They may
            //  cast that card without paying its mana cost. Then they put
            //  all cards exiled this way that weren't cast on the bottom
            //  of their library in a random order." CR 701.5 (counter) +
            //  CR 701.13 (mill) + CR 308.2 (shared card type). Card shape
            //  only here; the resolve-time SpellDefinition is built on
            //  demand via TibaltsTrickeryFactory.BuildSpellDefinition.
            //  The optional "may cast for free" rider is delivered through
            //  an onResolved callback (mirrors CrashingFootfallsFactory's
            //  cascade-resolved hook) — production callers wire a
            //  CastFromExileAlternativeCost + SpellCastFlow path; the
            //  default leaves the eligible card in exile until the bottom
            //  step sweeps it into the random-order pile.
            "Tibalt's Trickery" => TibaltsTrickeryFactory.Create(owner),

            // Artifact — {1} (PithingNeedleFactory).
            // "As Pithing Needle enters, choose a card name. Activated
            //  abilities of sources with the chosen name can't be
            //  activated unless they're mana abilities." (CR 602.5c / 605.)
            // Wired via PithingNeedleStaticEffect when the runtime
            // (owner, nameSelector, eventBus) overload is used. The
            // single-arg dispatcher path here produces the correct card
            // shape only.
            "Pithing Needle" => PithingNeedleFactory.Create(owner),

            // Creature — Kor Artificer {1}{W} 1/2 (StoneforgeMysticFactory).
            // ETB tutor: search library for an Equipment card → hand
            // (deterministic first-match; shuffle deferred). Activated
            // {1}{W}, {T}: put an Equipment card from hand directly onto
            // the battlefield, then attach to a creature you control
            // (CR 113.6c / 117.1a — alt-zone "cast"). The single-arg
            // dispatcher path here produces the correct card shape; the
            // activated ability uses raw zone moves (no ZoneService).
            // Use the (owner, zoneService, eventBus, triggers) overload
            // for full ETB-replacement / trigger-firing wiring.
            "Stoneforge Mystic" => StoneforgeMysticFactory.Create(owner),

            // Sorcery — {2}{U} (ShowAndTellFactory). Urza's Saga.
            // "Each player may put an artifact, creature, enchantment, or
            //  land card from their hand onto the battlefield."
            // Card shape only at the dispatcher; the per-player resolve
            // effect (iterate allPlayers, deterministic first-permanent
            // pick + optional ZoneService routing so ETB triggers /
            // replacements on the put-in permanent fire — CR 603.6a /
            // CR 614) is built on demand via
            // ShowAndTellFactory.BuildResolveEffect(allPlayers, zoneService,
            // picker). Real "may"-decline + per-player permanent-choice
            // prompt deferred (same queue as Stoneforge Mystic / Sun Titan).
            "Show and Tell" => ShowAndTellFactory.Create(owner),

            // Enchantment — {2}{R} (SneakAttackFactory). Urza's Saga.
            // "{R}: You may put a creature card from your hand onto the
            //  battlefield. That creature gains haste. Sacrifice it at the
            //  beginning of the next end step." CR 602 — repeatable {R}
            //  activated ability. Each activation closes over its own
            //  resolve-time creature pick and (when a TriggerManager is
            //  wired) its own delayed end-step sacrifice (CR 603.7) so
            //  multiple activations in the same turn each sacrifice their
            //  cheated-in creature. v1 deterministic first-creature-in-hand
            //  pick (auto-accepts the "you may" when a candidate exists —
            //  same shape as Aether Vial / Through the Breach / Goblin
            //  Lackey). Haste granted via the standard EOT-scoped keyword
            //  grant (observationally equivalent to a no-duration grant
            //  given the creature is sac'd at the same boundary).
            //  Single-arg dispatcher path produces the correct card shape
            //  without ZoneService / TriggerManager wiring; use the
            //  (owner, zoneService, triggers) overload for fully-wired
            //  behaviour.
            "Sneak Attack" => SneakAttackFactory.Create(owner),

            // Creature — Human Soldier {1}{W} 2/2 (PuresteelPaladinFactory).
            // ETB-draw trigger: whenever an Equipment enters under controller's
            // control, draw a card (CR 603.1 — "you may" simplified to a
            // forced draw in v1). Conditional static: while controller has
            // ≥3 artifacts on battlefield, Equipment they control have
            // equip {0}; tracked via ZeroEquipCostEffect lifecycle and read
            // back through IsZeroEquipActiveFor(player). The single-arg
            // dispatcher path here produces card shape + ETB trigger; the
            // (owner, eventBus, triggers) overload also attaches the
            // zero-equip-cost lifecycle.
            "Puresteel Paladin" => PuresteelPaladinFactory.Create(owner),

            // Creature — Ooze {1}{G} 2/2 (ScavengingOozeFactory).
            // Activated {G}: exile target creature card from a graveyard;
            // if you do, put a +1/+1 counter on Scavenging Ooze and gain
            // 1 life. The single-arg dispatcher path scans only the
            // controller's graveyard; use the (owner, allPlayersResolver)
            // overload to scan every player's graveyard. Real "target
            // creature card from a graveyard" prompt deferred — v1 picks
            // the first creature card deterministically.
            "Scavenging Ooze" => ScavengingOozeFactory.Create(owner),

            // Creature — Zombie Knight {1}{B}{B} 2/3 (MurderousRiderFactory).
            // Throne of Eldraine Adventure card. v1 ships the creature side
            // (Lifelink keyword marker — CR 702.15) + a Swift End helper
            // exposed via MurderousRiderFactory.BuildAdventureSpell that
            // returns a destroy-target-creature-or-planeswalker SpellDefinition
            // + self-life-loss 2 (CR 119.3). The Adventure cast-from-hand-to-
            // exile pipeline (CR 715) is deferred — same gap as
            // BonecrusherGiantFactory; the printed "when this dies, exile it"
            // self-exile LTB clause is also deferred (no card-local death
            // replacement surface yet).
            "Murderous Rider" => MurderousRiderFactory.Create(owner),

            // Sorcery — {2}{G}{G} (ScapeshiftFactory). Morningtide.
            // "Sacrifice any number of lands. Search your library for that
            //  many land cards, put them onto the battlefield, then shuffle."
            // (CR 701.16 + CR 701.19a). Card shape only at the dispatcher;
            // resolve closure built on demand via
            // ScapeshiftFactory.BuildResolveEffect(caster, sacSelector,
            // tutorSelector). The selectors decouple "pick a subset of
            // permanents to sacrifice" + multi-card library tutor from agent
            // surfaces the engine does not yet expose. The tutor side falls
            // back to PrimevalTitan's per-slot agent loop when no selector
            // is supplied; the sacrifice side defaults to zero lands (clean
            // no-op faithful to the lower bound of "any number"). Lands
            // enter UNTAPPED per the printed oracle — distinct from
            // Primeval Titan / Cultivate. Modern Titanshift combo finisher
            // (pairs with Valakut, the Molten Pinnacle). Library shuffle
            // deferred — same rationale as SearchSpellFactory.
            "Scapeshift" => ScapeshiftFactory.Create(owner),

            // Creature — Dragon {3}{U}{U} 3/3 (MurktideRegentFactory).
            // Flying + Delve marker keywords wired. ETB trigger: exile target
            // instant or sorcery card from a graveyard; enters with +1/+1
            // counters = delve-exiled-count + (ETB exile ? 1 : 0) per CR 122.1g.
            // The delve count is plumbed via Card.PendingDelveExiledCount,
            // stamped by SpellCastFlow when DelveCost is paid and consumed
            // by the ETB effect.
            "Murktide Regent" => MurktideRegentFactory.Create(owner),

            // Creature — Zombie Fish {7}{B} 5/5 (GurmagAnglerFactory). Khans of Tarkir.
            // Delve marker keyword only — no printed triggers or activated abilities.
            // The delve mechanic itself lives in DelveCost + SpellCastFlow; cast via
            // the cast-flow's delveCost parameter to substitute exiled graveyard cards
            // for generic mana (CR 702.66). Bot-side delve discovery deferred — same
            // gap as Treasure Cruise / Murktide Regent.
            "Gurmag Angler" => GurmagAnglerFactory.Create(owner),

            // Enchantment — {1}{U} (DressDownFactory). Flash. CR 613.6 + 613.7b:
            // "Creatures lose all abilities and have base power and toughness
            // 1/1." End-step sacrifice trigger wired (CR 500.4 / CR 603.1).
            // The single-arg dispatcher path here attaches the Flash keyword
            // and the end-step trigger to the card shape; live continuous-
            // effects + TriggerManager wiring requires the (owner, effects,
            // eventBus, triggers, creaturePoolSource) overload. Candidate-pool
            // snapshot semantics — creatures entering AFTER Dress Down are
            // not scoped by v1.
            "Dress Down" => DressDownFactory.Create(owner),

            // Sorcery — {G} (AncientStirringsFactory). Rise of the Eldrazi.
            // "Look at the top five cards of your library. You may reveal a
            //  colorless card from among them and put it into your hand. Then
            //  put the rest on the bottom of your library in a random order."
            // Card shape only here; the resolve effect is built on demand via
            // AncientStirringsFactory.BuildResolveEffect with a default
            // selector that picks the first colourless peeked card (CR 105)
            // and shuffles the remainder for the random-order bottom placement
            // (CR 701.20a). The ImpulseMayRevealFilterTemplate also matches
            // the oracle text by shape, but drops the colour filter — the
            // named factory carries the predicate locally.
            "Ancient Stirrings" => AncientStirringsFactory.Create(owner),

            // Instant — {U} (StubbornDenialFactory). Khans of Tarkir.
            // "Choose one — Counter target noncreature spell unless its
            //  controller pays {1}. Ferocious — Counter that spell if you
            //  control a creature with power 4 or greater." CR 702.114.
            // Card shape only here; the resolve-time SpellDefinition (with
            // the ferocious gate and the unless-pay fallback) is built on
            // demand via StubbornDenialFactory.BuildSpellDefinition. The
            // unless-pay rider is surfaced as a Func<bool> callback because
            // the engine has no Yes/No agent prompt yet — default is "no".
            "Stubborn Denial" => StubbornDenialFactory.Create(owner),

            // Instant — {U} (ConsiderFactory). Innistrad: Midnight Hunt.
            // "Look at the top card of your library. You may put that card
            // into your graveyard. Then draw a card." Effectively Surveil 1
            // (CR 701.42) + draw 1. Card shape only here; the resolve effect
            // is built on demand via ConsiderFactory.BuildResolveEffect and
            // splices into a SpellDefinition.EffectFactory. The surveil
            // decision is sourced from the registered IPlayerAgent (via
            // AgentRegistry) when available; the default fall-back sends the
            // peeked card to the graveyard.
            "Consider" => ConsiderFactory.Create(owner),

            // Sorcery — {U} (PonderFactory). Lorwyn / Modern Horizons 3.
            // "Look at the top three cards of your library, then put them
            //  back in any order. You may shuffle your library. Draw a card."
            // Card shape only here; the resolve effect is built on demand
            // via PonderFactory.BuildResolveEffect. Reuses ScryAction with
            // ToBottom = [] for the reorder. "May shuffle" rider deferred
            // (no IZone.Shuffle entry point yet).
            "Ponder" => PonderFactory.Create(owner),

            // Sorcery — {U} (PreordainFactory). Magic 2011 / Modern Horizons 3.
            // "Scry 2, then draw a card." Card shape only here; the resolve
            // effect is built on demand via PreordainFactory.BuildResolveEffect.
            // The data-driven oracle binder's ScryNSpell already covers this
            // shape via tail detection — this named factory exists for direct
            // construction in tests / dispatch parity with other cantrips.
            "Preordain" => PreordainFactory.Create(owner),

            // Instant — {B} (CabalRitualFactory). Torment / Modern Horizons 2.
            // "Add {B}{B}{B}. Threshold — Add {C}{C}{C}{C}{C} instead if seven
            // or more cards are in your graveyard." Card shape only here; the
            // resolve effect is built on demand via
            // CabalRitualFactory.BuildResolveEffect and splices into a
            // SpellDefinition.EffectFactory. Threshold (CR 702.50) is sampled
            // against the controller's graveyard at resolution; produced mana
            // is added to the controller's mana pool via AddManaToPool, with
            // {C} routed into the generic bucket per ManaCost.Parse
            // (CR 107.4c).
            "Cabal Ritual" => CabalRitualFactory.Create(owner),

            // Instant — {B} (DarkRitualFactory). Alpha and many reprints.
            // "Add {B}{B}{B}." Card shape only here; the resolve effect is
            // built on demand via DarkRitualFactory.BuildResolveEffect and
            // splices into a SpellDefinition.EffectFactory. Sibling of
            // Cabal Ritual minus the threshold clause.
            "Dark Ritual" => DarkRitualFactory.Create(owner),

            // Instant — {2}{R}{R} (ThroughTheBreachFactory). Champions of
            // Kamigawa. "You may put a creature card from your hand onto
            // the battlefield. That creature gains haste until end of
            // turn. Sacrifice that creature at the beginning of the next
            // end step. Splice onto Arcane {1}{R}{R}{R}." Card shape only
            // here; the resolve effect (put first-creature-in-hand →
            // battlefield, grant Haste EOT via
            // GrantKeywordUntilEndOfTurnEffect, and register a delayed
            // end-step sacrifice DelayedTriggeredAbility — CR 603.7) is
            // built on demand via ThroughTheBreachFactory.BuildResolveEffect.
            // Splice onto Arcane (CR 702.46) is DEFERRED — no splice alt-
            // cost primitive in the engine yet (same gap as every other
            // Splice card).
            "Through the Breach" => ThroughTheBreachFactory.Create(owner),

            // Legendary Enchantment — {X}{B}{B} (TheMeathookMassacreFactory).
            // Innistrad: Midnight Hunt. "When The Meathook Massacre enters,
            // all creatures get -X/-X until end of turn. Whenever a creature
            // an opponent controls dies, you gain 1 life. Whenever a creature
            // you control dies, each opponent loses 1 life." Three triggered
            // abilities wired: (1) ETB sweep reads X from Card.PendingCastX
            // (stamped by SpellCastFlow at cast time) and registers a
            // PumpUntilEndOfTurnEffect(-X, -X) on every creature on every
            // player's battlefield (via the optional allPlayersResolver;
            // falls back to controller-only without it — same convention as
            // Pernicious Deed); (2) opponent-creature dies → controller
            // gains 1 life; (3) own-creature dies → each opponent supplied
            // by the optional opponentResolver loses 1 life (resolver shape
            // mirrors Sheoldred, the Apocalypse). Single-arg dispatcher
            // path attaches all three triggers for shape and resolves the
            // ETB sweep against the controller's battlefield only; own-dies
            // drain silently no-ops without a resolver.
            "The Meathook Massacre" => TheMeathookMassacreFactory.Create(owner),

            // Artifact — {0} (LotusPetalFactory). Tempest and many reprints.
            // "{T}, Sacrifice Lotus Petal: Add one mana of any color."
            // Five ManaAbility instances (one per WUBRG), each gated on
            // !IsTapped + Zone == Battlefield and carrying an inline
            // additionalCostPayer that performs the CR 701.16 sacrifice
            // (controller's battlefield → owner's graveyard). CR 605.1 —
            // the activation remains a mana ability (no stack). Modal
            // single-ability "any colour" shape deferred (same gap as
            // Mox Opal / Delighted Halfling / City of Brass).
            "Lotus Petal" => LotusPetalFactory.Create(owner),

            // Creature — Human Cleric {W}{B} 2/1 (PriestOfFellRitesFactory).
            // ETB triggered ability: reanimate target creature card with
            // mana value 3 or less from controller's graveyard
            // (deterministic first-match v1). Graveyard-activated
            // unearth-style ability: {2}{W}{B}, Exile this from
            // graveyard: reanimate target creature card from controller's
            // graveyard. "Activate only as a sorcery" timing gate is
            // deferred — engine has no per-activated-ability sorcery-
            // speed restriction yet. The single-arg dispatcher path
            // uses raw zone moves; use the (owner, zoneService,
            // eventBus, triggers) overload for ETB-trigger / ZoneService
            // wiring on the reanimated creature.
            "Priest of Fell Rites" => PriestOfFellRitesFactory.Create(owner),

            // Sorcery — {B} (ReanimateFactory). Tempest.
            // "Put target creature card from a graveyard onto the
            //  battlefield under your control. You lose life equal to its
            //  mana value." Card shape only here; the resolve effect is
            // built on demand via ReanimateFactory.BuildResolveEffect
            // (deterministic first-creature pick scoped to the caster's
            // graveyard via the single-arg path; multi-graveyard scan via
            // the (caster, zoneService, allPlayersResolver) overload).
            "Reanimate" => ReanimateFactory.Create(owner),

            // Enchantment — Aura {1}{B} (AnimateDeadFactory). Limited Edition Alpha.
            // "Enchant creature card in a graveyard. When Animate Dead
            //  enters, if it's on the battlefield, it loses 'enchant
            //  creature card in a graveyard' and gains 'enchant creature
            //  put onto the battlefield with Animate Dead'. Return
            //  enchanted creature card to the battlefield under your
            //  control and attach Animate Dead to it. When Animate Dead
            //  leaves the battlefield, that creature's controller
            //  sacrifices it. Enchanted creature gets -1/-0."
            // v1 simplification: the ETB mode-shift on the Enchant clause
            // is collapsed into a single resolve effect (reanimate + auto-
            // attach), so the runtime never observes the aura with the
            // "enchant graveyard card" predicate on the battlefield. The
            // -1/-0 static is registered via AttachedBoostEffect (Layer
            // 7c) when the runtime (owner, continuousEffects, zoneService,
            // eventBus, triggers) overload is used; the LTB-sacrifice
            // trigger is registered when eventBus + triggers are supplied.
            "Animate Dead" => AnimateDeadFactory.Create(owner),

            // Creature — Elemental Incarnation {3}{U} 3/3 (SubtletyFactory).
            // Modern Horizons 2 incarnation, blue counterpart to Solitude.
            // Flash + Evoke keyword markers wired. ETB bounce trigger wired:
            // returns target opponent's creature/planeswalker to its owner's
            // hand, then that owner does a 1-card "look + may bottom" scry
            // decision sourced from their registered IPlayerAgent. Evoke
            // alt-cost = "exile a blue card from hand" via EvokeAlternativeCost;
            // printed evoke-sacrifice trigger fires when Subtlety enters if
            // evoke was paid (CR 702.74b).
            "Subtlety" => SubtletyFactory.Create(owner),

            // Creature — Avatar {B} 13/13 (DeathsShadowFactory).
            // CR 604.3 / 613.2 — Layer 7a characteristic-defining P/T.
            // P/T = clamp(13 - controller life, 0, 13). Wired via
            // DeathsShadowFactory.Create(owner, effects, eventBus) when
            // runtime services are available; the single-arg dispatcher
            // path here produces the correct card shape only (printed 13/13
            // seed, no live CDA).
            "Death's Shadow" => DeathsShadowFactory.Create(owner),

            // Artifact Creature — Phyrexian Wurm {6} 6/6 (WurmcoilEngineFactory).
            // Deathtouch + Lifelink keyword markers wired. Dies trigger
            // (CR 603.6c / 700.4) creates two 3/3 Phyrexian Wurm artifact
            // creature tokens — one with Deathtouch and one with Lifelink.
            // The single-arg dispatcher path here produces the correct
            // card shape without TriggerManager registration / ZoneService
            // wiring on the spawned tokens. Use the (owner, zoneService,
            // eventBus, triggers) overload for fully-wired behavior.
            "Wurmcoil Engine" => WurmcoilEngineFactory.Create(owner),

            // Legendary Planeswalker — Karn {4} loyalty 5
            // (KarnTheGreatCreatorFactory). Printed static "Activated
            // abilities of artifacts your opponents control can't be
            // activated" wired via OpponentArtifactActivatedSuppressionEffect
            // when the (owner, effects, eventBus, battlefieldResolver,
            // wishSelector) overload is used. +1 animate-noncreature-artifact
            // registers Layer 4 type-add + Layer 7b BecomesPTEffect (or
            // shim for non-Creature C# targets); -2 wishboard accepts a
            // Func<Player, ICard?> selector returning an artifact "outside
            // the game" or face-up exiled artifact owned by Karn's
            // controller. Single-arg dispatcher path produces shape only
            // (no live static, +1 no-ops without effects/board).
            "Karn, the Great Creator" => KarnTheGreatCreatorFactory.Create(owner),

            // Enchantment — {1}{W} (SigardasAidFactory). Eldritch Moon.
            // "Equipment and Auras you control have flash." +
            // "Whenever an Equipment enters under your control, you may
            //  attach it to target creature you control."
            // Flash-grant lifecycle wired via FlashGrantStaticEffect (a new
            // FlashGrantRegistry mirrors CastingRestrictions — TimingRules
            // consults it after the printed Instant/Flash check). The
            // ETB-attach trigger is attached for shape; pass an event bus +
            // TriggerManager via the overload for fully-wired lifecycle and
            // bus-driven trigger firing. v1 auto-picks the first
            // controller-side creature as the attach target (CR 701.3a
            // prompt deferred — same as Stoneforge Mystic).
            "Sigarda's Aid" => SigardasAidFactory.Create(owner),

            // Artifact — {1} (AmuletOfVigorFactory). Worldwake.
            // "Whenever a permanent enters tapped under your control,
            //  untap it." Triggered ability over CardMovedEvent →
            // Battlefield; condition gates on controller + Permanent +
            // IsTapped (ZoneService taps before publishing, so IsTapped
            // is already true at trigger-evaluation time per CR 614.6).
            // Single-arg dispatcher path attaches the ability for shape
            // tests; use the (owner, triggers) overload to wire up
            // bus-driven firing.
            "Amulet of Vigor" => AmuletOfVigorFactory.Create(owner),

            // Sorcery — {1}{G} (SylvanScryingFactory). "Search your library
            // for a land card, reveal it, put it into your hand, then
            // shuffle." (CR 701.19a). Tutors ANY land — basic or nonbasic —
            // which is the Tron-enabling distinction vs. Cultivate-style
            // basic-only tutors. The resolve-time SpellDefinition is built
            // on demand via SylvanScryingFactory.BuildSpellDefinition,
            // which delegates to the shared SearchSpellFactory.SearchLibrarySpell
            // ("land") so the agent prompt + pick→hand machinery is shared
            // with template-bound land tutors. Shuffle deferred (no
            // IZone.Shuffle entry point yet — same rationale as the rest
            // of SearchSpellFactory).
            "Sylvan Scrying" => SylvanScryingFactory.Create(owner),

            // Instant — {U} (MysticalTutorFactory). Mirage and reprinted.
            // "Search your library for an instant or sorcery card, reveal
            //  it, put it on top of your library, then shuffle." (CR 701.19a).
            // The resolve-time SpellDefinition is built on demand via
            // MysticalTutorFactory.BuildSpellDefinition. Pick destination
            // is top-of-library (index 0) via IZone.InsertCardAt, not hand
            // — distinguishes it from the shared SearchSpellFactory path
            // used by Sylvan Scrying / Stoneforge Mystic. Shuffle deferred
            // (no IZone.Shuffle entry point yet — same rationale as the
            // rest of SearchSpellFactory).
            "Mystical Tutor" => MysticalTutorFactory.Create(owner),

            // Instant — {B} (VampiricTutorFactory). Visions and reprinted.
            // "Search your library for a card, then shuffle. Put that card
            //  on top. You lose 2 life." (CR 701.19a / 701.19c / 119.3).
            // Sibling of MysticalTutorFactory: no type predicate (any card
            // is a legal pick), pick destination is top-of-library (index 0)
            // via IZone.InsertCardAt, and a 2-life loss fires
            // unconditionally after the (optional) tutor step. The
            // resolve-time SpellDefinition is built on demand via
            // VampiricTutorFactory.BuildSpellDefinition. Shuffle deferred
            // (no IZone.Shuffle entry point yet — same rationale as the
            // rest of SearchSpellFactory / MysticalTutorFactory).
            "Vampiric Tutor" => VampiricTutorFactory.Create(owner),

            // Sorcery — {1}{R}{G}{W} (CrashingFootfallsFactory). Modern Horizons.
            // CR 702.85 — Cascade. On-cast triggered ability fires
            // CascadeAction.Cascade with sourceManaValue = 4 (exile from top
            // until a nonland with MV < 4 is found, bottom the rest in random
            // order, leave the eligible card in exile so the caller can drive
            // a CastFromExileAlternativeCost cast). Resolve effect creates
            // two 4/4 green Rhino Warrior creature tokens with Trample. The
            // single-arg dispatcher path attaches the trigger to the card
            // shape without TriggerManager wiring; use the (owner, triggers,
            // willCast, onCascadeResolved) overload to drive the free cast.
            "Crashing Footfalls" => CrashingFootfallsFactory.Create(owner),

            // Creature — Giant {4}{G}{G} 6/6 (PrimevalTitanFactory).
            // Trample keyword wired. ETB + attack triggered abilities both
            // tutor up to two lands → battlefield tapped (CR 603.1, CR
            // 508.1f, CR 701.19a). "Up to two" composes the existing single-
            // land tutor primitive twice — the agent picks zero or one land
            // per slot (decline returns null, CR 701.19a). The single-arg
            // dispatcher path uses the agent-driven default selector and
            // does NOT register the triggers with a TriggerManager. Use the
            // (owner, triggers, selector) overload for fully-wired trigger
            // registration or deterministic test selectors. Library shuffle
            // deferred (no IZone.Shuffle entry point yet — same rationale
            // as SearchSpellFactory).
            "Primeval Titan" => PrimevalTitanFactory.Create(owner),

            // Creature — Giant {4}{W}{W} 6/6 (SunTitanFactory). Magic 2011
            // mythic cycle counterpart to Primeval Titan. Vigilance keyword
            // wired (CR 702.20). ETB + attack triggered abilities both
            // reanimate target permanent card with mana value 3 or less from
            // controller's graveyard to the battlefield (CR 603.1, CR 508.1f).
            // v1 picks the first eligible permanent card deterministically;
            // "you may" defaults to taking the action when an eligible
            // candidate exists. "Permanent card" (CR 110.4) accepts any
            // artifact / creature / enchantment / land / planeswalker; it
            // excludes instant and sorcery cards. The single-arg dispatcher
            // path uses raw zone moves; use the (owner, zoneService,
            // eventBus, triggers) overload for ZoneService-routed moves so
            // ETB triggers on the reanimated permanent fire (CR 603.6a).
            "Sun Titan" => SunTitanFactory.Create(owner),

            // Legendary Creature — Elder Giant {1}{G}{U} 6/6 (UroTitanFactory).
            // Theros Beyond Death. Three triggered abilities surfaced on the
            // card: (1) "When Uro enters, sacrifice it unless it escaped" —
            // Escape (CR 702.143) is not wired in v1, so the rider is
            // structurally collapsed to "always sacrifice on ETB" faithful
            // to the printed hardcast case (CR 603.1 + CR 701.16); (2)/(3)
            // ETB + attack — gain 3 life (CR 119.3), draw a card (CR 121.1),
            // then may put a land card from hand onto the battlefield (CR
            // 113.6c). v1 deterministic first-land-in-hand pick (auto-accepts
            // the "may" when a candidate exists — same shape as Aether Vial
            // / Sneak Attack / Through the Breach). The single-arg dispatcher
            // path uses raw zone moves; use the (owner, zoneService, eventBus,
            // triggers) overload for ZoneService routing on the played land
            // (so ETB triggers fire — CR 603.6a) and TriggerManager-driven
            // stack placement. Escape alt-cost (cast from graveyard + exile
            // five other graveyard cards) is deferred — no graveyard cast
            // alt-cost + multi-card-exile additional-cost primitive yet.
            // "Elder" subtype not in CardSubtype — Giant is wired.
            "Uro, Titan of Nature's Wrath" => UroTitanFactory.Create(owner),

            // Legendary Creature — Elemental Incarnation {2}{R}{W} 4/4
            // (PhlageFactory). Modern Horizons 3. "When Phlage enters, it
            // deals 3 damage to any target and you gain 3 life. Escape—
            // {2}{R}{W}, Exile three other cards from your graveyard."
            // ETB triggered ability declares a 1..1 "any target"
            // TargetRequest; on resolution deals 3 damage (Player /
            // Creature / Planeswalker via DealDamageWithPlaneswalker) and
            // the controller gains 3 life (CR 119 / CR 119.3). Same shape
            // as Lightning Helix's resolve, lifted onto a creature ETB.
            // Escape alt-cost deferred (same gap as Uro — no cast-from-
            // graveyard alt-cost + multi-card-exile additional-cost
            // primitive yet).
            "Phlage, Titan of Fire's Fury" => PhlageFactory.Create(owner),

            // Land — Urza's Mine (Antiquities, Urza Tron cycle).
            // {T}: Add {C}. If controller controls an Urza's Mine, an
            // Urza's Power-Plant, AND an Urza's Tower, add {2} instead.
            // Wired via TronLandHelper.ComputeManaAddition (controller-
            // only battlefield scan) plumbed through the Func<ManaCost>
            // ManaAbility overload, so the amount is decided at
            // activation time against live battlefield state.
            "Urza's Mine" => UrzasMineFactory.Create(owner),

            // Land — Urza's Tower (Antiquities). Same shape as Urza's
            // Mine — only the printed subtype differs (Tower).
            "Urza's Tower" => UrzasTowerFactory.Create(owner),

            // Land — Urza's Power-Plant (Antiquities). Same shape as
            // Urza's Mine — only the printed subtype differs
            // (PowerPlant).
            "Urza's Power-Plant" => UrzasPowerPlantFactory.Create(owner),

            // Sorcery — {2}{B}{B}{B} (LivingEndFactory). Time Spiral.
            // "Cascade. Each player exiles all creature cards from their
            //  graveyard, then sacrifices all creatures they control, then
            //  puts all cards they exiled this way onto the battlefield."
            // Card shape only here; the resolve-time SpellDefinition is
            // built on demand via LivingEndFactory.BuildSpellDefinition,
            // which routes each reanimate move through ZoneService so ETB
            // triggers fire on every reanimated permanent (CR 603.6a —
            // PR #165, #174 plumbing). The single-arg dispatcher path
            // attaches the Cascade (CR 702.85) on-cast trigger for shape
            // inspection but does not register it with a TriggerManager;
            // use the LivingEndFactory.Create(owner, triggers, willCast,
            // onCascadeResolved) overload for fully-wired bus firing
            // (mirrors CrashingFootfallsFactory).
            "Living End" => LivingEndFactory.Create(owner),

            // Legendary Creature — Cat Nightmare {W}{B} 3/2 (LurrusOfTheDreamDenFactory).
            // Lifelink keyword wired. Static ability surfaced with description
            // "During each of your turns, you may cast one permanent spell
            // with mana value 2 or less from your graveyard." Companion deck-
            // construction rule (CR 702.139) deferred — that is a deck-builder
            // foundational concern, not a runtime gameplay one. The runtime
            // grave-cast gate (per-turn budget, mv ≤ 2, permanent-only,
            // controller's-turn-only) is exposed via
            // LurrusOfTheDreamDenFactory.GetGate(card); callers compose it
            // with a GraveyardCastAlternativeCost via BuildAlternativeCost.
            // The single-arg dispatcher path here produces the correct card
            // shape without bus-driven turn-boundary reset wiring (suitable
            // for shape tests). Use the (owner, eventBus) overload to enable
            // automatic per-turn budget reset on TurnStartedEvent.
            "Lurrus of the Dream-Den" => LurrusOfTheDreamDenFactory.Create(owner),

            // Artifact — {1} (AetherVialFactory). Darksteel.
            // Upkeep trigger: add a charge counter (v1 auto-accept). {T}:
            // put a creature card from hand with mv = charge counters
            // onto the battlefield (v1 deterministic first match,
            // auto-accept on "you may"). The single-arg dispatcher path
            // attaches both abilities to the card shape without
            // TriggerManager registration or ZoneService routing; use
            // the (owner, zoneService, eventBus, triggers) overload for
            // bus-driven upkeep firing and ETB-trigger routing on the
            // placed creature (CR 603.6a).
            "Aether Vial" => AetherVialFactory.Create(owner),

            // Artifact — {1} (AetherSpellbombFactory). Mirrodin.
            // "{U}, Sacrifice this artifact: Return target creature to its
            // owner's hand."
            // "{1}, Sacrifice this artifact: Draw a card."
            // Two activated abilities — both sac the bomb. The bounce
            // declares a single TargetRequest (resolution-time creature
            // guard catches illegal picks; CR 608.2b). The cantrip mode is
            // a vanilla {1} + sac + draw 1. Sacrifice is performed by the
            // effect closure because the generic AdditionalCost.Pay
            // sacrifice path is a stub (mirrors Mishra's Bauble).
            "Aether Spellbomb" => AetherSpellbombFactory.Create(owner),

            // Land — Aether Hub (Kaladesh, AetherHubFactory).
            // Oracle: "Aether Hub enters with an energy counter on it.
            // {T}: Add {C}. {T}, Pay {E}: Add one mana of any color."
            // ETB trigger (CR 603.6a) grants the controller one energy
            // (CR 106.13 — player-scoped resource via Player.GainEnergy)
            // AND stamps a CounterType.Energy marker on the land for
            // shape inspection. {T}: Add {C} wired as a ManaAbility.
            // {T}, Pay {E}: Add one mana of any color modelled as 5
            // ManaAbility instances (one per WUBRG) carrying the
            // additional-cost overload — gated on
            // controller.EnergyCounters >= 1 (CR 119.4) and the
            // additionalCostPayer spends one energy via PayEnergy(1)
            // after the {T} tap pays out. Single-arg dispatcher path
            // attaches the ETB trigger without TriggerManager
            // registration; (owner, eventBus, triggers) overload wires
            // bus-driven firing. See factory xmldoc.
            "Aether Hub" => AetherHubFactory.Create(owner),

            // Artifact — Equipment {1} (ColossusHammerFactory).
            // Static "equipped creature gets +10/+0 and loses flying" via
            // AttachedBoostEffect (Layer 7c) + LoseKeywordEffect("Flying")
            // (Layer 6). Equip {8} activated ability wired (sorcery-speed
            // restriction enforced via action-validator, deferred at this
            // ability level). The single-arg dispatcher path produces the
            // correct card shape only; use the (owner, continuousEffects)
            // overload for live boost / lose-flying registration.
            "Colossus Hammer" => ColossusHammerFactory.Create(owner),

            // Artifact — Equipment {1} (SkullclampFactory). Darksteel.
            // Static "equipped creature gets +1/-1" + dies trigger (CR 603.6c /
            // CR 700.4): "Whenever equipped creature dies, draw two cards."
            // Equip {1} activated ability wired. The single-arg dispatcher
            // path here produces the correct card shape; use the (owner,
            // continuousEffects, triggers) overload for live boost +
            // bus-driven dies-trigger wiring.
            "Skullclamp" => SkullclampFactory.Create(owner),

            // Creature — Frog Mutant {U}{B} 1/3 (PsychicFrogFactory).
            // Modern Horizons 3. Flying keyword marker (CR 702.9) wired.
            // Combat-damage-to-a-player trigger (CR 510 / CR 603.1) loots N
            // (draws N + discards N) where N = combat damage dealt to a
            // player — v1 deterministic first-card-in-hand pick for each
            // discard, empty-library halts draw via
            // MarkTriedToDrawFromEmptyLibrary, empty-hand halts discard
            // cleanly. Activated ability "Discard a card: +1/+1 counter on
            // Psychic Frog" (CR 602) wired via DiscardACardCost +
            // CounterType.PlusOnePlusOne. The single-arg dispatcher path
            // here produces the correct card shape without TriggerManager
            // wiring; use the (owner, triggers) overload for bus-driven
            // combat-damage firing. Discard prompt deferred (same queue as
            // Liliana / Faithless Looting / Sword of Feast and Famine).
            "Psychic Frog" => PsychicFrogFactory.Create(owner),

            // Artifact — Equipment {3} (SwordOfFeastAndFamineFactory).
            // Mirrodin Besieged. Static "+2/+2" via AttachedBoostEffect (Layer
            // 7c). "Has protection from black and from green" granted to the
            // equipped creature via two AttachedAuraAbilityGrantStaticEffect
            // lifecycles (one ProtectionAbility per colour); Protection lookup
            // (Majik.Core.Rules.Protection.HasProtectionFromColor) scans the
            // bearer's Abilities for ProtectionAbility so the grants feed
            // standard CR 702.16 gameplay. Combat-damage-to-a-player trigger
            // (CR 510 / CR 603.1) fires when the equipped creature deals
            // combat damage to a player: damaged player discards a card (v1
            // deterministic first-card pick) and controller's lands untap.
            // Equip {2} activated ability wired. The single-arg dispatcher
            // path produces the correct card shape; use the (owner,
            // continuousEffects, eventBus, triggers) overload for fully-wired
            // boost / protection lifecycle / bus-driven combat-damage firing.
            // Sorcery-speed gate + attach-target prompt + discard-prompt
            // deferred (same queue as Colossus Hammer / Umezawa's Jitte /
            // Liliana of the Veil).
            "Sword of Feast and Famine" => SwordOfFeastAndFamineFactory.Create(owner),

            // Legendary Artifact — Equipment {2} (UmezawasJitteFactory).
            // Betrayers of Kamigawa. Combat-damage trigger places two charge
            // counters on Jitte. Three modal activated abilities, each
            // {Remove a charge counter}: (1) +2/+2 EOT to equipped creature;
            // (2) target creature gets -1/-1 EOT; (3) controller gains 2
            // life. Equip {2} activated ability wired. The single-arg
            // dispatcher path produces the correct card shape; use the
            // (owner, continuousEffects, triggers) overload for live
            // boost / bus-driven combat-damage trigger wiring.
            "Umezawa's Jitte" => UmezawasJitteFactory.Create(owner),

            // Artifact — Equipment {3} (SwordOfFireAndIceFactory). Darksteel.
            // Static "+2/+2 and has protection from red and from blue" — the
            // boost ships via AttachedBoostEffect (Layer 7c); protection-from-
            // colour rides as two ProtectionAbility markers on the equipment
            // card itself (full DEBT-A enforcement deferred — no attachment-
            // aware Layer 6 grant for protection yet). Combat-damage-to-a-
            // player trigger (CR 510 / CR 603.1) deals 2 damage to any target
            // + draws a card — TargetRequest("any target") attached for shape;
            // damage no-ops without a chosen target while the paired draw
            // still resolves (CR 608.2b "do as much as possible"). Equip {2}
            // activated ability wired (sorcery-speed restriction + attach-
            // target prompt deferred — same gaps as the Colossus Hammer /
            // Skullclamp equipment cycle). Single-arg dispatcher path
            // produces the correct card shape only; use the (owner,
            // continuousEffects, triggers) overload for live boost +
            // bus-driven trigger firing.
            "Sword of Fire and Ice" => SwordOfFireAndIceFactory.Create(owner),

            // Legendary Land — Phyrexian Tower (PhyrexianTowerFactory).
            // {T}: Add {C} and {T}, Sacrifice a creature: Add {B}{B} — wired.
            // The sacrifice cost uses SacrificeAnotherCreatureCost; callers can
            // pre-set the sacrifice target on the second ability's
            // SacrificeChoice for deterministic test/bot behavior.
            "Phyrexian Tower" => PhyrexianTowerFactory.Create(owner),

            // Creature — Dauthi Rogue {1}{B} 3/2 (DauthiVoidwalkerFactory).
            // Modern Horizons 2. Shadow keyword wired. Opponent-graveyard →
            // exile-with-void-counter replacement effect (CR 614) ships via
            // the bus-aware overload — the single-arg dispatcher path here
            // produces the correct card shape (Shadow + activated ability)
            // without ReplacementBus wiring. {2}, {T}, Remove a void
            // counter from a card exiled with Dauthi Voidwalker: "you may
            // play that card this turn without paying its mana cost" is
            // wired via CastFromExileAlternativeCost({0}) — see
            // DauthiVoidwalkerFactory.BuildAlternativeCost. EOT timing on
            // the cast permission deferred.
            "Dauthi Voidwalker" => DauthiVoidwalkerFactory.Create(owner),

            // Artifact Creature — Dragon {10} 4/4 (ScionOfDracoFactory).
            // Modern Horizons 2. Domain cost reduction (CR 702.16 / CR 117.7):
            // "This spell costs {2} less to cast for each basic land type
            //  among lands you control." Wired via the whole-reducer shape
            // on CostReductionAbility, delegating to
            // TribalFlamesFactory.CountDomain for the distinct-basic-type
            // count (printed-subtypes mode at cost-calc time; floor-at-zero
            // in CostReduction.GetEffectiveCost). Keyword-grant rider
            // ("creatures you control of each creature type have first
            // strike, vigilance, trample, lifelink, and hexproof") deferred
            // — needs per-permanent shared-creature-type Layer 6 grants.
            "Scion of Draco" => ScionOfDracoFactory.Create(owner),

            // Legendary Artifact — {0} (MoxOpalFactory). Scars of Mirrodin.
            // "Metalcraft — {T}: Add one mana of any color. Activate only if
            //  you control three or more artifacts." CR 702.95. Five
            // ManaAbility instances (one per WUBRG), each gated on
            // !IsTapped AND controller's artifact count >= 3 (Mox Opal
            // itself counts when on the battlefield). Opponent artifacts
            // do not contribute. Single modal-colour ability shape is not
            // in the engine yet — same five-ability fan-out used by
            // DelightedHalflingFactory.
            "Mox Opal" => MoxOpalFactory.Create(owner),

            // Sorcery — {R} (RecklessChargeFactory). Odyssey / Modern Horizons.
            // "Target creature gets +3/+0 and gains haste until end of turn.
            //  Flashback {2}{R}." Card shape only here; the resolve-time
            // SpellDefinition (target creature → +3/+0 + Haste EOT, both
            // expiring at cleanup per CR 514.2) is built on demand via
            // RecklessChargeFactory.BuildSpellDefinition. Flashback alt-cost
            // ({2}{R}) is exposed via RecklessChargeFactory.BuildFlashbackCost
            // (parsed by FlashbackOracleParser — same pattern as Faithless
            // Looting). Illegal-target / no-ActiveEffects fallbacks no-op
            // cleanly.
            "Reckless Charge" => RecklessChargeFactory.Create(owner),

            // Legendary Creature — Monkey Pirate {R} 2/1 (RagavanNimblePilfererFactory).
            // Modern Horizons 2 staple. Combat-damage-to-a-player trigger
            // wired: creates a Treasure token under the Ragavan controller,
            // exiles the damaged player's library top, and stamps a
            // runtime exile-cast grant via Card.GrantRuntimeExileCast so
            // the Ragavan controller may cast that exiled card via
            // ExileCastAlternativeCost until end of turn (CR 118.9).
            // Single-arg dispatcher path produces the correct card shape
            // without bus-driven trigger registration / EOT-clear hook.
            // Use the (owner, zoneService, eventBus, triggers) overload
            // for fully-wired behaviour. Dash {1}{R} (CR 702.108) is
            // DEFERRED — no DashAlternativeCost / DashReturnRegistry yet.
            "Ragavan, Nimble Pilferer" => RagavanNimblePilfererFactory.Create(owner),

            // Sorcery — {1}{G} (EldritchEvolutionFactory). Eldritch Moon.
            // "As an additional cost to cast this spell, sacrifice a
            //  creature. Search your library for a creature card with mana
            //  value less than or equal to the sacrificed creature's mana
            //  value plus 2, put it onto the battlefield, then shuffle.
            //  Exile Eldritch Evolution." CR 601.2f additional-cost +
            // CR 701.19a creature tutor → battlefield + CR 608.2 self-exile
            // override. Card shape only here; the resolve-time spell
            // definition is built on demand via
            // EldritchEvolutionFactory.BuildSpellDefinition(caster, card,
            // zoneService) so ETB triggers fire on the tutored permanent
            // when a ZoneService is wired. Sacrifice target picks the
            // first eligible creature on the controller's battlefield
            // deterministically (same v1 behaviour as Fling / Thud /
            // Life's Legacy). Library shuffle (CR 701.19c) deferred —
            // same rationale as SearchSpellFactory.
            "Eldritch Evolution" => EldritchEvolutionFactory.Create(owner),

            // Land — Cavern of Souls (Avacyn Restored, CavernOfSoulsFactory).
            // ETB: choose a creature type (resolved eagerly via a Func<Player,
            // CardSubtype> typeChooser on the 2-arg overload; the single-arg
            // dispatcher path leaves the chosen-type slot empty).
            // {T}: Add {C} — wired.
            // {T}: Add one mana of any color — five ManaAbility instances
            // (one per WUBRG) wired (mirrors Delighted Halfling).
            // Spend-restriction ("creature spell of the chosen type") +
            // uncounterable rider deferred — engine has no per-mana provenance
            // ledger yet (see CavernOfSoulsFactory xmldoc).
            "Cavern of Souls" => CavernOfSoulsFactory.Create(owner),

            // Artifact — {X}{X} (ChaliceOfTheVoidFactory). Mirrodin.
            // ETB trigger: enters with X charge counters (X read from
            // Card.PendingCastX, stamped by SpellCastFlow after the
            // caster's ChooseXAsync). Triggered ability: whenever any
            // player casts a spell with mana value equal to the number
            // of charge counters on Chalice, counter that spell.
            // Symmetric — counters both players' spells. The
            // single-arg dispatcher path produces the correct card
            // shape (both triggered abilities attached) without
            // TriggerManager registration or stack-coupled counter
            // wiring; use the (owner, stack, eventBus, triggers)
            // overload for bus-driven trigger firing + RemoveFromStack
            // routing.
            "Chalice of the Void" => ChaliceOfTheVoidFactory.Create(owner),

            // Instant — {X}{G}{G}{G} (ChordOfCallingFactory). Ravnica.
            // Flash + Convoke keyword markers wired inline. Convoke
            // alt-cost surfaced via ChordOfCallingFactory.BuildAlternativeCost
            // (ConvokeAlternativeCost) — v1 returns printed cost unchanged
            // until SpellCastFlow grows a Convoke-aware reduction hook
            // (see ConvokeAlternativeCost xmldoc). Resolve-time
            // SpellDefinition (HasVariableX = true, tutor creature with
            // mv ≤ X → battlefield) is built on demand via
            // ChordOfCallingFactory.BuildSpellDefinition(caster, zones?).
            // ETB triggers on the tutored creature fire when a live
            // ZoneService is threaded in (mirrors LivingEnd /
            // PrimevalTitan PR #165 / #174 wiring). Library shuffle (CR
            // 701.19c) deferred — same rationale as SearchSpellFactory.
            "Chord of Calling" => ChordOfCallingFactory.Create(owner),

            // Artifact — {X} (EngineeredExplosivesFactory). Fifth Dawn / Modern
            // Horizons. Sunburst ETB → enters with X charge counters
            // (CR 702.43a — non-creature variant). v1 approximation: the
            // engine has no per-cast mana-provenance ledger, so the
            // (owner, xValueProvider, allPlayersResolver) overload accepts a
            // Func<int> X provider that callers wire to the cast-time printed
            // X (upper bound on colours spent for {X} artifacts). Activated
            // {2}, Sacrifice this: destroy each nonland permanent with mv =
            // charge counters — wired. The single-arg dispatcher path uses
            // no X provider (ETB applies 0 counters, matching X=0) and
            // scans only the controller's battlefield. Sacrifice payment is
            // still a no-op stub at AdditionalCost.Pay; the effect closure
            // moves Engineered Explosives to the graveyard so visible state
            // matches CR 701.16 (same trick as Mishra's Bauble).
            "Engineered Explosives" => EngineeredExplosivesFactory.Create(owner),

            // Creature — Human Scout {2}{G} 3/2 (TirelessTrackerFactory).
            // Shadows over Innistrad. Landfall-style triggered ability:
            // "Whenever a land enters under your control, create a Clue
            // token." Wired over CardMovedEvent → battlefield with a Land +
            // controller-match predicate; Clue creation routes through
            // TokenFactory.CreateClue. Activated ability: "{2}, Sacrifice
            // a Clue: Put a +1/+1 counter on Tireless Tracker." The
            // sacrifice cost is a SacrificeAClueCost surfaced on the
            // returned card via TirelessTrackerActivatedAbility.SacrificeChoice
            // so a caller can pre-set the Clue to sac (mirrors Phyrexian
            // Tower's SacrificeChoice pattern). The single-arg dispatcher
            // path here attaches the trigger to the card shape without
            // TriggerManager / ZoneService wiring; use the (owner,
            // zoneService, triggers) overload for fully-wired bus firing.
            "Tireless Tracker" => TirelessTrackerFactory.Create(owner),

            // Creature — Giant {2}{R} 4/3 (BonecrusherGiantFactory). Throne
            // of Eldraine Adventure card. v1 ships the creature side +
            // targeted-by-spell trigger (deal 2 to spell's controller).
            // The Stomp Adventure half + cast-from-exile pipeline (CR 715)
            // is deferred — no Adventure cast surface in the engine yet.
            "Bonecrusher Giant" => BonecrusherGiantFactory.Create(owner),

            // Creature — Human Knight {1}{R} 2/1 (EmberethShieldbreakerFactory).
            // Throne of Eldraine Adventure card. v1 ships the vanilla
            // creature side (no printed keywords / triggers) + a Battle
            // Display helper exposed via
            // EmberethShieldbreakerFactory.BuildAdventureSpell that returns
            // a destroy-target-artifact SpellDefinition (CR 701.7). The
            // Adventure cast-from-hand-to-exile pipeline (CR 715) is
            // deferred — same gap as BonecrusherGiantFactory /
            // MurderousRiderFactory.
            "Embereth Shieldbreaker" => EmberethShieldbreakerFactory.Create(owner),

            // Instant — {U} (SpellSnareFactory). Coldsnap.
            // "Counter target spell with mana value 2." Card shape only
            // here; the resolve-time SpellDefinition is built on demand via
            // SpellSnareFactory.BuildDefinition(targetResolver, stack). MV
            // is sampled at resolution time (CR 202.3) — printed + PendingCastX,
            // mirroring Chalice of the Void. Illegal-mv target → no-op (CR 608.2b).
            "Spell Snare" => SpellSnareFactory.Create(owner),

            // Instant — {1}{R/G} (ManamorphoseFactory). Shadowmoor.
            // "Add two mana in any combination of colors. Draw a card."
            // Hybrid pip parses to 1 generic + HybridPip(Red, Green) via
            // ManaCost.Parse (CR 107.4e). Card shape only here; the resolve
            // effect (deposit 2 picked mana + draw 1) is built on demand via
            // ManamorphoseFactory.BuildResolveEffect with a Func<Player,
            // ManaColor[]> picker — v1 default returns {R}{G}. No agent
            // prompt for the colour pair yet (IPlayerAgent has no
            // ChooseManaColorsAsync); callers can pre-pick. Net-zero
            // mana-effect bookkeeping for cost-reduction restrictions
            // (CR 106.11b) deferred — no mana-provenance ledger.
            "Manamorphose" => ManamorphoseFactory.Create(owner),

            // Land — Mutavault (Morningtide / reprints, MutavaultFactory).
            // {T}: Add {C} — vanilla ManaAbility wired.
            // {1}: Until end of turn, Mutavault becomes a 2/2 creature that's
            // every creature type. It's still a land. ActivatedAbility with a
            // ManaCostCost("{1}") that, on resolution, registers Layer 4
            // (MutavaultAnimateEffect — add Creature + every modelled creature
            // subtype) and Layer 7b (MutavaultBecomesPTEffect — base P/T 2/2).
            // Both effects flagged ExpiresAtEndOfTurn (CR 514.2). Single-arg
            // dispatch wires no ContinuousEffectsService — the activated
            // ability resolves but no animate effect is recorded; use
            // MutavaultFactory.Create(owner, effects) for fully-wired animate.
            // "Every creature type" is approximated as every creature subtype
            // currently enumerated in CardSubtype — see MutavaultAnimateEffect
            // .EveryCreatureType + class xmldoc for the v1 simplification.
            "Mutavault" => MutavaultFactory.Create(owner),

            // Legendary Land — Karakas (Legends / reprints, KarakasFactory).
            // {T}: Add {W} — vanilla ManaAbility wired.
            // {T}: Return target legendary creature to its owner's hand —
            // ActivatedAbility with AdditionalCost.Tap + TargetRequest for
            // "target legendary creature". Resolution-time gate checks the
            // chosen target is a legendary creature (CR 608.2b illegal-
            // target → effect does nothing). v1 uses raw zone moves (no
            // ZoneService routing — mirrors Teferi -3 bounce). ActionValidator
            // doesn't yet filter the agent's target list by "legendary"
            // (resolution-time guard catches illegal picks).
            "Karakas" => KarakasFactory.Create(owner),

            // Land — Wasteland (Tempest / reprints, WastelandFactory).
            // {T}: Add {C} — vanilla ManaAbility wired.
            // {T}, Sacrifice Wasteland: Destroy target nonbasic land —
            // ActivatedAbility with AdditionalCost.Tap + TargetRequest for
            // "target nonbasic land". The self-sacrifice is performed inline
            // by the effect closure (AdditionalCost.Sacrifice's Pay is a
            // stub — same trick Engineered Explosives + Mishra's Bauble use).
            // Resolution-time gate checks the chosen target is a non-basic
            // Land on the battlefield (CR 608.2b illegal-target → effect
            // does nothing). v1 uses raw zone moves (no ZoneService routing —
            // mirrors Karakas's bounce). ActionValidator doesn't yet filter
            // the agent's target list by "nonbasic land" (resolution-time
            // guard catches illegal picks). Instant-speed per the printed
            // oracle (CR 602.5b default activation timing).
            "Wasteland" => WastelandFactory.Create(owner),

            // Creature — Illusion {1}{U} 0/0 (PhantasmalImageFactory).
            // CR 706.10 — "You may have this enter as a copy of any creature
            // on the battlefield, except it's an Illusion in addition to its
            // other types and has 'When this creature becomes the target of
            // a spell or ability, sacrifice it.'" The single-arg dispatcher
            // path here produces the correct card shape (printed 0/0 Illusion
            // + the targeted-by-spell-or-ability sacrifice trigger attached)
            // without ReplacementBus / ContinuousEffectsService / EventBus /
            // TriggerManager wiring. Use the (owner, eventBus, triggers,
            // replacements, effects) overload for fully-wired enters-as-copy
            // + Layer 4 Illusion rider + bus-driven trigger firing.
            "Phantasmal Image" => PhantasmalImageFactory.Create(owner),

            // Enchantment — {B}{B}{B} (NecropotenceFactory). Ice Age.
            // "Skip your draw step. Whenever you discard a card, exile that
            //  card. Pay 1 life: Exile the top card of your library face
            //  down. Put that card into your hand at the beginning of your
            //  next end step." Skip-draw wired via SkipDrawRegistry
            // (consulted by TurnDriver); discard→exile wired via
            // ReplacementBus on the hand→graveyard ZoneMoveIntent funnel
            // (engine has no DiscardEvent in v1); activated ability wired
            // with Pay 1 life + delayed end-step return-to-hand. The
            // single-arg dispatcher path produces the correct card shape
            // (Enchantment + Static + ReplacementEffect + ActivatedAbility)
            // but the skip-draw / discard-exile / delayed draw side
            // effects do not fire because no registry/bus/triggers are
            // wired. Use the (owner, replacements, triggerManager)
            // overload for fully-wired behaviour. Face-down exile is
            // deferred — engine has no face-down flag.
            "Necropotence" => NecropotenceFactory.Create(owner),

            // Enchantment — {B}{B}{B} (NecrodominanceFactory). Modern Horizons 3.
            // "If you would draw a card except for the first card you draw in
            //  each of your draw steps, skip that draw. Skip your draw step.
            //  Pay 1 life: Exile the top card of your library face down. Look
            //  at it any time. You may cast that card from exile until end
            //  of turn." Necropotence variant — same skip-your-draw-step
            //  hook (SkipDrawRegistry), plus an additional-draw-skip clause
            //  surfaced as a static marker (engine has no CardDrawIntent on
            //  the ReplacementBus in v1), and an activated ability that
            //  swaps Necropotence's delayed end-step return-to-hand for a
            //  cast-from-exile alternative cost (CR 118.9) revoked at the
            //  next Cleanup step (CR 514.2). The single-arg dispatcher
            //  path produces the correct card shape (Enchantment + two
            //  Static markers + ActivatedAbility) without bus-driven EOT
            //  revocation; use the (owner, eventBus) overload for fully-
            //  wired behaviour. Face-down exile + live additional-draw
            //  skip + sorcery-speed cast restrictions deferred — same
            //  v1 gaps as Necropotence / Dauthi Voidwalker.
            "Necrodominance" => NecrodominanceFactory.Create(owner),

            // Enchantment — {1}{W} (StonySilenceFactory). Return to Ravnica.
            // "Activated abilities of artifacts can't be activated unless
            //  they're mana abilities." (CR 602.5c / 605.) Symmetric global
            // variant of Karn the Great Creator's opponent-only static.
            // Wired via StonySilenceStaticEffect when the (owner, eventBus)
            // overload is used — a predicate restriction is registered into
            // ActivatedAbilityRestrictions matching any non-mana activated
            // ability whose source is an on-battlefield artifact. The
            // single-arg dispatcher path here produces the correct card
            // shape only (no live suppression).
            "Stony Silence" => StonySilenceFactory.Create(owner),

            // Artifact — {2} (CursedTotemFactory). Mirage.
            // "Activated abilities of creatures can't be activated unless
            //  they're mana abilities." (CR 602.5c / 605.) Creature-side
            // analogue of Stony Silence's global artifact suppression.
            // Wired via CursedTotemStaticEffect when the (owner, eventBus)
            // overload is used — a predicate restriction is registered into
            // ActivatedAbilityRestrictions matching any non-mana activated
            // ability whose source is an on-battlefield creature. The
            // single-arg dispatcher path here produces the correct card
            // shape only (no live suppression).
            "Cursed Totem" => CursedTotemFactory.Create(owner),

            // Instant — {G} (VeilOfSummerFactory). Core Set 2020.
            // "Draw a card if an opponent has cast a blue or black spell this
            // turn. Spells you control can't be countered this turn, and you
            // and permanents you control gain hexproof from blue and from
            // black until end of turn."
            // Card shape only here; the resolve-time SpellDefinition is built
            // on demand via VeilOfSummerFactory.BuildDefinition(caster,
            // turnState, continuousEffects). Conditional draw consults
            // TurnState.OpponentCastSpellOfColor; uncounterable + hexproof
            // riders are structural in v1 (see factory doc).
            "Veil of Summer" => VeilOfSummerFactory.Create(owner),

            // Instant — {1}{U} (AetherGustFactory). Core Set 2020.
            // "Choose target spell or permanent that's red or green. Its owner
            //  puts it on the top or bottom of their library." (CR 115 +
            //  CR 109.4 / 701.20a.) Card shape only here; the bounce
            // SpellDefinition is built on demand via
            // AetherGustFactory.BuildDefinition(targetResolver, stack,
            // topChooser?). Colour-of-target gate runs at resolution time
            // via CardColors.GetColors (CR 105). Top-vs-bottom decision is
            // sourced from the optional topChooser callback; null defaults
            // to bottom (mirrors Manamorphose's deterministic v1 fallback).
            "Aether Gust" => AetherGustFactory.Create(owner),

            // Creature — Goblin {R} 1/1 (GoblinLackeyFactory). Urza's Destiny.
            // "Whenever Goblin Lackey deals combat damage to a player, you may
            //  put a Goblin creature card from your hand onto the battlefield."
            // Combat-damage-to-a-player trigger mirrors the Ragavan, Nimble
            // Pilferer shape (CombatDamageDealtEvent filtered to source + non-
            // null TargetPlayer). v1 deterministically picks the first Goblin
            // creature card in hand and routes the hand → battlefield move
            // through ZoneService when supplied so ETB triggers fire on the
            // cheated-in Goblin (CR 603.6a). The single-arg dispatcher path
            // here produces the correct card shape without TriggerManager
            // registration / ZoneService routing. Use the (owner, zoneService,
            // eventBus, triggers) overload for fully-wired behaviour.
            // Agent-driven "you may" decline + multi-candidate selection
            // deferred (mirrors Aether Vial + Stoneforge Mystic).
            "Goblin Lackey" => GoblinLackeyFactory.Create(owner),

            // Creature — Goblin Wizard {U}{R} 2/2 (GoblinElectromancerFactory).
            // Return to Ravnica. "Instant and sorcery spells you cast cost
            // {1} less to cast." Wired via SpellCostReductionAbility — a
            // sibling to SpellCostIncreaseAbility (Damping Sphere family).
            // Predicate matches instant/sorcery spells; reduction is a flat
            // {1} generic per cast. CostReduction.GetEffectiveCost scans
            // only the caster's battlefield for this ability shape, so the
            // "you cast" scope is enforced inside the cost-calc helper.
            // Floor-at-zero is layered in alongside other reducers; coloured
            // pips are untouched (CR 117.7c).
            "Goblin Electromancer" => GoblinElectromancerFactory.Create(owner),

            // Creature — Goblin Warrior {1}{R}{R} 2/2 (GoblinChieftainFactory).
            // Magic 2010 / many reprints. "Haste. Other Goblin creatures you
            // control have haste and get +1/+1." Printed Haste keyword on
            // Chieftain itself wired via KeywordAbility. The lord-style static
            // ("Other Goblins +1/+1 + Haste") is wired via LordStaticEffect
            // (Plague Engineer shape, sign-flipped to +1/+1, includeSelf:
            // false, scoped to controller's creatures via the default filter)
            // when the (owner, ContinuousEffectsService) overload is used.
            // The single-arg dispatcher path here produces the correct card
            // shape with the Haste keyword only — no lord static is
            // registered (no layers service available). Modern Goblins / 8-
            // Whack pillar.
            "Goblin Chieftain" => GoblinChieftainFactory.Create(owner),

            // Creature — Goblin Warrior {1}{R}{R} 2/2 (GoblinWarchiefFactory).
            // Scourge / many reprints. "Goblin spells you cast cost {1} less
            // to cast. Goblins you control have haste." Cost-reduction rider
            // wired via SpellCostReductionAbility (Goblin Electromancer
            // shape, predicate filtered to spells carrying CardSubtype.Goblin
            // — "Goblin spells" covers Goblin creature spells AND any non-
            // creature spells with Goblin in the subtype line). Haste-grant
            // static wired via LordStaticEffect (Goblin Chieftain shape with
            // power/toughness = 0, grantedKeywords = ["Haste"], includeSelf:
            // true — the oracle text says "Goblins you control" with no
            // "other" rider, so Warchief grants Haste to itself too) when
            // the (owner, ContinuousEffectsService) overload is used. The
            // single-arg dispatcher path produces the card shape with the
            // cost-reduction rider only — no live haste grant. Modern
            // Goblins / 8-Whack pillar.
            "Goblin Warchief" => GoblinWarchiefFactory.Create(owner),

            // Creature — Goblin Warrior {1}{R} 1/2 (GoblinPiledriverFactory).
            // Onslaught / many reprints. "Protection from blue. Whenever
            // Goblin Piledriver attacks, it gets +2/+0 until end of turn for
            // each other attacking Goblin." Protection from blue wired via
            // ProtectionAbility (same shape as Sword of Fire and Ice's two
            // protection riders). Attack trigger wired via Triggers.
            // OnAttackSelf against CreatureAttacksEvent; the pump-per-other-
            // attacking-Goblin body reads the attackers list from an injected
            // closure (attackingCreaturesSource) and registers a
            // PumpUntilEndOfTurnEffect for +2X/+0 EOT against
            // Creature.ActiveEffects when the (owner, triggers,
            // attackingCreaturesSource) overload is used. The single-arg
            // dispatcher path produces the card shape with protection from
            // blue + the attack trigger attached, but no live pump body
            // (no attackers source means zero pump). Modern Goblins / 8-
            // Whack pillar.
            "Goblin Piledriver" => GoblinPiledriverFactory.Create(owner),

            // Creature — Human Druid {G} 0/1 (NobleHierarchFactory).
            // Conflux / Modern Horizons 2. Exalted (CR 702.90) — whenever a
            // creature you control attacks alone, that creature gets +1/+1
            // until end of turn. {T}: Add {G}, {W}, or {U} — three
            // ManaAbility instances wired. The single-arg dispatcher path
            // attaches the exalted trigger to the card shape without
            // TriggerManager wiring; attackingCreaturesSource is null so
            // the pump body is a no-op. Use the
            // (owner, triggers, attackingCreaturesSource) overload for
            // fully-wired behavior.
            "Noble Hierarch" => NobleHierarchFactory.Create(owner),

            // Creature — Goblin Shaman {G} 0/1 (IgnobleHierarchFactory).
            // Modern Horizons 3. Mono-G black/red/green sibling of Noble
            // Hierarch — same shape (Exalted CR 702.90 + three tap
            // ManaAbility instances) with mana colours swapped to {B}, {R},
            // {G} and subtypes swapped to Goblin Shaman. Single-arg
            // dispatcher path attaches the exalted trigger without
            // TriggerManager wiring; attackingCreaturesSource is null so the
            // pump body is a no-op. Use the (owner, triggers,
            // attackingCreaturesSource) overload for fully-wired behavior.
            "Ignoble Hierarch" => IgnobleHierarchFactory.Create(owner),

            // Creature — Goblin Warrior {2}{R} 2/2 (GoblinRabblemasterFactory).
            // Magic 2015 / many reprints. "Other Goblin creatures you control
            // have haste. Whenever Goblin Rabblemaster attacks, create a 1/1
            // red Goblin creature token, then it gets +1/+0 until end of turn
            // for each attacking Goblin you control." Lord-style Haste grant
            // wired via LordStaticEffect (Goblin Chieftain shape with
            // power/toughness 0 — keyword-only). Attack trigger wired via
            // Triggers.OnAttackSelf against CreatureAttacksEvent; the body
            // creates a 1/1 Goblin token via TokenFactory.CreateOnBattlefield
            // and then registers a PumpUntilEndOfTurnEffect on Rabblemaster
            // for +N/+0 EOT, where N = count of attacking Goblins the
            // controller controls (no "other" qualifier — Rabblemaster itself
            // counts). The attackers list is read from an injected closure
            // (attackingCreaturesSource); the single-arg dispatcher path
            // produces the card shape with the attack trigger attached but
            // no live pump body and no lord-static registration (no layers
            // service / no attackers source). Modern Goblins / 8-Whack
            // pillar.
            "Goblin Rabblemaster" => GoblinRabblemasterFactory.Create(owner),

            // Creature — Goblin {2}{R} 1/1 (GoblinMatronFactory). Urza's Legacy.
            // "When Goblin Matron enters, you may search your library for a
            //  Goblin card, reveal that card, and put it into your hand.
            //  Then shuffle."
            // ETB tutor (CR 603.1, CR 701.19a) — predicate filters library
            // by CardSubtype.Goblin; agent-driven pick with deterministic
            // first-match fallback (same pattern as MysticalTutorFactory /
            // SearchSpellFactory). The single-arg dispatcher path here
            // produces the correct card shape; use the
            // (owner, zoneService, eventBus, triggers) overload for fully-
            // wired behaviour. Shuffle (CR 701.19c) + reveal event deferred
            // — same gaps as the rest of the tutor surface.
            "Goblin Matron" => GoblinMatronFactory.Create(owner),

            // Artifact — {2} (DampingSphereFactory). Dominaria.
            // Two static riders:
            //   1. "If a land is tapped for two or more mana, it produces
            //      {C} instead of any other type and amount." Wired via
            //      DampingSphereCappedManaAbility applied by
            //      EffectiveManaAbilities.For when the all-players list is
            //      supplied.
            //   2. "Each spell a player casts costs {1} more to cast for
            //      each other spell that player has cast this turn." Wired
            //      via a SpellCostIncreaseAbility scanned by
            //      CostReduction.GetEffectiveCost when the all-players list
            //      is supplied.
            // The single-arg dispatcher path here produces the correct card
            // shape (cost rider reads a null TurnState → zero); production
            // wiring should use DampingSphereFactory.Create(owner,
            // turnState) so the rider reads the live per-turn tally. See
            // factory xmldoc for deferred ManaPaymentResolver /
            // CostReduction call-site plumbing.
            "Damping Sphere" => DampingSphereFactory.Create(owner),

            // Artifact — {1}{B} (WishclawTalismanFactory). Throne of Eldraine.
            // "Wishclaw Talisman enters tapped. {T}, Pay 3 life: Search your
            //  library for a card, put that card into your hand, then shuffle.
            //  An opponent gains control of Wishclaw Talisman. Activate only
            //  as a sorcery."
            // ETB tapped wired via EntersTappedReplacement (surfaced through
            // WishclawTalismanWiring on the runtime overload). Activated
            // ability has {T} + Pay 3 life costs; effect tutors any card via
            // the SearchSpellFactory agent-driven primitive then registers
            // a ControlChangeEffect (CR 613.2) swapping control of the
            // Talisman to a caller-chosen opponent. Sorcery-speed gate +
            // shuffle (CR 701.19c) + opponent-prompt deferred — same
            // deferral patterns as Priest of Fell Rites / SearchSpellFactory.
            // The single-arg dispatcher path here produces the correct card
            // shape; control-swap is a no-op without a live
            // ContinuousEffectsService (use the (owner, effects,
            // opponentChooser) overload for the full effect).
            "Wishclaw Talisman" => WishclawTalismanFactory.Create(owner),

            // Sorcery — {2}{B} (YawgmothsWillFactory). Urza's Saga.
            // "Until end of turn, you may play cards from your graveyard.
            //  If a card would be put into your graveyard from anywhere
            //  this turn, exile it instead." Card shape only here; the
            // resolve effect (stamp Card.GrantRuntimeGraveyardCast on every
            // card in the controller's graveyard + register an
            // EOT-expirable YawgmothsWillGraveToExileReplacement on the
            // supplied ReplacementBus) is built on demand via
            // YawgmothsWillFactory.BuildResolveEffect(caster, replacements?).
            // Mirrors LurrusOfTheDreamDenFactory's grave-cast plumbing
            // (per-card alt cost via Costs/GraveyardCastAlternativeCost)
            // and DauthiVoidwalkerFactory's grave→exile replacement
            // pattern. The single-arg dispatcher path produces a bare
            // Sorcery shell — the resolve effect is wired by callers.
            "Yawgmoth's Will" => YawgmothsWillFactory.Create(owner),

            // Enchantment — {2}{R}{R} (ManabarbsFactory). Sixth Edition.
            // "Whenever a player taps a land for mana, Manabarbs deals 1
            //  damage to that player." A symmetric triggered ability
            // subscribed to ManaAbilityActivatedEvent (CR 605 — published
            // by ManaAbilityActivator after the activator's mana pool is
            // topped up). Source gate matches Land via printed type only;
            // non-land mana abilities (Mox Opal, etc.) emit the same
            // event but the source predicate rejects them. Damage is
            // surfaced as Player.LoseLife(1) — same v1 non-combat-damage
            // shape as Dark Confidant. The single-arg dispatcher path
            // here produces the correct card shape without
            // TriggerManager wiring; use the (owner, triggers) overload
            // for bus-driven trigger firing.
            "Manabarbs" => ManabarbsFactory.Create(owner),

            // Creature — Human Rogue {2}{B} 2/2 (PlagueEngineerFactory).
            // Core Set 2020 staple. Deathtouch keyword wired. ETB choose-a-
            // creature-type: chosen subtype is resolved eagerly via a
            // Func<Player, CardSubtype> typeChooser on the 3-arg overload
            // (same pattern as Cavern of Souls). Static "Creatures of the
            // chosen type your opponents control get -1/-1" wired via
            // LordStaticEffect with opponentsOnly: true at Layer 7c — the
            // effect's IsActive() gates on Plague Engineer being on the
            // battlefield, so LTB/flicker naturally lifts the debuff
            // (mirrors Colossus Hammer's no-LTB-cleanup pattern). The
            // single-arg dispatcher path here produces the correct card
            // shape (Deathtouch + 2/2 Human Rogue) without a live debuff;
            // use PlagueEngineerFactory.Create(owner, continuousEffects,
            // typeChooser) for fully-wired behaviour. Agent-prompt
            // integration (ChooseSubtype) deferred — same queue as
            // Pithing Needle / Cavern of Souls.
            "Plague Engineer" => PlagueEngineerFactory.Create(owner),

            // Enchantment — {2}{B} (EngineeredPlagueFactory). Urza's Legacy.
            // "As Engineered Plague enters the battlefield, choose a creature
            //  type. All creatures of the chosen type get -1/-1."
            // Debuffs ALL players' creatures of the chosen type (unlike Plague
            // Engineer's opponents-only restriction). Wired via
            // LordStaticEffect with opponentsOnly: false when the runtime
            // (owner, continuousEffects, typeChooser) overload is used. The
            // single-arg dispatcher path here produces the correct card shape
            // only (no live debuff). Agent-prompt integration (ChooseSubtype)
            // deferred — same queue as Pithing Needle / Cavern of Souls.
            "Engineered Plague" => EngineeredPlagueFactory.Create(owner),

            // Legendary Artifact — {5} (PyromancersGogglesFactory). Magic Origins.
            // "{T}: Add {R}. When you spend this mana to cast an instant or
            //  sorcery spell, copy that spell. You may choose new targets for
            //  the copy." v1 ships {T}: Add {R} as a single ManaAbility plus a
            //  structural copy-rider TriggeredAbility (SpellCastEvent, gated on
            //  controller + Instant|Sorcery) whose effect is a no-op. Mana-
            //  provenance ledger ("when you spend this mana") + stack-copy
            //  primitive ("copy that spell") + new-targets prompt all deferred.
            "Pyromancer's Goggles" => PyromancersGogglesFactory.Create(owner),

            // Sorcery — {1}{R} (PyroclasmFactory). Portal Second Age and
            // many reprints. "Pyroclasm deals 2 damage to each creature."
            // Card shape only here; the resolve effect (2 damage to every
            // creature on every supplied player's battlefield via
            // Creature.TakeDamage — CR 109.5) is built on demand via
            // PyroclasmFactory.BuildResolveEffect(allPlayers). Distinct
            // from the shared DealsDamageEachCreatureTemplate stub, which
            // scans only the caster's battlefield.
            "Pyroclasm" => PyroclasmFactory.Create(owner),

            // Sorcery — {2}{R} (AngerOfTheGodsFactory). Theros.
            // "Anger of the Gods deals 3 damage to each creature. If a
            //  creature dealt damage this way would die this turn, exile
            //  it instead." Card shape only here; the resolve effect
            // (sweep 3 dmg + EOT-expirable ZoneMoveIntent replacement
            // rewriting tagged creature graveyard moves to exile via
            // AngerOfTheGodsExileInsteadReplacement on the supplied
            // ReplacementBus) is built on demand via
            // AngerOfTheGodsFactory.BuildResolveEffect(allPlayers,
            // replacements?). The rider tracks "damaged this way" by
            // reference identity on the sweep's hit set (CR 700.3) and
            // expires on the cleanup step (CR 514.2) via the bus's
            // ExpireEndOfTurn sweep. The single-arg dispatcher path
            // produces the correct card shape only.
            "Anger of the Gods" => AngerOfTheGodsFactory.Create(owner),

            // Instant — {1}{U} (DazeFactory). Nemesis.
            // "You may return an Island you control to its owner's hand
            //  rather than pay this spell's mana cost. Counter target spell
            //  unless its controller pays {1}."
            // Card shape only here; the resolve-time SpellDefinition
            // (counter-target-spell-unless-pay-{1}) is built on demand via
            // DazeFactory.BuildDefinition. Bounce-land pitch alt-cost (CR
            // 118.9) is surfaced via BounceLandPitchAlternativeCost
            // (Island predicate) — payment moves the chosen Island from
            // battlefield to its owner's hand on resolution. No timing
            // gate (Daze prints none, unlike the Force-of-Will pitch
            // cycle's "if it's not your turn" rider).
            "Daze" => DazeFactory.Create(owner),

            // Legendary Creature — Nymph {G}{W} 1/2 (SythisHarvestsHandFactory).
            // Theros Beyond Death constellation cycle. "Constellation —
            // Whenever an enchantment enters under your control, you gain
            // 1 life and draw a card." (CR 702.144 / 603.1). Trigger over
            // CardMovedEvent → battlefield with controller-match + card-type
            // Enchantment predicate (covers plain enchantments AND Auras
            // per CR 303.1). The single-arg dispatcher path here produces
            // the correct card shape without TriggerManager registration;
            // use the (owner, triggers) overload for fully-wired behavior.
            "Sythis, Harvest's Hand" => SythisHarvestsHandFactory.Create(owner),

            // Enchantment — Aura {2}{R}{R} (SplinterTwinFactory). Rise of
            // the Eldrazi. "Enchant creature. Enchanted creature has '{T}:
            // Create a token that's a copy of this creature, except it has
            // haste. Exile the token at the beginning of the next end
            // step.'" CR 303.4 / 613.1f — grant-activated-ability-on-attach
            // wired via AttachedAuraAbilityGrantStaticEffect. The granted
            // ability lives on the bearer's Abilities collection only while
            // the aura is attached + on the battlefield; revoked on LTB or
            // detach. Token copies snapshot bearer name + printed P/T +
            // subtypes + keyword names at activation (v1 lossy — aligns
            // with CopyEffect's printed-values semantics). Delayed
            // end-step exile registers as a DelayedTriggeredAbility when a
            // TriggerManager is wired. Single-arg dispatcher path produces
            // the correct card shape without lifecycle / trigger wiring;
            // use the (owner, eventBus, zoneService, triggers) overload
            // for fully-wired behaviour.
            "Splinter Twin" => SplinterTwinFactory.Create(owner),

            // Artifact — {1} (SenseisDiviningTopFactory). Champions of Kamigawa.
            // "{T}: Look at the top three cards of your library, then put them
            //  back in any order."
            // "{1}, {T}: Draw a card, then put Sensei's Divining Top on top of
            //  its owner's library."
            // Two activated abilities wired: a tap-only peek-3-and-reorder
            // (mirrors Ponder's agent-driven reorder via ScryAction with
            // ToBottom=[], default-preserves order), and a {1}+{T} draw-then-
            // self-return that draws a card via raw zone moves (empty-library
            // flags MarkTriedToDrawFromEmptyLibrary per CR 704.5b) and
            // moves Top from the battlefield onto the top of its owner's
            // library via IZone.InsertCardAt(0). Agent-driven "you may"
            // prompts are not relevant — Top has no opt-out clauses.
            // Printed Legendary supertype omitted in v1 (task scope).
            "Sensei's Divining Top" => SenseisDiviningTopFactory.Create(owner),

            // Instant — {W} (SwordsToPlowsharesFactory). Alpha.
            // "Exile target creature. Its controller gains life equal to its
            //  power." Card shape only here; the resolve-time SpellDefinition
            // (exile + power-snapshot lifegain) is built on demand via
            // SwordsToPlowsharesFactory.BuildDefinition(targetResolver).
            // Power is sampled BEFORE the zone move via Creature.Power, which
            // routes through ContinuousEffectsService.Compute when the creature
            // has ActiveEffects attached (Tarmogoyf CDA / anthems / pump all
            // feed through). Negative power floors to zero per CR 119.3.
            "Swords to Plowshares" => SwordsToPlowsharesFactory.Create(owner),

            // Creature — Spirit {1}{W}{U} 2/3 (SpellQuellerFactory). Eldritch Moon.
            // Flash keyword wired. ETB triggered ability targets a spell with
            // mana value 4 or less on the stack and exiles it (target supplied
            // via TriggeredAbility.SetChosenTargets — same shape as Snapcaster
            // Mage). LTB triggered ability releases the exiled card so its
            // owner may cast it without paying its mana cost (CR 702.85a-style
            // free cast via CastFromExileAlternativeCost — same pattern as
            // Cascade). The single-arg dispatcher path produces the correct
            // card shape without Stack / TriggerManager / host-callback wiring;
            // use the (owner, stack, triggers, onExiledCardReleased) overload
            // for fully-wired behaviour.
            "Spell Queller" => SpellQuellerFactory.Create(owner),

            // Land — Mirrodin Besieged (InkmothNexusFactory). Manland.
            // {T}: Add {C} mana ability + {1} activated ability registering
            // an InkmothAnimateLandEffect (Layer 4 type/subtype/keyword grant
            // for 1/1 Phyrexian Insect artifact creature with Flying + Infect,
            // "still a land", EOT). Single-arg dispatcher path produces the
            // correct card shape without ContinuousEffectsService wiring (the
            // animate {1} cost still resolves, the effect is just not
            // registered); use the (owner, continuousEffects) overload for
            // the fully-wired animate-on-resolve behaviour. Infect mechanic
            // (poison counters + creature damage as -1/-1 counters) is a
            // keyword marker only in v1 — see InkmothNexusFactory xmldoc.
            "Inkmoth Nexus" => InkmothNexusFactory.Create(owner),

            // Creature — Human Wizard {2}{U} 2/2 (TrinketMageFactory).
            // ETB tutor: search library for an artifact card with mana
            // value 1 or less → hand (deterministic first-match; shuffle
            // deferred, mirrors Stoneforge Mystic). The single-arg
            // dispatcher path here produces the correct card shape; use
            // the (owner, eventBus, triggers) overload to register the
            // ETB trigger with a TriggerManager.
            "Trinket Mage" => TrinketMageFactory.Create(owner),

            // Creature — Goblin Artificer {R} 1/1 (GoblinWelderFactory).
            // Urza's Legacy. Activated ability {T}: target artifact a
            // player controls + target artifact card in that player's
            // graveyard; on resolve that player sacrifices the
            // battlefield artifact and returns the graveyard artifact
            // to the battlefield (CR 608). The single-arg dispatcher
            // path here produces the correct card shape — the
            // activated ability is attached with the {T} cost but its
            // effect body no-ops because the dispatcher path does not
            // supply a player iterator. Use the
            // (owner, zoneService, eventBus, playerProvider) overload
            // for full sac-then-reanimate resolution; tests can also
            // drive the resolution directly via
            // GoblinWelderFactory.WeldResolve(players).
            "Goblin Welder" => GoblinWelderFactory.Create(owner),

            // Creature — Goblin Artificer {1}{R} 1/2 (GoblinEngineerFactory).
            // Modern Horizons. "When Goblin Engineer enters, you may search
            // your library for an artifact card, then put that card into
            // your graveyard. If you do, shuffle." +
            // "{R}, {T}, Sacrifice an artifact: Return target artifact card
            // from your graveyard to the battlefield." (CR 603.1 / CR 608).
            // The ETB tutor sends the picked artifact to the graveyard
            // (NOT hand) — distinguishes from TrinketMage / GoblinMatron.
            // The activated ability declares structural {R} + {T} costs;
            // "Sacrifice an artifact" is performed by the effect body
            // (generic permanent-class cost, no engine primitive yet).
            // Both sacrifice and reanimate picks are deterministic v1
            // (first-match). The single-arg dispatcher path here produces
            // the correct card shape with raw zone moves; use the
            // (owner, zoneService, eventBus, triggers) overload for
            // bus-driven ETB-trigger registration and ZoneService-routed
            // moves so ETB triggers on the reanimated artifact fire.
            "Goblin Engineer" => GoblinEngineerFactory.Create(owner),

            // Creature — Human Shaman {R} 1/1 (DragonsRageChannelerFactory).
            // Modern Horizons 2. "Whenever you cast a noncreature spell,
            // surveil 1. Delirium — Dragon's Rage Channeler gets +2/+2 and
            // has flying as long as there are four or more card types
            // among cards in your graveyard." Surveil trigger over
            // SpellCastEvent gated on controller + non-creature card
            // (mirrors Ledger Shredder's surveil routing). Delirium static
            // wired via DeliriumPumpEffect — two registered ContinuousEffects
            // (+2/+2 in Layer 7c, Flying in Layer 6) whose IsActive() gates
            // on DRC being on the battlefield AND CR 702.105's distinct-
            // type count >= 4 (sampled live via TarmogoyfFactory
            // .CountDistinctCardTypes). The single-arg dispatcher path here
            // produces the correct card shape with the surveil trigger
            // attached for ability-shape observability; use the (owner,
            // eventBus, triggers, effects) overload for fully-wired
            // bus-driven trigger firing + ETB/LTB delirium lifecycle.
            "Dragon's Rage Channeler" => DragonsRageChannelerFactory.Create(owner),

            // Creature — Minotaur Wizard {R/W}{R/W}{R/W} 3/3 (BorosReckonerFactory).
            // Gatecrash. First strike + "Whenever Boros Reckoner is dealt damage,
            // it deals that much damage to any target." Printed text is a
            // *replacement* effect on the incoming damage (CR 614) but v1 ships
            // it as a triggered ability (CR 603.1) over DamageDealtEvent —
            // simpler than introducing a source-damage redirect primitive. The
            // damage still resolves on Boros Reckoner before the redirect fires,
            // and the redirect goes on the stack rather than replacing the
            // original damage. Hybrid mana cost {R/W} parses via
            // ManaCost.Parse's HybridPip path (CR 107.4e). The single-arg
            // dispatcher path here produces the correct card shape without
            // TriggerManager wiring; use the (owner, eventBus, triggers)
            // overload to register the damage-received trigger and republish
            // the redirect as a non-combat DamageDealtEvent (CR 119.2c).
            "Boros Reckoner" => BorosReckonerFactory.Create(owner),

            // Instant — {U}{U} (BrainFreezeFactory). Scourge.
            // "Target player mills three cards. Storm (When you cast this
            // spell, copy it for each spell cast before it this turn. You
            // may choose new targets for the copies.)" CR 702.40 + CR 701.13.
            // Card shape only here; the resolve-time mill SpellDefinition is
            // built on demand via BrainFreezeFactory.BuildDefinition(targetResolver).
            // The structural Storm trigger is attached for shape inspection;
            // use the (owner, triggers, stack, turnState) overload for fully-
            // wired Storm copy semantics — copies share the original chosen
            // target (CR 702.40a retargeting deferred — see StormHelper +
            // SpellCopier xmldocs).
            "Brain Freeze" => BrainFreezeFactory.Create(owner),

            // Enchantment — {1}{B}{G} (PerniciousDeedFactory). Apocalypse.
            // "{X}, Sacrifice Pernicious Deed: Destroy each artifact,
            //  creature, and enchantment with mana value X or less."
            // Mirrors Engineered Explosives' v1 shape: ManaCostCost("{X}")
            // + AdditionalCost.Sacrifice on the activation; the effect
            // closure samples X from a caller-supplied Func<int>
            // (single-arg path uses X = 0) and scans every resolver-
            // supplied battlefield (controller-only when no resolver) for
            // Artifact / Creature / Enchantment cards with mv ≤ X.
            // Sacrifice payment is a no-op stub at AdditionalCost.Pay; the
            // effect closure moves Pernicious Deed to its owner's
            // graveyard so visible state matches CR 701.16 (same trick as
            // Engineered Explosives + Mishra's Bauble).
            "Pernicious Deed" => PerniciousDeedFactory.Create(owner),

            // Sorcery — {B} (CabalTherapyFactory). Judgment / Modern Horizons 2.
            // "Name a nonland card. Target player reveals their hand and
            //  discards all cards with that name. Flashback—Sacrifice a
            //  creature." Card shape only here; the resolve-time
            // SpellDefinition (target-player request + RevealHelper publish +
            // discard-all-with-name) is built on demand via
            // CabalTherapyFactory.BuildSpellDefinition(caster, resolver,
            // nameSelector, eventBus). Flashback alt-cost is split between
            // BuildFlashbackCost (ManaCost.Zero — Cabal Therapy's printed
            // flashback cost is non-mana) and BuildFlashbackAdditionalCosts
            // (SacrificeACreatureAdditionalCost). The engine's
            // FlashbackAlternativeCost only carries the mana portion (CR
            // 118.9) so the sacrifice rider is threaded as a paired
            // additional cost — v1 simplification noted in the factory
            // xmldoc. Card-name picker prompt deferred (same queue as
            // Pithing Needle).
            "Cabal Therapy" => CabalTherapyFactory.Create(owner),

            // Sorcery — {2}{B}{B} (TendrilsOfAgonyFactory). Scourge.
            // "Target opponent loses 2 life and you gain 2 life. Storm
            // (When you cast this spell, copy it for each spell cast
            // before it this turn. You may choose new targets for the
            // copies.)" CR 702.40. Card shape only here; the resolve-time
            // life-swing SpellDefinition is built on demand via
            // TendrilsOfAgonyFactory.BuildDefinition(controller, targetResolver).
            // The structural Storm trigger is attached for shape inspection;
            // use the (owner, triggers, stack, turnState) overload for
            // fully-wired Storm copy semantics — copies share the original
            // chosen opponent (CR 702.40a retargeting deferred — see
            // StormHelper + SpellCopier xmldocs). Mirrors Brain Freeze.
            "Tendrils of Agony" => TendrilsOfAgonyFactory.Create(owner),

            // Sorcery — {2}{R} (HidetsugusSecondRiteFactory). Champions of
            // Kamigawa / Kamigawa: Neon Dynasty reprint. "If target
            // opponent's life total is exactly 10, Hidetsugu's Second Rite
            // deals 10 damage to them." Card shape only here; the
            // resolve-time SpellDefinition (target-opponent request +
            // life == 10 gate + 10 damage via OracleSpellBinder.DealDamage)
            // is built on demand via
            // HidetsugusSecondRiteFactory.BuildSpellDefinition. CR 608.2c
            // — printed "if ..." is a resolve-time condition; non-10 life
            // totals are a clean no-op rather than an illegal-target
            // failure.
            "Hidetsugu's Second Rite" => HidetsugusSecondRiteFactory.Create(owner),

            // Artifact — {1} (SolRingFactory). Limited Edition Alpha.
            // "{T}: Add {C}{C}." Single tap mana ability adding two
            // colourless (ManaCost.Parse("CC") routes {C} through the
            // generic bucket per CR 107.4c).
            "Sol Ring" => SolRingFactory.Create(owner),

            // Artifact — {1} (ManaVaultFactory). Limited Edition Alpha.
            // "Mana Vault doesn't untap during your untap step." (deferred —
            // no engine surface for "doesn't untap" yet).
            // "At the beginning of your upkeep, if Mana Vault is tapped, you
            //  may pay {4}. If you don't, Mana Vault deals 1 damage to you."
            //  — upkeep TriggeredAbility (CR 603.1 / CR 603.4) attempts
            //  PayMana({4}) on the controller's pool and falls back to
            //  LoseLife(1) on failure (v1 "may" = pay-if-able, same prompt
            //  gap shared with the Pact cycle).
            // "{T}: Add {C}{C}{C}." Tap mana ability adding three colourless
            //  (Generic = 3 via ManaCost.Parse).
            // The single-arg dispatcher path attaches the upkeep trigger for
            // shape; use the (owner, triggers) overload to register it with
            // a live TriggerManager.
            "Mana Vault" => ManaVaultFactory.Create(owner),

            // Artifact — {0} (ManaCryptFactory). Mercadian Masques media
            // insert / Eternal Masters. "At the beginning of your upkeep,
            //  flip a coin. If you lose the flip, Mana Crypt deals 3 damage
            //  to you. {T}: Add {C}{C}." Upkeep TriggeredAbility (CR 603.1
            //  / CR 500.4) samples an injectable coin-flip seam and routes
            //  the 3 damage through Player.LoseLife — same v1 damage
            //  simplification as Mana Vault / Manabarbs / Dark Confidant
            //  (no DamageDealtEvent route). The single-arg dispatcher path
            //  uses System.Random.Shared as the flip source; use the
            //  (owner, triggers, coinLoses) overload for deterministic
            //  testing or live TriggerManager wiring.
            "Mana Crypt" => ManaCryptFactory.Create(owner),

            // Legendary Artifact — {0} (MoxAmberFactory). Dominaria.
            // "{T}: Add one mana of any color among legendary creatures
            //  and planeswalkers you control." Five WUBRG ManaAbility
            //  instances each gated on !IsTapped + a live scan of the
            //  controller's battlefield for a legendary creature or
            //  planeswalker whose printed colour (CR 105 via CardColors)
            //  includes the ability's colour. Opponent legendaries do
            //  NOT count — same scoping as MoxOpalFactory's metalcraft
            //  gate. Modal single-ability "any colour" shape deferred
            //  (same gap as Mox Opal / Delighted Halfling / City of Brass).
            "Mox Amber" => MoxAmberFactory.Create(owner),

            // Legendary Creature — Avatar {B}{B}{G}{G} 8/8 (HogaakFactory).
            // Modern Horizons. Trample + Convoke keyword markers wired.
            // Additional cost (exile two creature cards from controller's
            // graveyard — CR 601.2f) surfaced via
            // HogaakFactory.BuildExileTwoCreaturesAdditionalCost
            // (ExileCreaturesFromGraveyardAdditionalCost — generic shape so
            // other graveyard-exile additional costs can reuse it). Convoke
            // cost surfaced via HogaakFactory.BuildAlternativeCost — v1
            // returns printed cost unchanged (same gap as Chord of Calling).
            // "Can't be cast from hand" + "Hogaak's mana value is 8" are
            // documented but unenforced — no legal-cast-zones predicate on
            // SpellDefinition and no name-keyed mana-value override surface
            // exist yet. The dispatcher path here produces the correct card
            // shape (Trample + Convoke keyword markers attached); the
            // additional cost + Convoke alt cost are exposed via the
            // factory's Build* statics so callers can compose them into
            // the cast flow.
            "Hogaak, Arisen Necropolis" => HogaakFactory.Create(owner),

            // Enchantment — {B} (BridgeFromBelowFactory). Future Sight.
            // Two graveyard-resident triggered abilities (CR 603.6d,
            // activeZones = {Graveyard}). (1) "Whenever a nontoken creature
            // is put into your graveyard from the battlefield, if Bridge
            // from Below is in your graveyard, create a 2/2 black Zombie
            // creature token" — CardMovedEvent Battlefield → Graveyard
            // gated on Creature + !IsToken + owner == Bridge's controller,
            // with an interveningIf checking Bridge is in its controller's
            // graveyard (CR 603.4). (2) "When a creature is put into an
            // opponent's graveyard from the battlefield, exile Bridge from
            // Below" — CardMovedEvent Battlefield → Graveyard gated on
            // Creature landing in an opponent's graveyard, moves Bridge
            // graveyard → exile via raw zone mutation. Zombie token created
            // via TokenFactory.CreateOnBattlefield; routes through
            // ZoneService when the (owner, zoneService, eventBus, triggers)
            // overload is used so the spawned token publishes CardMovedEvent.
            // Token-colour identity (black) deferred — same gap as Crashing
            // Footfalls' green Rhinos and Wurmcoil's colourless Wurms.
            "Bridge from Below" => BridgeFromBelowFactory.Create(owner),

            // Legendary Creature — Phyrexian Angel {3}{W}{U}{B}{R}{G}
            // (AtraxaGrandUnifierFactory). Phyrexia: All Will Be One.
            // Flying + Vigilance + Deathtouch + Lifelink keyword markers
            // wired (CR 702.9 / 702.20 / 702.2 / 702.15). ETB triggered
            // ability: reveal the top ten cards of your library, put one
            // card of each card type into your hand, then place the rest
            // on the bottom of your library in a random order (CR 603.6a,
            // CR 701.20a). The single-arg dispatcher path attaches the
            // ETB trigger to the card shape without TriggerManager
            // wiring; callers can invoke AtraxaGrandUnifierFactory.ResolveEtb
            // directly to drive the reveal-and-pick. The Battle card
            // type (MoM+) is iterated transparently when added to the
            // CardType enum — no factory change needed.
            "Atraxa, Grand Unifier" => AtraxaGrandUnifierFactory.Create(owner),

            // Legendary Creature — Phyrexian Praetor {2}{B}{B} 4/5
            // (SheoldredTheApocalypseFactory). Dominaria United. Deathtouch
            // wired as a KeywordAbility marker. Draw trigger (CR 603.1):
            // "Whenever you draw a card, you gain 2 life and each opponent
            // loses 2 life" wired via Triggers.OnCardDrawnByPlayer filtered
            // to the controller — only the controller's draws fire it. The
            // single-arg dispatcher path here produces the correct card
            // shape and gains 2 life for the controller on execute; the
            // "each opponent loses 2" clause silently no-ops without an
            // opponent resolver (mirrors Liliana of the Veil's player-list-
            // resolver pattern). Use the (owner, opponentResolver, eventBus,
            // triggers) overload to wire full drain + bus-driven firing.
            "Sheoldred, the Apocalypse" => SheoldredTheApocalypseFactory.Create(owner),

            // Legendary Artifact — {4} (TheOneRingFactory). Tales of
            // Middle-earth. Indestructible keyword wired. ETB trigger
            // ("when this enters, if you cast it, you gain protection
            // from everything until your next turn") attached as a
            // structural no-op — no cast-marker, no "until your next
            // turn" cleanup, no player-scoped protection layer yet.
            // Upkeep trigger: lose 1 life per burden counter on The One
            // Ring (CR 500.4 / CR 603.1). Activated {T}: add a burden
            // counter, then draw a card for each burden counter on it
            // (add-then-draw ordering — first activation draws 1, second
            // draws 2, etc., per CR 608.2c). Single-arg dispatcher path
            // here produces the correct card shape without TriggerManager
            // wiring; use the (owner, eventBus, triggers) overload for
            // bus-driven trigger firing.
            "The One Ring" => TheOneRingFactory.Create(owner),

            // Instant — {U}{U}{U} (ArchmagesCharmFactory). Modern Horizons.
            // CR 700.2d — modal "Choose one —" with 3 printed modes
            // (counter spell / target player draws two / gain control of
            // nonland permanent with mv ≤ 1). The single-arg dispatcher
            // path produces the correct card shape; the bound
            // SpellDefinition is built on demand via
            // ArchmagesCharmFactory.BuildDefinition(caster, targetResolver,
            // stack[, effects]). Mode 2's ControlChangeEffect (CR 613.2,
            // Layer 2) is no-op without a live ContinuousEffectsService —
            // counter / draw modes still resolve fully. Mirrors
            // CrypticCommandFactory for the modal shape and
            // WishclawTalismanFactory for the control-change registration.
            "Archmage's Charm" => ArchmagesCharmFactory.Create(owner),

            // Creature — Phoenix {3}{R} 3/2 (ArclightPhoenixFactory).
            // Guilds of Ravnica. Flying + Haste keyword markers (CR 702.9 /
            // CR 702.10). Graveyard-resident triggered ability scoped to
            // activeZones = {Graveyard} (CR 603.6d): at the beginning of
            // combat on the controller's turn, if the controller has cast
            // three or more instant and/or sorcery spells this turn, return
            // Arclight Phoenix from graveyard to battlefield. Per-turn
            // instant+sorcery count is held in a closure private to the
            // card instance, incremented on every SpellCastEvent owned by
            // the controller whose card has CardType.Instant or Sorcery,
            // and reset on TurnStartedEvent when an event bus is supplied
            // (CR 500.1) — mirrors LedgerShredderFactory's per-turn-counter
            // pattern. CR 603.10 intervening "if" re-checks both the
            // ≥3-cast gate and the from-graveyard zone constraint at
            // resolution. "May" auto-accepted at v1 (same simplification
            // as Sneak Attack / Through the Breach). Single-arg dispatcher
            // path attaches the trigger to the card without bus-driven
            // count tracking or TriggerManager registration; use the
            // (owner, bus, triggers) overload for fully-wired behavior.
            "Arclight Phoenix" => ArclightPhoenixFactory.Create(owner),

            // Creature — Phoenix {2}{R}{R} 3/2 (PhoenixOfAshFactory).
            // Throne of Eldraine. Haste keyword marker (CR 702.10). The
            // printed "can attack as though it didn't have summoning
            // sickness as long as it has haste" rider collapses
            // observationally to Haste in v1 (CR 702.10b — haste already
            // bypasses summoning sickness for attack declaration). Escape
            // alt-cost ({3}{R}{R}, exile four other graveyard cards —
            // CR 702.143) deferred — same gap as Uro / Phlage / Cling to
            // Dust, blocked on the missing graveyard cast alt-cost +
            // multi-card-exile additional-cost primitive.
            "Phoenix of Ash" => PhoenixOfAshFactory.Create(owner),

            // Sorcery — {W} (PrismaticEndingFactory). Modern Horizons 2.
            // "Exile target nonland permanent with mana value less than
            //  or equal to the number of colors of mana spent to cast
            //  Prismatic Ending." Card shape only here; the resolve-time
            // SpellDefinition (single target-nonland-permanent request +
            // exile gated on mv ≤ colours-spent cap) is built on demand
            // via PrismaticEndingFactory.BuildSpellDefinition. Mana
            // provenance ledger is DEFERRED — callers supply a
            // Func<int> colors-spent provider; the single-arg path
            // defaults to PrismaticEndingFactory.DefaultColorsSpent = 1
            // (the printed {W} pip).
            "Prismatic Ending" => PrismaticEndingFactory.Create(owner),

            // Legendary Creature — Bird Bard {G}{W}{U} 3/4
            // (NaduWingedWisdomFactory). Modern Horizons 3. Flying +
            // "Whenever a creature you control becomes the target of a
            // spell or ability, that creature's controller may reveal the
            // top of their library; land → battlefield, otherwise →
            // hand. Triggers only twice each turn." v1 wires the
            // targeted-by trigger over TargetsChosenEvent (spell + ability
            // sources publish it), enforces the twice-per-turn cap with a
            // shared closure, and resets on TurnStartedEvent. The "may"
            // is auto-taken; routing the land ETB through ZoneService is
            // deferred to fully-wired callers — see factory xmldoc.
            "Nadu, Winged Wisdom" => NaduWingedWisdomFactory.Create(owner),

            // Sorcery — {2}{W}{W} (WrathOfGodFactory). Limited Edition Alpha
            // and many reprints. "Destroy all creatures. They can't be
            // regenerated." Card shape only at the dispatcher; the resolve
            // effect (every player's battlefield → graveyard sweep for every
            // Creature) is built on demand via WrathOfGodFactory.BuildResolveEffect.
            // The "can't be regenerated" rider and indestructible bypass are
            // lossy at v1 — same gap as DestroyAllCreaturesTemplate's sweep.
            "Wrath of God" => WrathOfGodFactory.Create(owner),

            // Sorcery — {2}{B}{B} (DamnationFactory). Planar Chaos.
            // Functional reprint of Wrath of God in black; resolve effect
            // delegates to WrathOfGodFactory.BuildResolveEffect.
            "Damnation" => DamnationFactory.Create(owner),

            // Instant — {4}{B} (MurderousCutFactory). Khans of Tarkir.
            // CR 702.66 — Delve. "Delve" marker keyword wired; the cost
            // mechanic itself lives in DelveCost + SpellCastFlow (same
            // wire-up as Treasure Cruise / Dig Through Time). Resolve-time
            // SpellDefinition ("Destroy target creature" — CR 701.7) is
            // built on demand via MurderousCutFactory.BuildSpellDefinition.
            // Indestructible + "can't be regenerated" riders deferred — same
            // lossy MVP as DestroySpellFactory.DestroyCreatureSpell.
            "Murderous Cut" => MurderousCutFactory.Create(owner),

            // Instant — {B} (ClingToDustFactory). Theros Beyond Death.
            // CR 700.2d — modal "Choose one —" (2 printed modes): (0) exile
            // target card from a graveyard + gain mv life; (1) exile target
            // card from a graveyard + draw 1 + lose 1 life. Card shape only
            // here; the bound SpellDefinition is built on demand via
            // ClingToDustFactory.BuildSpellDefinition(caster, resolver). Per-
            // mode TargetRequests with MinTargets=0 so unchosen modes don't
            // gate the cast (mirrors ArchmagesCharmFactory). Escape alt-cost
            // ({2}{B}, exile two other graveyard cards — CR 702.143) deferred,
            // same gap as Uro / Phlage.
            "Cling to Dust" => ClingToDustFactory.Create(owner),

            // Instant — {U}{B} (DrownInTheLochFactory). Throne of Eldraine.
            // CR 700.2d — modal "Choose one" with two modes (counter target
            // spell mv ≤ X / destroy target creature mv ≤ X). X is the
            // largest mana value among cards in opponents' graveyards,
            // computed at resolution time from
            // ChosenSpellParams.AllPlayers. The single-arg dispatcher path
            // produces the correct card shape; the bound SpellDefinition is
            // built on demand via DrownInTheLochFactory.BuildDefinition(
            // caster, targetResolver, stack). Mirrors ArchmagesCharm /
            // CrypticCommand for the modal shape; mv-≤-X gate is enforced
            // at resolution (CR 608.2b) since the engine's target prompt
            // doesn't yet express "mana value X or less".
            "Drown in the Loch" => DrownInTheLochFactory.Create(owner),

            // Land — Zendikar / Modern reprints (ValakutTheMoltenPinnacleFactory).
            // "Valakut, the Molten Pinnacle enters tapped unless you control
            //  five or more other Mountains. Whenever a Mountain enters under
            //  your control, if you control at least five other Mountains,
            //  you may have Valakut, the Molten Pinnacle deal 3 damage to
            //  any target. {T}: Add {R}." CR 614.1c (conditional ETB-tapped)
            //  + CR 603.1 / 603.4 (intervening-if landfall-style trigger).
            // The single-arg dispatcher path here attaches the {T}: Add {R}
            // mana ability + landfall-style trigger for shape; the
            // conditional ETB-tapped replacement is omitted (no
            // ReplacementBus available at dispatch). Use
            // ValakutTheMoltenPinnacleFactory.Create(owner, replacements,
            // triggers) for fully-wired behaviour. "You may" prompt +
            // agent-driven "any target" pick deferred (v1 honours pre-set
            // ChosenTargets and auto-accepts the may).
            "Valakut, the Molten Pinnacle" => ValakutTheMoltenPinnacleFactory.Create(owner),

            // Land — Island (MysticSanctuaryFactory). Modern Horizons 2.
            // "{T}: Add {U}. When Mystic Sanctuary enters the battlefield,
            //  if you control three or more other Islands, put target instant
            //  or sorcery card from your graveyard on top of your library."
            // CR 603.4 (intervening-if: 3+ other Islands) + CR 608.2b
            // (illegal-on-resolution guard). Single-arg dispatcher path
            // attaches the {T}: Add {U} mana ability + ETB trigger for
            // shape; use MysticSanctuaryFactory.Create(owner, triggers)
            // for bus-driven trigger registration.
            "Mystic Sanctuary" => MysticSanctuaryFactory.Create(owner),

            // Sorcery — {1}{R} (MizziumMortarsFactory). Return to Ravnica.
            // CR 702.96 — Overload. Default printed cast deals 4 damage to
            // target creature; the overload alt-cost {4}{R}{R} rewrites
            // "target" to "each" (CR 702.96b), yielding "4 damage to each
            // creature you don't control". OverloadAlternativeCost stub in
            // Costs/ gates the alt-cost from-hand and carries an
            // IsOverloaded flag, but the alt-cost is not yet plumbed
            // through SpellCastFlow to the resolving stack object — so v1
            // ships the structural overloaded branch as a wasOverloaded
            // toggle on MizziumMortarsFactory.BuildSpellDefinition (same
            // posture as Burst Lightning's wasKicked toggle). Production
            // casts at the single-arg dispatcher path ship as
            // not-overloaded — 4 damage to one target creature.
            "Mizzium Mortars" => MizziumMortarsFactory.Create(owner),

            // Creature — Minotaur Warrior {1}{R} 2/1 (EarthshakerKhenraFactory).
            // Hour of Devastation. Haste keyword wired. ETB triggered ability
            // (CR 603.6a) declares a 1..1 "target creature with power 2 or less"
            // TargetRequest and on resolution registers a CombatRestrictionEffect
            // with CombatRestriction.CannotBlock targeting the chosen creature
            // (CR 509.1c) — EOT-scoped (CR 514.2) via the default ExpiresAtEndOfTurn.
            // Resolution rechecks "power 2 or less" + still-on-battlefield (CR 608.2b);
            // restriction is registered against the target's
            // ContinuousEffectsService (Creature.ActiveEffects) where the combat
            // validator looks. Eternalize {5}{R}{R} (CR 702.117) is deferred —
            // needs an exile-cost / graveyard-to-token alt-cost (sibling of
            // Unearth / Priest of Fell Rites). "Power 2 or less" target-legality
            // filter at choose-time deferred — same posture as Solitude / Kraul
            // Harpooner; engine accepts any Creature target and the resolve-time
            // gate enforces the threshold.
            "Earthshaker Khenra" => EarthshakerKhenraFactory.Create(owner),

            // Creature — Human Wizard {1}{W} 1/3 (DrannithMagistrateFactory).
            // Ikoria: Lair of Behemoths. Printed static "Your opponents
            // can't cast spells from anywhere other than their hands."
            // (CR 113.6) wired via CastFromHandOnlyRestrictionEffect when
            // the runtime (owner, opponentResolver, eventBus) overload is
            // used. CastSpellAction.FromZone is the validator's lookup —
            // callers that stamp a non-Hand FromZone for an opponent's
            // cast are rejected with RuleViolation 113.6. The single-arg
            // dispatcher path here produces the correct card shape only
            // (no live opponent restriction). Production casts that don't
            // yet stamp a source zone are unaffected by the restriction
            // — same posture as other CR 113.6 / CR 601.2a from-zone
            // sensitive effects (Snapcaster grants flashback via a
            // separate path).
            "Drannith Magistrate" => DrannithMagistrateFactory.Create(owner),

            // Creature — Human Cleric {1}{W} 2/2 (ContainmentPriestFactory).
            // Commander 2014 / Modern Horizons 2. Flash keyword wired.
            // Printed replacement effect (CR 614): "If a nontoken creature
            // would enter the battlefield and it wasn't cast, exile it
            // instead." Wired via ContainmentPriestExileReplacementEffect
            // when the runtime (owner, replacementBus, eventBus) overload
            // is used. The single-arg dispatcher path here produces the
            // correct card shape + Flash keyword without live replacement
            // registration. ZoneMoveIntent.WasCast enforcement deferred
            // until ZoneService consults the ReplacementBus on all ETB paths.
            "Containment Priest" => ContainmentPriestFactory.Create(owner),

            // Creature — Human Wizard {W}{U} 2/2 (MeddlingMageFactory).
            // Planeshift / various reprints. ETB "choose a nonland card name"
            // — the single-arg dispatcher path defaults to an empty name (no
            // restriction). Printed static (CR 601.3): "Spells with the
            // chosen name can't be cast." Wired via
            // MeddlingMageCastRestrictionEffect + CastingRestrictions when
            // the runtime (owner, chosenName, eventBus) overload is used
            // (ActionValidator.ValidateCastSpell rejects casts via
            // RuleViolation 601.3). LTB releases the block automatically.
            "Meddling Mage" => MeddlingMageFactory.Create(owner),

            // Creature — Kavu {G}{W} 2/2 (TerritorialKavuFactory).
            // Modern Horizons 2. Domain — gets +1/+1 for each basic land
            // type among lands you control (CR 702.16 / CR 613.1g, Layer
            // 7c). Wired via DomainPumpStaticEffect + ETB/LTB lifecycle
            // when the (owner, effects, eventBus, triggers) overload is
            // used. Attack trigger (CR 508.1f): discard a card, then draw
            // a card — v1 deterministic first-card-in-hand pick; "you may"
            // + agent-driven choice deferred. The single-arg dispatcher
            // path produces the correct card shape without live layers
            // service or TriggerManager wiring.
            "Territorial Kavu" => TerritorialKavuFactory.Create(owner),

            // Legendary Creature — Human Soldier {1}{W} 2/1
            // (ThaliaGuardianOfThrabenFactory). Dark Ascension.
            // First strike keyword wired. Static "Noncreature spells cost
            // {1} more to cast" wired via SpellCostIncreaseAbility on the
            // card (CR 117.7 / CR 601.2f) — symmetric across all casters.
            // The cost rider is consulted by CostReduction.GetEffectiveCost
            // when allPlayers is supplied; inert while Thalia is off the
            // battlefield.
            "Thalia, Guardian of Thraben" => ThaliaGuardianOfThrabenFactory.Create(owner),

            // Creature — Ouphe {1}{G/W}{G/W} 3/2 (KitchenFinksFactory).
            // Shadowmoor / Modern Horizons 2. "When Kitchen Finks enters the
            // battlefield, you gain 2 life. Persist (When this creature dies,
            // if it had no -1/-1 counters on it, return it to the battlefield
            // under its owner's control with a -1/-1 counter on it.)"
            // ETB lifegain (CR 603.6a + CR 119.3) and Persist (CR 702.78)
            // triggers both wired. Hybrid mana cost {G/W} via ManaCost.Parse
            // (CR 107.4e — same HybridPip path as Boros Reckoner {R/W}).
            "Kitchen Finks" => KitchenFinksFactory.Create(owner),

            // Instant — {1}{U} (RemandFactory). Ravnica: City of Guilds.
            // "Counter target spell. If that spell is countered this way,
            //  put it into its owner's hand instead of into that player's
            //  graveyard. Draw a card." Card shape only here; the resolve-time
            // SpellDefinition (counter + hand-return + draw) is built on demand
            // via RemandFactory.BuildDefinition(caster, targetResolver, stack).
            "Remand" => RemandFactory.Create(owner),

            // Creature — Human Monk {W}{U}{R} 3/3 (MantisRiderFactory). Khans of Tarkir.
            // Flying + Vigilance + Haste keyword markers wired (CR 702.9, 702.20, 702.10).
            // Vanilla three-keyword creature — no activated abilities, triggered
            // abilities, or static effects. Core Modern Humans piece.
            "Mantis Rider" => MantisRiderFactory.Create(owner),

            // Creature — Human Wizard {1}{W}{U} 2/3 (ReflectorMageFactory). Oath of the Gatewatch.
            // ETB triggered ability (CR 603.6a): bounce target creature an opponent controls
            // to its owner's hand (CR 701.10). CR 608.2b: if target is no longer on
            // battlefield at resolution, ability does nothing. The single-arg dispatcher
            // path here produces the correct card shape with the ETB trigger attached
            // (raw zone-move fallback); use the (owner, zoneService, eventBus, triggers)
            // overload for full ZoneService routing + TriggerManager wiring. Name-based
            // cast restriction ("can't cast same-named spells until your next turn") deferred
            // — no delayed-until-next-turn NamedCastRestriction surface in v1.
            "Reflector Mage" => ReflectorMageFactory.Create(owner),

            // Instant — {1}{U} (ManaLeakFactory). Stronghold / various reprints.
            // "Counter target spell unless its controller pays {3}." Card shape
            // only here; the resolve-time SpellDefinition (counter-unless-pay-{3})
            // is built on demand via ManaLeakFactory.BuildDefinition(targetResolver,
            // stack). Mirrors DazeFactory's "unless pay" pattern with N=3.
            "Mana Leak" => ManaLeakFactory.Create(owner),

            // Creature — Merfolk Wizard {1}{U} 2/1 (SilvergillAdeptFactory).
            // Lorwyn / various reprints. As an additional cost to cast this
            // spell, reveal a Merfolk card from your hand or pay {3}
            // (v1: RevealMerfolkOrPay3 keyword marker — enforcement deferred).
            // ETB trigger: controller draws a card (CR 603.6a). Single-arg
            // dispatcher path attaches the ETB trigger; use the
            // (owner, eventBus, triggers) overload for bus-driven firing.
            "Silvergill Adept" => SilvergillAdeptFactory.Create(owner),

            // Creature — Merfolk Wizard {U} 1/1 (CursecatcherFactory).
            // Shadowmoor / various reprints. Activated ability — Sacrifice
            // Cursecatcher: Counter target spell unless its controller pays
            // {1}. Sacrifice-in-effect + counter-unless-pay wired (v1:
            // auto-resolve payment, no agent prompt). Single-arg dispatcher
            // path passes no live stack; use the (owner, stack) overload for
            // a fully-wired counter effect.
            "Cursecatcher" => CursecatcherFactory.Create(owner),

            // Creature — Merfolk Wizard {U}{U} 2/2 (MerfolkTricksterFactory).
            // Dominaria. Flash keyword wired (CR 702.8). Single ETB triggered
            // ability (CR 603.6a) declares a 1..1 "target creature an opponent
            // controls" TargetRequest (Intent: BotIntent.Removal); on resolution
            // taps the chosen creature via Permanent.Tap (CR 701.20 — guarded
            // against already-tapped state) and registers a
            // LoseAllAbilitiesEffect scoped to the target with
            // expiresAtEndOfTurn: true against the target's
            // ContinuousEffectsService (Creature.ActiveEffects) — Layer 6 strip
            // expires at the cleanup step (CR 613.6 / 514.2 — same EOT scope as
            // the UntilEndOfTurn family of pump/keyword grants). Resolution
            // rechecks still-on-battlefield + opponent-controlled (CR 608.2b);
            // shape-only path silently no-ops the lose-abilities grant when
            // target.ActiveEffects is null. "Creature an opponent controls"
            // choose-time filter deferred — same posture as Solitude /
            // Earthshaker Khenra; the resolve-time recheck enforces the
            // controller scope. Caps Modern Merfolk pillar at ~85%.
            "Merfolk Trickster" => MerfolkTricksterFactory.Create(owner),

            // Creature — Human Soldier {W} 1/1 (ChampionOfTheParishFactory).
            // Innistrad. "Whenever another Human enters the battlefield under
            // your control, put a +1/+1 counter on Champion of the Parish."
            // ETB-other-Human trigger wired via EventTriggerCondition over
            // CardMovedEvent: Creature + Human subtype + controller match +
            // not self. Active only while Champion is on the battlefield.
            // Single-arg dispatcher path attaches the trigger for shape tests
            // without TriggerManager registration; use the (owner, triggers)
            // overload for bus-driven firing.
            "Champion of the Parish" => ChampionOfTheParishFactory.Create(owner),

            // Creature — Human Soldier {1}{W} 1/1 (ThaliaLieutenantFactory).
            // Shadows over Innistrad. Two triggered abilities:
            //   1. ETB-self — "When Thalia's Lieutenant enters, put a +1/+1
            //      counter on each other Human you control." Wired via
            //      Triggers.OnEnterBattlefieldSelf; iterates controller's
            //      battlefield for Humans excluding self.
            //   2. ETB-other-Human — same predicate as Champion of the Parish.
            //      Put a +1/+1 counter on Lieutenant when another Human enters.
            // Single-arg dispatcher path attaches both triggers for shape tests
            // without TriggerManager registration; use the (owner, triggers)
            // overload for bus-driven firing.
            "Thalia's Lieutenant" => ThaliaLieutenantFactory.Create(owner),

            // Creature — Kor Spirit {1}{W}{W} 2/2 (SkyclaveApparitionFactory).
            // Zendikar Rising. ETB exile "up to one target nonland, nontoken
            // permanent an opponent controls with mana value 4 or less"
            // (MinTargets=0, MaxTargets=1 TargetRequest). LTB creates an X/X
            // blue Illusion creature token under the exiled permanent's
            // controller, X = exiled card's mana value. If 0 targets chosen,
            // LTB no-ops. Single-arg dispatcher path produces the correct
            // card shape without TriggerManager wiring; use the
            // (owner, eventBus, triggers) overload for fully-wired behaviour.
            // Token colour (blue) deferred — same gap as Crashing Footfalls.
            "Skyclave Apparition" => SkyclaveApparitionFactory.Create(owner),

            // Creature — Cat Cleric {1}{W} 2/2 (LeoninArbiterFactory).
            // Scars of Mirrodin. "Players can't search their libraries unless
            // they pay {2}." v1 structural shape only: a
            // LeoninArbiterSearchRestrictionEffect marker is registered on
            // the ContinuousEffectsService (via the wired overload) while
            // Leonin Arbiter is on the battlefield; actual search-tax
            // enforcement is deferred (no unified search-library surface yet).
            // The single-arg dispatcher path here produces the correct card
            // shape only (no live marker). Use
            // LeoninArbiterFactory.Create(owner, continuousEffectsService)
            // for the wired form.
            "Leonin Arbiter" => LeoninArbiterFactory.Create(owner),

            // Instant — {B}{G} (AbruptDecayFactory). Return to Ravnica.
            // "This spell can't be countered. Destroy target nonland
            //  permanent with mana value 3 or less." (CR 701.7 / CR 202.3.)
            // "Can't Be Countered" keyword marker attached for structural
            // observability; enforcement deferred (same posture as
            // Veil of Summer / Cavern of Souls). Card shape only here;
            // the resolve-time SpellDefinition (mv ≤ 3 gate + destroy)
            // is built on demand via
            // AbruptDecayFactory.BuildSpellDefinition.
            "Abrupt Decay" => AbruptDecayFactory.Create(owner),

            // Instant — {B}{R} (TerminateFactory). Planeshift / various reprints.
            // "Destroy target creature. It can't be regenerated." (CR 701.7.)
            // Card shape only here; the resolve-time SpellDefinition
            // (target-creature request + destroy) is built on demand via
            // TerminateFactory.BuildSpellDefinition. "Can't be regenerated"
            // rider deferred — no regeneration shield surface in the engine
            // yet (same gap as Wrath of God / Day of Judgment).
            "Terminate" => TerminateFactory.Create(owner),

            // Instant — {U}{R} (IzzetCharmFactory). Return to Ravnica.
            // CR 700.2d — modal "Choose one —" with 3 printed modes:
            // (0) counter noncreature spell unless pay {2} (v1 auto-resolve),
            // (1) deal 2 damage to any target,
            // (2) draw two cards, then discard two cards (v1 deterministic
            // last-two-in-hand). Card shape only here; the bound
            // SpellDefinition is built on demand via
            // IzzetCharmFactory.BuildDefinition(caster, targetResolver,
            // allPlayers, stack).
            "Izzet Charm" => IzzetCharmFactory.Create(owner),

            // Creature — Human Wizard {1}{U}{R} 0/3 (IzzetStaticasterFactory).
            // Return to Ravnica. Flash keyword wired. Activated ability {T}:
            // 1 damage to target creature and each other creature with the
            // same name as that creature. The single-arg dispatcher path
            // produces the correct card shape (Flash + tap-ping ability);
            // the name-sweep body no-ops without an allCreaturesResolver.
            // Use IzzetStaticasterFactory.Create(owner, allCreaturesResolver)
            // for fully-wired same-name sweep.
            "Izzet Staticaster" => IzzetStaticasterFactory.Create(owner),

            // Instant — {1}{B}{R} (KolaghansCommandFactory). Dragons of Tarkir.
            // CR 700.2e — modal "Choose two —" with 4 printed modes
            // (return creature from graveyard to hand / 2 damage to any target /
            // target player discards a card / destroy target artifact). The
            // single-arg dispatcher path produces the correct card shape;
            // the bound SpellDefinition is built on demand via
            // KolaghansCommandFactory.BuildDefinition(caster, targetResolver,
            // allPlayers, stack, chosenModes). Same modal structure as
            // CrypticCommandFactory — 4 TargetRequests with MinTargets=0.
            "Kolaghan's Command" => KolaghansCommandFactory.Create(owner),

            // Creature — Vampire Spirit {1}{B} 2/1 (BloodghastFactory). Zendikar.
            // Can't block (permanent CombatRestriction — wired via full overload).
            // Landfall trigger (CR 603.6d — graveyard-resident): when a land enters
            // under your control, return Bloodghast from your graveyard to the
            // battlefield (v1 auto-accepts "you may"). Haste while an opponent has
            // ≤10 life (v1 snapshot — deferred dynamic conditional keyword grant).
            // The single-arg dispatcher path produces the correct card shape with
            // the landfall trigger attached but not TriggerManager-registered, and
            // without the can't-block or haste wiring.
            "Bloodghast" => BloodghastFactory.Create(owner),

            // Land (non-basic) — (CreepingTarPitFactory). Worldwake.
            // Enters tapped (EntersTappedReplacement — wired via full overload).
            // {T}: Add {U} and {T}: Add {B} — two ManaAbility instances wired.
            // {1}{U}{B}: Until EOT, becomes 3/2 Elemental creature still a land +
            // gains Shroud — Layer 4 (CreepingTarPitAnimateEffect) + Layer 7b
            // (CreepingTarPitBecomesPTEffect) + Layer 6 (CreepingTarPitShroudEffect).
            // Colour identity of the animated form (blue + black Layer 5 colour-set)
            // deferred — no colour-changing effect primitive yet. Combat math via
            // Compute deferred — same gap as Mutavault (Land runtime instance).
            "Creeping Tar Pit" => CreepingTarPitFactory.Create(owner),

            // Creature — Human Shaman {1}{R} 2/1 (YoungPyromancerFactory).
            // Magic 2014. Whenever you cast an instant or sorcery spell,
            // create a 1/1 red Elemental creature token (CR 603.1).
            // Token colour (red) deferred — same gap as Goblin Rabblemaster.
            // The single-arg dispatcher path produces the correct card shape
            // without trigger-manager wiring. Use the (owner, eventBus,
            // triggers) overload for fully-wired behavior.
            "Young Pyromancer" => YoungPyromancerFactory.Create(owner),

            // Creature — Wolf {G} 1/1 (YoungWolfFactory). Innistrad.
            // Undying keyword marker (CR 702.93). Undying triggered ability
            // (CR 702.93b) wired via UndyingFactory.Build — fires on
            // Battlefield → Graveyard CardMovedEvent with intervening-if
            // "no +1/+1 counters at death" (CR 603.4); on resolve raw-moves
            // graveyard → battlefield, clears counters (CR 121.2), and adds
            // one +1/+1 counter. The single-arg dispatcher path attaches the
            // trigger without TriggerManager registration. Use the (owner,
            // triggers) overload for bus-driven trigger firing (mirrors
            // NihilSpellbombFactory's two-arg pattern).
            "Young Wolf" => YoungWolfFactory.Create(owner),

            // Creature — Human Monk {2}{W} 2/2 (MonasteryMentorFactory).
            // Fate Reforged. Prowess (CR 702.108) — whenever you cast a
            // noncreature spell, +1/+1 until end of turn (wired via
            // ProwessFactory.Build when a ContinuousEffectsService is
            // supplied). Whenever you cast a noncreature spell, create a
            // 1/1 white Monk creature token with prowess. Token colour (white)
            // and prowess on the token are deferred — same gap as
            // Goblin Rabblemaster / Crashing Footfalls token gaps.
            // The single-arg dispatcher path produces the correct card shape
            // without trigger-manager or effects wiring. Use the
            // (owner, eventBus, triggers, effects) overload for fully-wired
            // behavior.
            "Monastery Mentor" => MonasteryMentorFactory.Create(owner),

            // Creature — Human Monk {R} 1/2 (MonasterySwiftspearFactory).
            // Khans of Tarkir + many reprints. Haste (CR 702.10) +
            // Prowess (CR 702.108) — "Whenever you cast a noncreature
            // spell, this creature gets +1/+1 until end of turn." Haste +
            // Prowess KeywordAbility markers always attached for shape
            // inspection. Prowess mechanic itself wired via
            // ProwessFactory.Build when a ContinuousEffectsService is
            // supplied. The single-arg dispatcher path produces the
            // correct card shape without trigger-manager or effects
            // wiring. Use the (owner, eventBus, triggers, effects)
            // overload for fully-wired behavior.
            "Monastery Swiftspear" => MonasterySwiftspearFactory.Create(owner),

            // Instant — {B}{G} (AssassinsTrophyFactory). Guilds of Ravnica.
            // "Destroy target permanent an opponent controls. Its controller
            //  searches their library for a basic land card, puts it onto
            //  the battlefield, then shuffles." Resolve-time opponent check
            // enforced; library shuffle deferred (same gap as PathToExile /
            // SearchForTomorrow).
            "Assassin's Trophy" => AssassinsTrophyFactory.Create(owner),

            // Instant — {2}{G} (BeastWithinFactory). New Phyrexia / various.
            // "Destroy target permanent. Its controller creates a 3/3 green
            //  Beast creature token." Any permanent is a legal target —
            // including your own. Token colour (green) deferred (same gap as
            // Pact of the Titan / Crashing Footfalls).
            "Beast Within" => BeastWithinFactory.Create(owner),

            // Artifact — {1} (RelicOfProgenitusFactory). Shards of Alara / reprints.
            // Common Modern graveyard-hate. {T}: target player exiles a card from
            // their graveyard (1..1 TargetRequest; v1 auto-pick first card).
            // {1}, Exile Relic: exile all cards from all graveyards; draw a card.
            // Self-exile cost performed by effect closure (stub pattern mirrors
            // Mishra's Bauble / Engineered Explosives). The single-arg dispatcher
            // path sweeps only the controller's graveyard; use the
            // (owner, allPlayersResolver) overload for full all-graveyards sweep.
            "Relic of Progenitus" => RelicOfProgenitusFactory.Create(owner),

            // Artifact — {B} (NihilSpellbombFactory). Scars of Mirrodin / reprints.
            // Common Modern graveyard-hate. {T}, Sacrifice ~: exile all cards from
            // target player's graveyard (1..1 TargetRequest; v1 auto-pick target
            // from ChosenTargets). Dies trigger (CR 603.6c): may pay {B}; if you
            // do, draw a card — v1 auto-pays when pool has {B}. Single-arg
            // dispatcher path attaches the trigger without TriggerManager
            // registration; use the (owner, triggers) overload for bus-driven
            // trigger firing.
            "Nihil Spellbomb" => NihilSpellbombFactory.Create(owner),

            // Creature — Spirit {G}{G} 2/1 (StrangleRootGeistFactory).
            // Dark Ascension. Haste (CR 702.10) + Undying (CR 702.93)
            // keyword markers wired. Undying trigger built via the canonical
            // UndyingFactory helper (same path as Young Wolf / Geralf's
            // Messenger / Butcher Ghoul): on death without a +1/+1 counter
            // the creature returns to its owner's battlefield with one
            // +1/+1 counter; interveningIf (CR 603.4) gates the second
            // death so the post-return Geist stays dead. Both keyword
            // markers survive the Undying return so CombatAbilities.HasHaste
            // still reads true — the returned Geist can attack the same
            // turn (CR 302.1 bypass via Haste). Single-arg dispatcher path
            // attaches both keyword markers + the Undying trigger to the
            // card shape without TriggerManager registration; use the
            // (owner, triggers) overload for bus-driven trigger firing.
            "Strangleroot Geist" => StrangleRootGeistFactory.Create(owner),

            // Enchantment — Class {U}{R} (StormchasersTalentFactory).
            // Modern Horizons 3. ETB trigger (CR 603.6a) creates a 1/1
            // Mercenary creature token with a "Prowess" KeywordAbility
            // marker (CR 702.108) via TokenFactory.CreateOnBattlefield.
            // Single-arg dispatcher path attaches the trigger to the card
            // shape without ZoneService / TriggerManager wiring; use the
            // (owner, zoneService, triggers) overload for bus-driven
            // firing + ZoneService-routed token entry. Class leveling
            // (CR 716) DEFERRED — {1}{U}{R}: Level 2 and {3}{U}{R}: Level 3
            // activated abilities are not wired (blocked on per-activated-
            // ability sorcery-speed gate + ClassState binder hook + the
            // sequential-level cost restriction; same gap as Tasigur's
            // {B}{G}{U} on the sorcery-speed axis). Level 2 cast-trigger
            // (Mercenary deals 1 damage) and Level 3 loot trigger DEFERRED
            // with the leveling primitive — both are simple bodies that
            // mirror PsychicFrog / FaithlessLooting / LedgerShredder once
            // the level gate exists. Token colour identity (blue + red)
            // deferred — same gap as Esika's Chariot's green Cats.
            // Prowess pump on the token deferred — KeywordAbility marker
            // attached but no live ContinuousEffectsService is threaded
            // through TokenFactory (same posture as MonasteryMentor's
            // Monk tokens).
            "Stormchaser's Talent" => StormchasersTalentFactory.Create(owner),

            _ => new Card(name, ""),
        };

        card.SetOwner(owner);

        if (card is Land && card.HasSupertype(CardSupertype.Basic))
        {
            AttachBasicLandMana(card, owner);
        }
        return card;
    }

    private static Land Land(string name, CardSubtype subtype) =>
        new(name, new[] { CardSupertype.Basic }, new[] { subtype });

    private static void AttachBasicLandMana(ICard land, Player controller)
    {
        var color = land.HasSubtype(CardSubtype.Mountain) ? "R"
                  : land.HasSubtype(CardSubtype.Forest)   ? "G"
                  : land.HasSubtype(CardSubtype.Plains)   ? "W"
                  : land.HasSubtype(CardSubtype.Island)   ? "U"
                  : land.HasSubtype(CardSubtype.Swamp)    ? "B"
                  : land.HasSubtype(CardSubtype.Wastes)   ? "C"
                  : null;

        if (color != null)
        {
            land.AddAbility(new ManaAbility(land, controller, ManaCost.Parse(color)));
        }
    }
}
