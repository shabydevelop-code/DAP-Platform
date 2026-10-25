-- GuideMe V5 official schema (9 tables).
-- Use for provisioning a NEW empty V5 database only.
-- Never run this schema against an existing database; use the migration script.
-- JSON array ordering must preserve MatcherOrder and LocatorId ordering from V5.
-- StepRules combines signals and captures; RuleType disambiguates their identifiers.
PRAGMA foreign_keys=ON;

CREATE TABLE Guides (
    GuideId INTEGER PRIMARY KEY,
    GuideKey TEXT NOT NULL UNIQUE,
    GuideName TEXT NOT NULL
);

CREATE TABLE ApplicationContexts (
    GuideId INTEGER NOT NULL,
    ContextId TEXT NOT NULL,
    Runtime TEXT NOT NULL CHECK (Runtime IN ('Web','Windows')),
    DisplayName TEXT,
    MatchersJson TEXT NOT NULL DEFAULT '[]'
        CHECK (json_valid(MatchersJson) AND json_type(MatchersJson)='array'),
    PRIMARY KEY (GuideId, ContextId),
    FOREIGN KEY (GuideId) REFERENCES Guides(GuideId)
);

CREATE TABLE Steps (
    GuideId INTEGER NOT NULL,
    StepId TEXT NOT NULL,
    StepOrder INTEGER NOT NULL,
    IsEnabled INTEGER NOT NULL CHECK (IsEnabled IN (0,1)),
    ApplicationContextId TEXT,
    ContextKind TEXT,
    ContextValue TEXT,
    BubbleContent TEXT NOT NULL,
    BubblePlacement TEXT NOT NULL,
    PRIMARY KEY (GuideId, StepId),
    UNIQUE (GuideId, StepOrder),
    FOREIGN KEY (GuideId) REFERENCES Guides(GuideId),
    FOREIGN KEY (GuideId, ApplicationContextId)
        REFERENCES ApplicationContexts(GuideId, ContextId),
    CHECK ((ContextKind IS NULL AND ContextValue IS NULL)
        OR (ContextKind IS NOT NULL AND ContextValue IS NOT NULL))
);

CREATE TABLE Elements (
    GuideId INTEGER NOT NULL,
    ElementId TEXT NOT NULL,
    Runtime TEXT CHECK (Runtime IS NULL OR Runtime IN ('Web','Windows')),
    LocatorsJson TEXT NOT NULL
        CHECK (json_valid(LocatorsJson) AND json_type(LocatorsJson)='array'
            AND json_array_length(LocatorsJson)>0),
    FrameContextJson TEXT,
    DisplayName TEXT,
    PRIMARY KEY (GuideId, ElementId),
    FOREIGN KEY (GuideId) REFERENCES Guides(GuideId)
);

CREATE TABLE ElementAnchors (
    GuideId INTEGER NOT NULL,
    ElementId TEXT NOT NULL,
    AnchorElementId TEXT NOT NULL,
    Relation TEXT NOT NULL,
    AnchorOrder INTEGER NOT NULL,
    PRIMARY KEY (GuideId, ElementId, AnchorOrder),
    FOREIGN KEY (GuideId, ElementId)
        REFERENCES Elements(GuideId, ElementId),
    FOREIGN KEY (GuideId, AnchorElementId)
        REFERENCES Elements(GuideId, ElementId)
);

CREATE TABLE StepTargets (
    GuideId INTEGER NOT NULL,
    StepId TEXT NOT NULL,
    UsageId TEXT NOT NULL,
    ElementId TEXT NOT NULL,
    Purpose TEXT NOT NULL,
    PRIMARY KEY (GuideId, StepId, UsageId),
    FOREIGN KEY (GuideId, StepId) REFERENCES Steps(GuideId, StepId),
    FOREIGN KEY (GuideId, ElementId) REFERENCES Elements(GuideId, ElementId)
);

CREATE TABLE StepRules (
    GuideId INTEGER NOT NULL,
    StepId TEXT NOT NULL,
    RuleType TEXT NOT NULL CHECK (RuleType IN
        ('PrimaryValidation','AdditionalCompletionCondition','Capture')),
    RuleId TEXT NOT NULL,
    Kind TEXT,
    TargetUsageId TEXT,
    ExpectedValue TEXT,
    OptionsJson TEXT,
    Property TEXT,
    Pattern TEXT,
    Timing TEXT,
    PRIMARY KEY (GuideId, StepId, RuleType, RuleId),
    FOREIGN KEY (GuideId, StepId) REFERENCES Steps(GuideId, StepId),
    FOREIGN KEY (GuideId, StepId, TargetUsageId)
        REFERENCES StepTargets(GuideId, StepId, UsageId),
    CHECK (
        (RuleType='Capture' AND Property IS NOT NULL AND TargetUsageId IS NOT NULL
            AND Kind IS NULL AND ExpectedValue IS NULL AND OptionsJson IS NULL)
        OR
        (RuleType!='Capture' AND Kind IS NOT NULL AND Property IS NULL AND Pattern IS NULL AND Timing IS NULL)
    )
);

-- Guide-level variable declarations. Values captured during execution are never persisted here.
CREATE TABLE GuideVariables (
    GuideId INTEGER NOT NULL,
    VariableId TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    InitialValue TEXT,
    PRIMARY KEY (GuideId, VariableId),
    UNIQUE (GuideId, DisplayName),
    FOREIGN KEY (GuideId) REFERENCES Guides(GuideId)
);

-- Ordered variable assignments belong to steps. A target read references the
-- same StepTargets catalog that is used by Web and Windows element picking.
CREATE TABLE StepVariableActions (
    GuideId INTEGER NOT NULL,
    StepId TEXT NOT NULL,
    ActionId TEXT NOT NULL,
    ActionOrder INTEGER NOT NULL,
    VariableId TEXT NOT NULL,
    ActionKind TEXT NOT NULL CHECK (ActionKind IN
        ('SetValue','ReadFromTarget','Clear','CopyFromVariable')),
    LiteralValue TEXT,
    SourceVariableId TEXT,
    TargetUsageId TEXT,
    Property TEXT,
    Pattern TEXT,
    Timing TEXT NOT NULL DEFAULT 'AfterAction'
        CHECK (Timing IN ('BeforeAction','DuringStep','AfterAction')),
    PRIMARY KEY (GuideId, StepId, ActionId),
    UNIQUE (GuideId, StepId, ActionOrder),
    FOREIGN KEY (GuideId, StepId) REFERENCES Steps(GuideId, StepId),
    FOREIGN KEY (GuideId, VariableId) REFERENCES GuideVariables(GuideId, VariableId),
    FOREIGN KEY (GuideId, SourceVariableId)
        REFERENCES GuideVariables(GuideId, VariableId),
    FOREIGN KEY (GuideId, StepId, TargetUsageId)
        REFERENCES StepTargets(GuideId, StepId, UsageId),
    CHECK (
      (ActionKind='SetValue' AND LiteralValue IS NOT NULL AND
          SourceVariableId IS NULL AND TargetUsageId IS NULL AND Property IS NULL AND Pattern IS NULL)
      OR
      (ActionKind='ReadFromTarget' AND LiteralValue IS NULL AND
          SourceVariableId IS NULL AND TargetUsageId IS NOT NULL AND Property IS NOT NULL)
      OR
      (ActionKind='CopyFromVariable' AND LiteralValue IS NULL AND
          SourceVariableId IS NOT NULL AND TargetUsageId IS NULL AND Property IS NULL AND Pattern IS NULL)
      OR
      (ActionKind='Clear' AND LiteralValue IS NULL AND
          SourceVariableId IS NULL AND TargetUsageId IS NULL AND Property IS NULL AND Pattern IS NULL)
    )
);
