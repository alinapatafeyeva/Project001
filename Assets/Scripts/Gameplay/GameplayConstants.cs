namespace Project001.Gameplay
{
    /// <summary>
    /// Global gameplay rules shared by every normal level — never per-level
    /// data, so none of these belong on LevelDefinition. A level only
    /// describes what actually varies between levels: layout, collector
    /// queues, and hunger.
    ///
    /// Future bonuses may change the effective runtime value derived from a
    /// base constant here (e.g. effective conveyor capacity =
    /// BaseConveyorCapacity + runtime bonus modifier) without ever changing
    /// the base constant itself. No such modifier, service, or bonus logic
    /// exists yet.
    ///
    /// BaseConveyorMoveSpeed is the unmodified conveyor speed, separate from
    /// any future gameplay speed multiplier — effective speed will eventually
    /// be BaseConveyorMoveSpeed × GameplaySpeedMultiplier, with that
    /// multiplier shared by conveyor movement, animations, effects, and
    /// timers rather than implemented only inside ConveyorSystem. That
    /// multiplier does not exist yet either.
    /// </summary>
    public static class GameplayConstants
    {
        public const int WaitingLineCapacity = 5;
        public const int BaseConveyorCapacity = 5;
        public const float BaseConveyorMoveSpeed = 3.5f;
    }
}
