namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// A monster's visual skin selection — entirely separate from MatchTypeId
    /// (gameplay identity). Nothing in gameplay code derives a MonsterColor
    /// from a MatchTypeId or vice versa: level data assigns MonsterColor to
    /// each collector explicitly, and MonsterSkinDatabase is the only place
    /// a MonsterColor resolves to actual sprites.
    /// </summary>
    public enum MonsterColor
    {
        Purple,
        Green,
        Orange,
    }
}
