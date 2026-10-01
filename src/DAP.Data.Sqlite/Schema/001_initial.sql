PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Guides (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS GuideSteps (
    Id TEXT PRIMARY KEY,
    GuideId TEXT NOT NULL,
    StepOrder INTEGER NOT NULL,
    AdvanceMode TEXT NOT NULL,
    Runtime TEXT NULL,
    LocatorStrategy TEXT NULL,
    LocatorValue TEXT NULL,
    FrameContextJson TEXT NULL,
    ContextKind TEXT NULL,
    ContextValue TEXT NULL,
    BubbleContent TEXT NOT NULL,
    BubblePlacement TEXT NOT NULL,
    ValidationKind TEXT NULL,
    ValidationExpectedValue TEXT NULL,
    ValidationOptionsJson TEXT NULL,
    FOREIGN KEY (GuideId) REFERENCES Guides(Id) ON DELETE CASCADE,
    UNIQUE (GuideId, StepOrder)
);

CREATE TABLE IF NOT EXISTS TargetAnchors (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GuideStepId TEXT NOT NULL,
    AnchorOrder INTEGER NOT NULL,
    Relation TEXT NOT NULL,
    LocatorStrategy TEXT NOT NULL,
    LocatorValue TEXT NOT NULL,
    FOREIGN KEY (GuideStepId) REFERENCES GuideSteps(Id) ON DELETE CASCADE,
    UNIQUE (GuideStepId, AnchorOrder)
);

CREATE INDEX IF NOT EXISTS IX_GuideSteps_GuideId_StepOrder
    ON GuideSteps(GuideId, StepOrder);

CREATE INDEX IF NOT EXISTS IX_TargetAnchors_GuideStepId
    ON TargetAnchors(GuideStepId);
