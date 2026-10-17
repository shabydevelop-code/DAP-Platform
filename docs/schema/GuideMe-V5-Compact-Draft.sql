-- GuideMe V5 compact schema CANDIDATE (7 tables).
-- Design-only artifact: NOT the active production schema and NOT a migration script.
-- Do not execute against an existing GuideMe-V5.db.
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
    Runtime TEXT,
    LocatorsJson TEXT NOT NULL
        CHECK (json_valid(LocatorsJson) AND json_type(LocatorsJson)='array'
            AND json_array_length(LocatorsJson)>0),
    FrameContextJson TEXT,
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
