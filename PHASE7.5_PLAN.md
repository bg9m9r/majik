# Phase 7.5: Oracle Text Parsing and Layer System - Implementation Plan

## Overview

Phase 7.5 implements a system to parse oracle text (natural language card rules text) and automatically generate triggered abilities, static abilities, replacement effects, and activated abilities. Parsed abilities and effects are stored in the SQLite database, tied to cards. The system uses pattern-based parsing for common cases and AI (LLM API) for complex/unparseable oracle text. Effects are built into the engine as a library, with database references to those effects. This phase also implements the Magic rules layer system (Rule 613) for correctly applying continuous effects in the proper order.

**Status**: ⏳ Not Started  
**Goal**: Parse oracle text into structured abilities, store in database, and implement the layer system for continuous effects  
**Estimated Effort**: 4-5 weeks  
**Dependencies**: Phase 4 (Card System), Phase 6 (Rules Engine), Phase 7 (Database System)

## Objectives

1. **Database Schema**: Create tables for storing parsed abilities and effects tied to cards
2. **Effect Library**: Build a library of effect implementations in the engine
3. **Oracle Text Parser**: Parse natural language oracle text into structured ability components
4. **AI-Assisted Parsing**: Use LLM API for complex/unparseable oracle text
5. **Trigger Detection**: Identify and extract triggered abilities from oracle text
6. **Static Ability Extraction**: Extract static abilities and continuous effects
7. **Activated Ability Parsing**: Parse activated abilities with costs and effects
8. **Database Storage**: Store parsed abilities with references to effect library
9. **Layer System**: Implement the 7-layer system for continuous effects (Rule 613)
10. **Timestamp System**: Track timestamps for effect ordering
11. **Dependency Resolution**: Handle effect dependencies within layers
12. **Integration**: Connect parsed abilities to existing ability system

## Magic Rules Reference: Layers (Rule 613)

### Layer System Overview

The layer system determines the order in which continuous effects are applied to objects. All applicable continuous effects are applied in a series of layers:

**Layer 1**: Rules and effects that modify copiable values
- **Layer 1a**: Copiable effects (copy effects, merging)
- **Layer 1b**: Face-down spells and permanents

**Layer 2**: Control-changing effects

**Layer 3**: Text-changing effects

**Layer 4**: Type-changing effects (card type, subtype, supertype)

**Layer 5**: Color-changing effects

**Layer 6**: Ability-adding effects, keyword counters, ability-removing effects

**Layer 7**: Power- and/or toughness-changing effects
- **Layer 7a**: Characteristic-defining abilities (CDAs) that define P/T
- **Layer 7b**: Effects that set P/T to a specific number
- **Layer 7c**: Effects and counters that modify P/T
- **Layer 7d**: Effects that switch P/T

### Timestamp System (Rule 613.7)

Within a layer or sublayer, effects are applied in timestamp order:
- Continuous effects from static abilities share the timestamp of their source object
- Continuous effects from resolving spells/abilities receive a timestamp when created
- Objects receive timestamps when entering zones
- Simultaneous timestamps use APNAP order (Active Player, Nonactive Player)

### Dependency System (Rule 613.8)

If an effect depends on another effect (changing what it applies to or what it does), the dependent effect waits until the other effect is applied. Dependency overrides timestamp order.

## Architecture Overview

### Key Design Decisions

1. **Database-First**: Parsed abilities are stored in SQLite, not parsed on-the-fly
2. **Effect Library**: Common effects are built into the engine as reusable components
3. **Reference System**: Database stores references to effect library entries, not full implementations
4. **AI Integration**: Use LLM API (OpenAI, Anthropic, etc.) for complex parsing
5. **Hybrid Parsing**: Pattern-based parsing for common cases, AI for edge cases
6. **Parse-on-Import**: Parse oracle text during card import or as separate batch process

### Component Structure

```
Majik.Core/
├── CardData/
│   ├── Database/
│   │   ├── CardEntity.cs              # Existing card entity
│   │   ├── CardAbilityEntity.cs       # NEW: Stores parsed abilities
│   │   ├── EffectReferenceEntity.cs   # NEW: Effect library references
│   │   └── CardDbContext.cs           # Updated with new entities
│   └── Parsing/
│       ├── OracleTextParser.cs        # Main parser entry point
│       ├── PatternBasedParser.cs      # Pattern matching parser
│       ├── AIParser.cs                # AI-assisted parser (LLM API)
│       ├── AbilityExtractor.cs        # Extracts abilities from parsed text
│       ├── TriggerParser.cs           # Parses triggered abilities
│       ├── StaticAbilityParser.cs     # Parses static abilities
│       ├── ActivatedAbilityParser.cs  # Parses activated abilities
│       └── ReplacementEffectParser.cs # Parses replacement effects
├── Effects/
│   ├── IEffect.cs                     # Existing effect interface
│   ├── EffectLibrary.cs              # NEW: Registry of built-in effects
│   ├── Implementations/
│   │   ├── DamageEffect.cs            # "Deal X damage"
│   │   ├── LifeEffect.cs             # "Gain/lose X life"
│   │   ├── DrawEffect.cs             # "Draw X cards"
│   │   ├── TokenEffect.cs            # "Create token"
│   │   ├── CounterEffect.cs          # "Put counter on"
│   │   ├── DestroyEffect.cs          # "Destroy target"
│   │   ├── ExileEffect.cs            # "Exile target"
│   │   ├── ReturnEffect.cs           # "Return to hand/library"
│   │   ├── SearchEffect.cs           # "Search library"
│   │   ├── TapEffect.cs              # "Tap/untap"
│   │   └── ...                       # More effect types
│   └── EffectFactory.cs              # Creates effects from references
└── LayerSystem/
    ├── LayerManager.cs                # Manages layer application
    ├── ContinuousEffect.cs           # Represents a continuous effect
    ├── EffectTimestamp.cs            # Timestamp tracking
    ├── DependencyResolver.cs         # Resolves effect dependencies
    └── Layer.cs                      # Layer enumeration and utilities
```

### Database Schema

#### CardAbilityEntity

Stores parsed abilities tied to cards:

```csharp
public class CardAbilityEntity
{
    public int Id { get; set; }
    public int CardId { get; set; }  // Foreign key to CardEntity
    public CardEntity Card { get; set; } = null!;
    
    public AbilityType Type { get; set; }  // Triggered, Activated, Static, Replacement
    public int AbilityIndex { get; set; }  // Order in oracle text (0-based)
    
    // Triggered ability fields
    public string? TriggerCondition { get; set; }  // JSON: trigger condition
    public bool HasInterveningIf { get; set; }
    public string? InterveningIfCondition { get; set; }
    
    // Activated ability fields
    public string? ActivationCost { get; set; }  // JSON: cost description
    
    // Effect references (JSON array of effect reference IDs)
    public string EffectReferences { get; set; } = "[]";
    
    // Layer information (for static abilities)
    public int? Layer { get; set; }  // 1-7
    public int? Sublayer { get; set; }  // For Layer 1 and 7
    
    // Parsing metadata
    public ParsingMethod ParsingMethod { get; set; }  // Pattern, AI, Manual
    public string? ParsedText { get; set; }  // Original ability text
    public string? ParsingConfidence { get; set; }  // For AI parsing
    public DateTime ParsedAt { get; set; }
}
```

#### EffectReferenceEntity

Stores references to effect library entries:

```csharp
public class EffectReferenceEntity
{
    public int Id { get; set; }
    public string EffectId { get; set; } = "";  // Unique ID in effect library
    public EffectType Type { get; set; }  // Damage, Life, Draw, Token, etc.
    public string Name { get; set; } = "";  // Human-readable name
    public string Description { get; set; } = "";  // Effect description
    public string Parameters { get; set; } = "{}";  // JSON: parameter schema
    public bool IsBuiltIn { get; set; }  // True if in effect library
    public DateTime CreatedAt { get; set; }
}
```

#### CardAbilityEffectEntity (Join Table)

Links abilities to their effects:

```csharp
public class CardAbilityEffectEntity
{
    public int Id { get; set; }
    public int CardAbilityId { get; set; }
    public CardAbilityEntity CardAbility { get; set; } = null!;
    public int EffectReferenceId { get; set; }
    public EffectReferenceEntity EffectReference { get; set; } = null!;
    public int EffectOrder { get; set; }  // Order in ability (0-based)
    public string EffectParameters { get; set; } = "{}";  // JSON: actual parameters
}
```

## Detailed Implementation Plan

### Task 0: Database Schema and Effect Library

**Objective**: Create database schema for storing parsed abilities and build the effect library.

**Components**:

1. **Database Entities**
   - `CardAbilityEntity`: Stores parsed abilities tied to cards
   - `EffectReferenceEntity`: Registry of available effects (built-in + custom)
   - `CardAbilityEffectEntity`: Join table linking abilities to effects
   - Update `CardDbContext` with new entities and relationships

2. **Effect Library**
   - `EffectLibrary.cs`: Registry of built-in effect implementations
   - Effect implementations for common patterns:
     - `DamageEffect`: "Deal X damage to target"
     - `LifeEffect`: "Gain/lose X life"
     - `DrawEffect`: "Draw X cards"
     - `TokenEffect`: "Create [description] token"
     - `CounterEffect`: "Put X +1/+1 counters on target"
     - `DestroyEffect`: "Destroy target [type]"
     - `ExileEffect`: "Exile target [type]"
     - `ReturnEffect`: "Return target [type] to [zone]"
     - `SearchEffect`: "Search library for [type]"
     - `TapEffect`: "Tap/untap target"
     - `ModifyPTEffect`: "Gets +X/+Y" (for layer system)
     - `AddAbilityEffect`: "Gains [ability]" (for layer system)
     - And more...

3. **Effect Factory**
   - `EffectFactory.cs`: Creates effect instances from database references
   - Resolves effect IDs to implementations
   - Handles parameter binding

**Database Migration**:
```csharp
// Migration: AddAbilityTables
public class AddAbilityTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Create EffectReferenceEntity table
        migrationBuilder.CreateTable(
            name: "EffectReferences",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                EffectId = table.Column<string>(maxLength: 100, nullable: false),
                Type = table.Column<int>(nullable: false),
                Name = table.Column<string>(maxLength: 200, nullable: false),
                Description = table.Column<string>(nullable: true),
                Parameters = table.Column<string>(nullable: false, defaultValue: "{}"),
                IsBuiltIn = table.Column<bool>(nullable: false, defaultValue: true),
                CreatedAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EffectReferences", x => x.Id);
                table.UniqueConstraint("AK_EffectReferences_EffectId", x => x.EffectId);
            });

        // Create CardAbilityEntity table
        migrationBuilder.CreateTable(
            name: "CardAbilities",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                CardId = table.Column<int>(nullable: false),
                Type = table.Column<int>(nullable: false),
                AbilityIndex = table.Column<int>(nullable: false),
                TriggerCondition = table.Column<string>(nullable: true),
                HasInterveningIf = table.Column<bool>(nullable: false),
                InterveningIfCondition = table.Column<string>(nullable: true),
                ActivationCost = table.Column<string>(nullable: true),
                EffectReferences = table.Column<string>(nullable: false, defaultValue: "[]"),
                Layer = table.Column<int>(nullable: true),
                Sublayer = table.Column<int>(nullable: true),
                ParsingMethod = table.Column<int>(nullable: false),
                ParsedText = table.Column<string>(nullable: true),
                ParsingConfidence = table.Column<string>(nullable: true),
                ParsedAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CardAbilities", x => x.Id);
                table.ForeignKey(
                    name: "FK_CardAbilities_Cards_CardId",
                    column: x => x.CardId,
                    principalTable: "Cards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Create CardAbilityEffectEntity join table
        migrationBuilder.CreateTable(
            name: "CardAbilityEffects",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                CardAbilityId = table.Column<int>(nullable: false),
                EffectReferenceId = table.Column<int>(nullable: false),
                EffectOrder = table.Column<int>(nullable: false),
                EffectParameters = table.Column<string>(nullable: false, defaultValue: "{}")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CardAbilityEffects", x => x.Id);
                table.ForeignKey(
                    name: "FK_CardAbilityEffects_CardAbilities_CardAbilityId",
                    column: x => x.CardAbilityId,
                    principalTable: "CardAbilities",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CardAbilityEffects_EffectReferences_EffectReferenceId",
                    column: x => x.EffectReferenceId,
                    principalTable: "EffectReferences",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Indexes
        migrationBuilder.CreateIndex(
            name: "IX_CardAbilities_CardId",
            table: "CardAbilities",
            column: "CardId");
        migrationBuilder.CreateIndex(
            name: "IX_CardAbilityEffects_CardAbilityId",
            table: "CardAbilityEffects",
            column: "CardAbilityId");
        migrationBuilder.CreateIndex(
            name: "IX_CardAbilityEffects_EffectReferenceId",
            table: "CardAbilityEffects",
            column: "EffectReferenceId");
    }
}
```

**Effect Library Registration**:
```csharp
public class EffectLibrary
{
    private static readonly Dictionary<string, IEffect> _effects = new();
    
    static EffectLibrary()
    {
        RegisterBuiltInEffects();
    }
    
    private static void RegisterBuiltInEffects()
    {
        // Damage effects
        Register("damage_target", new DamageEffect());
        Register("damage_any", new DamageEffect());
        
        // Life effects
        Register("gain_life", new LifeEffect());
        Register("lose_life", new LifeEffect());
        
        // Draw effects
        Register("draw_cards", new DrawEffect());
        
        // Token effects
        Register("create_token", new TokenEffect());
        
        // Counter effects
        Register("put_counter", new CounterEffect());
        
        // ... more effects
    }
    
    public static void Register(string effectId, IEffect effect)
    {
        _effects[effectId] = effect;
    }
    
    public static IEffect? GetEffect(string effectId)
    {
        return _effects.TryGetValue(effectId, out var effect) ? effect : null;
    }
}
```

### Task 1: Oracle Text Parser Foundation

**Objective**: Create the foundation for parsing oracle text into structured components.

**Components**:

1. **OracleTextParser.cs**
   - Main entry point for parsing oracle text
   - Splits oracle text into sentences/paragraphs
   - Identifies ability boundaries (paragraph breaks = separate abilities per Rule 113.2c)
   - Handles special formatting (reminder text in parentheses, etc.)

2. **ParsedAbility.cs**
   - Model representing a parsed ability
   - Properties: `AbilityType`, `TriggerCondition`, `Cost`, `EffectDescription`, `Targets`, etc.
   - Supports all ability types (triggered, activated, static, replacement)

**Key Features**:
- Sentence splitting and paragraph detection
- Reminder text removal (text in parentheses)
- Keyword ability detection
- Ability type classification

**Example**:
```csharp
public class OracleTextParser
{
    public List<ParsedAbility> Parse(string oracleText)
    {
        // Split by paragraphs (each paragraph is a separate ability)
        // Remove reminder text
        // Classify ability types
        // Extract components
    }
}
```

### Task 1.5: AI-Assisted Parser

**Objective**: Integrate LLM API for parsing complex oracle text.

**Components**:

1. **AIParser.cs**
   - Interface with LLM API (OpenAI, Anthropic, etc.)
   - Sends oracle text to AI with structured prompt
   - Receives JSON response with parsed ability structure
   - Handles API errors and retries
   - Caches AI responses to avoid repeated API calls

2. **AIParsingService.cs**
   - Service for AI parsing operations
   - Manages API keys and configuration
   - Rate limiting and cost tracking
   - Batch processing support

**AI Prompt Structure**:
```json
{
  "system": "You are a Magic: The Gathering rules parser. Parse oracle text into structured abilities.",
  "user": "Parse this oracle text: '{oracle_text}'",
  "response_format": {
    "abilities": [
      {
        "type": "triggered|activated|static|replacement",
        "trigger_condition": "...",
        "cost": "...",
        "effects": [
          {
            "effect_id": "damage_target",
            "parameters": { "amount": 3, "target": "any" }
          }
        ],
        "layer": 7,
        "sublayer": 3
      }
    ]
  }
}
```

**Implementation**:
```csharp
public class AIParser
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    
    public async Task<ParsedAbility> ParseWithAI(string oracleText, CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(oracleText);
        var response = await CallLLMAPI(prompt, cancellationToken);
        return ParseAIResponse(response);
    }
    
    private string BuildPrompt(string oracleText)
    {
        return $@"Parse this Magic: The Gathering oracle text into structured abilities:
{oracleText}

Return JSON with this structure:
{{
  ""abilities"": [
    {{
      ""type"": ""triggered|activated|static|replacement"",
      ""trigger_condition"": ""..."",
      ""cost"": ""..."",
      ""effects"": [
        {{
          ""effect_id"": ""damage_target"",
          ""parameters"": {{ ""amount"": 3 }}
        }}
      ],
      ""layer"": 7,
      ""sublayer"": 3
    }}
  ]
}}";
    }
}
```

**Fallback Strategy**:
1. Try pattern-based parsing first (fast, free)
2. If pattern parsing fails or confidence is low, use AI
3. Store parsing method in database for future reference
4. Allow manual override for edge cases

### Task 2: Trigger Parser

**Objective**: Parse triggered abilities from oracle text.

**Components**:

1. **TriggerParser.cs**
   - Identifies trigger words: "Whenever", "When", "At"
   - Extracts trigger conditions
   - Parses intervening-if clauses
   - Extracts effect text

2. **TriggerCondition.cs**
   - Model for trigger conditions
   - Types: Event-based, State-based, Time-based
   - Conditions: "enters the battlefield", "becomes tapped", "deals damage", etc.

**Trigger Patterns** (Rule 603):
- "Whenever [condition], [effect]"
- "When [condition], [effect]"
- "At [time], [effect]"
- "Whenever [condition], if [condition], [effect]" (intervening-if)

**Example Oracle Texts**:
- "Whenever this creature enters the battlefield, draw a card."
- "When this creature dies, create a 2/2 black Zombie creature token."
- "At the beginning of your upkeep, you gain 1 life."

**Implementation**:
```csharp
public class TriggerParser
{
    public TriggeredAbility ParseTrigger(string abilityText)
    {
        // Identify trigger word
        // Extract trigger condition
        // Check for intervening-if
        // Extract effect
        // Create TriggeredAbility
    }
}
```

### Task 3: Static Ability Parser

**Objective**: Parse static abilities and continuous effects.

**Components**:

1. **StaticAbilityParser.cs**
   - Identifies static ability patterns
   - Extracts continuous effects
   - Determines which layer(s) the effect applies to
   - Parses characteristic-defining abilities (CDAs)

**Static Ability Patterns**:
- "Creatures you control get +1/+1" (Layer 7c)
- "This creature has flying" (Layer 6 - ability adding)
- "This creature is all colors" (Layer 5)
- "This creature's power and toughness are each equal to..." (Layer 7a - CDA)
- "This creature can't be blocked" (Layer 6 - ability adding)

**Layer Detection**:
- Analyze effect text to determine applicable layer(s)
- Some effects apply to multiple layers (e.g., type + P/T changes)

**Example**:
```csharp
public class StaticAbilityParser
{
    public StaticAbility ParseStatic(string abilityText)
    {
        // Identify static ability
        // Determine applicable layers
        // Extract effect description
        // Create StaticAbility with layer information
    }
}
```

### Task 4: Activated Ability Parser

**Objective**: Parse activated abilities with costs and effects.

**Components**:

1. **ActivatedAbilityParser.cs**
   - Identifies activated ability format: "[Cost]: [Effect]"
   - Parses costs (mana, tap, sacrifice, etc.)
   - Extracts effect text
   - Handles targeting requirements

**Activated Ability Patterns**:
- "{T}: Add {R}." (mana ability)
- "{1}, {T}, Sacrifice this artifact: Draw a card."
- "{X}{R}: This creature deals X damage to any target."

**Cost Parsing**:
- Mana costs: `{1}`, `{R}`, `{X}`
- Tap: `{T}`
- Additional costs: "Sacrifice", "Discard", etc.

### Task 5: Replacement Effect Parser

**Objective**: Parse replacement effects.

**Components**:

1. **ReplacementEffectParser.cs**
   - Identifies replacement effect keywords: "instead", "skip"
   - Extracts event being replaced
   - Parses replacement event

**Replacement Effect Patterns**:
- "If [event] would happen, [replacement] instead"
- "Skip [step/phase/turn]"
- "As [permanent] enters, [effect]"
- "Prevent [damage]"

### Task 6: Layer System Implementation

**Objective**: Implement the 7-layer system for applying continuous effects.

**Components**:

1. **Layer.cs**
   - Enumeration of layers and sublayers
   - Layer ordering utilities
   - Layer-to-effect mapping

2. **ContinuousEffect.cs**
   - Represents a continuous effect
   - Properties: `Layer`, `Sublayer`, `Timestamp`, `Source`, `Effect`, `Dependencies`
   - Methods: `AppliesTo(object)`, `Apply(object)`

3. **EffectTimestamp.cs**
   - Tracks timestamps for effects
   - Implements timestamp ordering
   - Handles simultaneous timestamps (APNAP)

4. **LayerManager.cs**
   - Manages all continuous effects
   - Applies effects in layer order
   - Handles timestamp ordering within layers
   - Resolves dependencies

5. **DependencyResolver.cs**
   - Detects dependencies between effects
   - Determines dependency order
   - Handles dependency loops

**Layer Application Algorithm**:

```csharp
public class LayerManager
{
    public void ApplyLayers(ICard card, GameState gameState)
    {
        // Start with base characteristics
        var characteristics = card.GetBaseCharacteristics();
        
        // Apply Layer 1 (copiable values)
        ApplyLayer1(characteristics, gameState);
        
        // Apply Layer 2 (control-changing)
        ApplyLayer2(characteristics, gameState);
        
        // Apply Layer 3 (text-changing)
        ApplyLayer3(characteristics, gameState);
        
        // Apply Layer 4 (type-changing)
        ApplyLayer4(characteristics, gameState);
        
        // Apply Layer 5 (color-changing)
        ApplyLayer5(characteristics, gameState);
        
        // Apply Layer 6 (ability-adding/removing)
        ApplyLayer6(characteristics, gameState);
        
        // Apply Layer 7 (P/T-changing)
        ApplyLayer7(characteristics, gameState);
        
        // Apply to card
        card.ApplyCharacteristics(characteristics);
    }
}
```

**Layer 7 Sublayers**:

```csharp
private void ApplyLayer7(Characteristics characteristics, GameState gameState)
{
    // Layer 7a: CDAs that define P/T
    ApplyLayer7a(characteristics, gameState);
    
    // Layer 7b: Set P/T to specific number
    ApplyLayer7b(characteristics, gameState);
    
    // Layer 7c: Modify P/T (counters, +X/+Y effects)
    ApplyLayer7c(characteristics, gameState);
    
    // Layer 7d: Switch P/T
    ApplyLayer7d(characteristics, gameState);
}
```

### Task 7: Timestamp System

**Objective**: Implement timestamp tracking for effect ordering.

**Components**:

1. **EffectTimestamp.cs**
   - Value object for timestamps
   - Comparison operators for ordering
   - APNAP order resolution

2. **TimestampTracker.cs**
   - Tracks timestamps for objects and effects
   - Assigns timestamps when objects enter zones
   - Updates timestamps for attachments, transformations, etc.

**Timestamp Rules** (Rule 613.7):
- Static abilities: Same timestamp as source object
- Resolved spells/abilities: Timestamp when created
- Objects: Timestamp when entering zone
- Attachments: New timestamp when attached
- Face up/down: New timestamp when flipped
- Simultaneous: APNAP order

**Implementation**:
```csharp
public class EffectTimestamp : IComparable<EffectTimestamp>
{
    public DateTime Timestamp { get; }
    public int APNAPOrder { get; } // For simultaneous timestamps
    
    public int CompareTo(EffectTimestamp other)
    {
        // Compare timestamps, then APNAP order
    }
}
```

### Task 8: Dependency Resolution

**Objective**: Handle effect dependencies within layers.

**Components**:

1. **DependencyResolver.cs**
   - Detects dependencies between effects
   - Builds dependency graph
   - Resolves dependency order
   - Handles dependency loops

**Dependency Rules** (Rule 613.8):
- Effect A depends on B if:
  - Same layer/sublayer
  - Applying B changes A's text, existence, what it applies to, or what it does
  - Neither or both are CDAs
- Dependent effects wait until dependencies are applied
- Dependency loops: Apply in timestamp order

**Algorithm**:
```csharp
public class DependencyResolver
{
    public List<ContinuousEffect> ResolveDependencies(
        List<ContinuousEffect> effects, 
        Layer layer)
    {
        // Build dependency graph
        // Topological sort
        // Handle loops
        // Return ordered list
    }
}
```

### Task 9: Database Integration and Batch Parsing

**Objective**: Integrate parsing with database and create batch parsing process.

**Components**:

1. **AbilityDatabaseService.cs**
   - Saves parsed abilities to database
   - Links abilities to effect references
   - Updates existing parsed abilities
   - Queries abilities for cards

2. **BatchParser.cs**
   - Processes all cards in database
   - Parses oracle text for cards without parsed abilities
   - Uses pattern-based parsing first, AI for failures
   - Progress tracking and resumable processing

3. **CardAbilityLoader.cs**
   - Loads parsed abilities from database for a card
   - Creates ability instances from database records
   - Resolves effect references to implementations

**Batch Parsing Process**:
```csharp
public class BatchParser
{
    public async Task ParseAllCards(CancellationToken cancellationToken = default)
    {
        var cards = await _dbContext.Cards
            .Where(c => !_dbContext.CardAbilities.Any(a => a.CardId == c.Id))
            .ToListAsync(cancellationToken);
        
        var progress = new Progress<ParseProgress>();
        progress.ProgressChanged += (s, e) => ReportProgress(e);
        
        foreach (var card in cards)
        {
            if (string.IsNullOrWhiteSpace(card.OracleText))
                continue;
                
            try
            {
                // Try pattern-based parsing first
                var parsed = await _patternParser.Parse(card.OracleText);
                
                if (parsed.Confidence < 0.7)
                {
                    // Low confidence, try AI
                    parsed = await _aiParser.ParseWithAI(card.OracleText, cancellationToken);
                }
                
                // Save to database
                await _abilityService.SaveAbilities(card.Id, parsed.Abilities);
            }
            catch (Exception ex)
            {
                // Log error, continue with next card
                _logger.LogError(ex, "Failed to parse card {CardName}", card.Name);
            }
        }
    }
}
```

### Task 10: Integration with Existing Systems

**Objective**: Connect parsed abilities from database to existing ability system.

**Components**:

1. **AbilityFactory.cs** (update existing or create new)
   - Creates `TriggeredAbility` from `CardAbilityEntity`
   - Creates `ActivatedAbility` from `CardAbilityEntity`
   - Creates `StaticAbility` from `CardAbilityEntity`
   - Creates `ReplacementEffect` from `CardAbilityEntity`
   - Resolves effect references to implementations

2. **CardFactory.cs** (update)
   - Loads card from database
   - Loads parsed abilities from database (if available)
   - Creates ability instances from database records
   - Falls back to parsing if no database records exist
   - Attaches abilities to card

3. **LayerSystemIntegration.cs**
   - Integrates layer system with `StaticAbilityManager`
   - Applies layers when game state changes
   - Updates characteristics based on layers
   - Uses layer information from database

**Integration Points**:
- `CardFactory`: Load abilities from database when creating cards
- `TriggerManager`: Register parsed triggered abilities
- `StaticAbilityManager`: Register parsed static abilities with layer info
- `ReplacementEffectManager`: Register parsed replacement effects
- Game state changes: Reapply layers
- Batch parsing: Run as separate process to populate database

**Example Card Loading**:
```csharp
public class CardFactory
{
    public async Task<ICard> CreateCardAsync(string cardName, Player owner)
    {
        // Load card data from database
        var cardEntity = await _cardProvider.GetCardByNameAsync(cardName);
        
        // Create card instance
        var card = CreateCardInstance(cardEntity);
        
        // Load parsed abilities from database
        var abilities = await _abilityLoader.LoadAbilitiesAsync(cardEntity.Id);
        
        // Create ability instances
        foreach (var abilityEntity in abilities)
        {
            var ability = _abilityFactory.CreateAbility(abilityEntity, card, owner);
            
            // Register based on type
            if (ability is ITriggeredAbility triggered)
                _triggerManager.RegisterTriggeredAbility(triggered);
            else if (ability is IStaticAbility staticAbility)
                _staticAbilityManager.RegisterStaticAbility(staticAbility);
            // ... etc
        }
        
        return card;
    }
}
```

### Task 11: Effect Execution System

**Objective**: Execute effects parsed from oracle text.

**Components**:

1. **EffectExecutor.cs**
   - Executes effect descriptions
   - Handles common effect patterns:
     - "Draw X cards"
     - "Deal X damage"
     - "Gain X life"
     - "Create token"
     - "Destroy target"
     - "Put counter on"
     - "Tap/untap"
     - "Return to hand"
     - etc.

2. **EffectPatternMatcher.cs**
   - Matches effect text to known patterns
   - Extracts parameters (X, target, etc.)
   - Creates appropriate `IEffect` implementations

**Common Effect Patterns**:
- Damage: "deals X damage to [target]"
- Life: "gain X life", "lose X life"
- Draw: "draw X card(s)"
- Token: "create [description] token"
- Counter: "put X +1/+1 counter(s) on [target]"
- Destroy: "destroy target [type]"
- Exile: "exile target [type]"
- Return: "return target [type] to [zone]"
- Search: "search your library for [type]"

### Task 12: Keyword Ability Handling

**Objective**: Handle keyword abilities from oracle text and keywords array.

**Components**:

1. **KeywordAbilityParser.cs**
   - Maps keyword names to ability implementations
   - Creates abilities for keywords
   - Handles keyword variations (e.g., "flying", "haste")

2. **KeywordRegistry.cs**
   - Registry of known keywords
   - Keyword-to-ability mapping
   - Keyword-to-layer mapping (for static keywords)

**Keywords** (from Scryfall `keywords` array):
- Static abilities: flying, trample, haste, vigilance, etc.
- Triggered abilities: prowess, landfall, etc.
- Activated abilities: cycling, etc.

**Implementation**:
```csharp
public class KeywordRegistry
{
    private static readonly Dictionary<string, KeywordInfo> Keywords = new()
    {
        { "flying", new KeywordInfo(KeywordType.Static, Layer.Six, CreateFlyingAbility) },
        { "haste", new KeywordInfo(KeywordType.Static, Layer.Six, CreateHasteAbility) },
        { "trample", new KeywordInfo(KeywordType.Static, Layer.Six, CreateTrampleAbility) },
        // ... etc
    };
}
```

### Task 13: Testing and Validation

**Objective**: Comprehensive testing of parser and layer system.

**Test Cases**:

1. **Parser Tests**:
   - Simple triggered abilities
   - Complex triggered abilities with intervening-if
   - Static abilities
   - Activated abilities
   - Replacement effects
   - Multiple abilities on one card
   - Reminder text handling

2. **Layer System Tests**:
   - Single effect in each layer
   - Multiple effects in same layer (timestamp order)
   - Effects across multiple layers
   - Dependency resolution
   - Dependency loops
   - Characteristic-defining abilities
   - P/T switching

3. **Integration Tests**:
   - Parse real card oracle text
   - Create abilities from parsed text
   - Apply layers correctly
   - Verify game state changes

**Example Test Cards**:
- Lightning Bolt: "Lightning Bolt deals 3 damage to any target."
- Grizzly Bears: "Creature — Bear" (no oracle text, just types)
- Glorious Anthem: "Creatures you control get +1/+1."
- Guttersnipe: "Whenever you cast an instant or sorcery spell, Guttersnipe deals 2 damage to each opponent."

## Implementation Phases

### Phase 1: Database and Effect Library (Week 1)
- Database schema design and migration
- Effect library implementation
- Effect factory system
- Database entities and relationships

### Phase 2: Parser Foundation (Week 1-2)
- Oracle text parser structure
- Basic sentence/paragraph splitting
- Ability type classification
- Pattern-based parser for common cases
- AI parser integration (LLM API)

### Phase 3: Ability Parsing (Week 2)
- Trigger parser (whenever/when/at)
- Static ability parser
- Activated ability parser
- Replacement effect parser
- Effect reference resolution

### Phase 4: Database Integration (Week 2-3)
- Ability database service
- Batch parsing process
- Card ability loader
- Integration with card import process

### Phase 5: Layer System (Week 3)
- Layer enumeration and utilities
- Continuous effect model
- Timestamp system
- Basic layer application

### Phase 6: Advanced Layer Features (Week 3-4)
- Dependency resolution
- Sublayer handling (Layer 1, Layer 7)
- Characteristic-defining abilities
- P/T switching

### Phase 7: Integration (Week 4)
- Ability factory integration
- Card factory updates
- Layer system integration with StaticAbilityManager
- Effect execution

### Phase 8: Testing and Refinement (Week 4-5)
- Comprehensive test suite
- Real card parsing tests (batch parse sample cards)
- AI parsing validation
- Performance optimization
- Edge case handling

## Technical Considerations

### Natural Language Processing

Oracle text is natural language, so parsing will be heuristic-based rather than perfect:

1. **Pattern Matching**: Use regex patterns for common ability structures
2. **Keyword Detection**: Identify Magic-specific keywords and phrases
3. **Fallback**: For complex/unparseable text, provide manual override or simplified interpretation
4. **Incremental**: Start with common patterns, expand over time

### Performance

- **Caching**: Cache parsed abilities for cards (don't reparse every time)
- **Lazy Parsing**: Parse abilities only when needed
- **Layer Recalculation**: Only recalculate layers when relevant effects change
- **Dependency Graph**: Cache dependency graphs when effects don't change

### Extensibility

- **Plugin System**: Allow custom parsers for special cases
- **Pattern Registry**: Extensible pattern matching system
- **Effect Library**: Extensible effect execution library

## Dependencies

### Required from Previous Phases

- ✅ Card system (Phase 4)
- ✅ Ability system (Phase 4)
- ✅ Trigger manager (Phase 4)
- ✅ Static ability manager (Phase 4)
- ✅ Replacement effect manager (Phase 4)
- ✅ Database system (Phase 7)
- ✅ Card entity with OracleText field (Phase 7)

### New Dependencies

- None (all using existing .NET libraries)

## Risks and Mitigations

### Risk 1: Oracle Text Complexity
**Risk**: Natural language parsing is inherently difficult and may not handle all cases.  
**Mitigation**: 
- Start with common patterns (80/20 rule)
- Use AI for complex cases (LLM API)
- Store parsed results in database (parse once)
- Provide fallback to manual ability creation
- Incrementally improve parser
- Use keyword array from Scryfall when available

### Risk 2: Layer System Complexity
**Risk**: Layer system is complex with many edge cases.  
**Mitigation**:
- Implement layer by layer
- Comprehensive test suite
- Reference Magic rules frequently
- Start with simple cases, add complexity incrementally

### Risk 3: Performance
**Risk**: Parsing and layer application could be slow.  
**Mitigation**:
- Cache parsed abilities
- Optimize layer application (only when needed)
- Profile and optimize hot paths

### Risk 4: Dependency Resolution
**Risk**: Dependency detection and resolution is complex.  
**Mitigation**:
- Start with simple dependency detection
- Use well-known algorithms (topological sort)
- Handle loops gracefully (fallback to timestamp)

### Risk 5: AI API Costs and Reliability
**Risk**: LLM API calls can be expensive and may have rate limits or downtime.  
**Mitigation**:
- Use pattern-based parsing for common cases (minimize AI calls)
- Cache AI responses in database
- Implement retry logic with exponential backoff
- Support multiple AI providers (fallback)
- Batch processing to optimize API usage
- Track costs and set limits

## Success Criteria

1. ✅ Database schema supports storing parsed abilities and effect references
2. ✅ Effect library contains at least 20 common effect types
3. ✅ Can parse common triggered ability patterns (pattern-based)
4. ✅ Can parse common static ability patterns (pattern-based)
5. ✅ Can parse common activated ability patterns (pattern-based)
6. ✅ AI parser can handle complex/unparseable oracle text
7. ✅ Batch parsing process can process all cards in database
8. ✅ Parsed abilities are stored in database and can be loaded
9. ✅ Layer system applies effects in correct order
10. ✅ Timestamp system orders effects correctly
11. ✅ Dependency resolution works for common cases
12. ✅ Can parse at least 70% of cards automatically (pattern + AI)
13. ✅ Integration with existing ability system works
14. ✅ Performance is acceptable (< 100ms for layer application, < 1s for card loading)
15. ✅ Comprehensive test coverage (> 80%)

## Future Enhancements (Post-Phase 7.5)

- Fine-tune AI model on Magic card oracle text for better parsing
- Support for more complex ability patterns
- Visual ability editor for manual override
- Ability template system
- Advanced dependency resolution
- Performance optimizations (caching, indexing)
- Support for modal abilities
- Support for split cards, double-faced cards
- Effect parameter validation
- Ability versioning (handle oracle text changes)
- Community-contributed effect implementations
- Effect performance profiling

## References

- **Magic Rules**: Rule 113 (Abilities), Rule 603 (Triggered Abilities), Rule 604 (Static Abilities), Rule 602 (Activated Abilities), Rule 613 (Layers), Rule 614 (Replacement Effects)
- **Scryfall Data**: Oracle text format, keywords array
- **Existing Code**: `Majik.Core/Abilities/`, `Majik.Core/Cards/`, `Majik.Core/Rules/`
