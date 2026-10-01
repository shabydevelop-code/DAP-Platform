namespace DAP.Core.Targets;

public sealed record Anchor(
    Locator Locator,
    AnchorRelation Relation = AnchorRelation.Context);

public enum AnchorRelation
{
    Context,
    Ancestor,
    Descendant,
    Sibling,
    Nearby
}
