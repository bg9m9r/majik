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
            // type "Merfolk Wizard" — Merfolk not yet in CardSubtype, so
            // only Wizard is assigned. Full lifecycle via
            // HarbingerOfTheSeasFactory.Create(owner, effects, eventBus).
            "Harbinger of the Seas" => HarbingerOfTheSeasFactory.Create(owner),

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

            // Sorcery — {2}{R} (RiftBoltFactory). 3 damage to any target;
            // Suspend 1—{R} (CR 702.62). Spell-def and suspend alt cost
            // built on demand via RiftBoltFactory.BuildSpellDefinition /
            // BuildSuspendCost — caller wires them through SpellCastFlow
            // and SuspendedCardRegistry.
            "Rift Bolt" => RiftBoltFactory.Create(owner),

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

            // Sorcery — {1}{R} (TribalFlamesFactory). Onslaught / Modern Horizons 2.
            // "Tribal Flames deals X damage to any target, where X is the
            //  number of basic land types among lands you control." CR 702.16
            //  (Domain). Card shape only here; the resolve-time SpellDefinition
            //  is built on demand via TribalFlamesFactory.BuildSpellDefinition,
            //  which uses ContinuousEffectsService.Compute(land).Subtypes when
            //  a live layers service is supplied so layer-4 retypes (Blood
            //  Moon, Spreading Seas, Urborg, Yavimaya) feed through.
            "Tribal Flames" => TribalFlamesFactory.Create(owner),

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

            // Creature — Dragon {3}{U}{U} 3/3 (MurktideRegentFactory).
            // Flying + Delve marker keywords wired. ETB trigger: exile target
            // instant or sorcery card from a graveyard; enters with +1/+1
            // counters = delve-exiled-count + (ETB exile ? 1 : 0) per CR 122.1g.
            // The delve count is plumbed via Card.PendingDelveExiledCount,
            // stamped by SpellCastFlow when DelveCost is paid and consumed
            // by the ETB effect.
            "Murktide Regent" => MurktideRegentFactory.Create(owner),

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

            // Legendary Artifact — {5} (PyromancersGogglesFactory). Magic Origins.
            // "{T}: Add {R}. When you spend this mana to cast an instant or
            //  sorcery spell, copy that spell. You may choose new targets for
            //  the copy." v1 ships {T}: Add {R} as a single ManaAbility plus a
            //  structural copy-rider TriggeredAbility (SpellCastEvent, gated on
            //  controller + Instant|Sorcery) whose effect is a no-op. Mana-
            //  provenance ledger ("when you spend this mana") + stack-copy
            //  primitive ("copy that spell") + new-targets prompt all deferred.
            "Pyromancer's Goggles" => PyromancersGogglesFactory.Create(owner),

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
