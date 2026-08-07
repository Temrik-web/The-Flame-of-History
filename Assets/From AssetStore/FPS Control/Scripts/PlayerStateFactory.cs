namespace EasyPeasyFirstPersonController
{
    public class PlayerStateFactory
    {
        FirstPersonController context;

        public PlayerStateFactory(FirstPersonController currentContext)
        {
            context = currentContext;
        }

        public PlayerBaseState Grounded() => new PlayerGroundedState(context, this);
        public PlayerBaseState Jumping() => new PlayerJumpingState(context, this);
        public PlayerBaseState Sliding() => new PlayerSlidingState(context, this);
        public PlayerBaseState Crouching() => new PlayerCrouchingState(context, this);
        public PlayerBaseState Fall() => new PlayerFallState(context, this);
        public PlayerBaseState LedgeGrab() => new PlayerLedgeGrabState(context, this);
        public PlayerBaseState Swimming() => new PlayerSwimmingState(context, this);
    }
}