namespace EasyPeasyFirstPersonController
{
    public abstract class PlayerBaseState
    {
        protected FirstPersonController ctx;
        protected PlayerStateFactory factory;

        public PlayerBaseState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
        {
            ctx = currentContext;
            factory = playerStateFactory;
        }

        public abstract void EnterState();
        public abstract void UpdateState();
        public abstract void ExitState();
        public abstract void CheckSwitchStates();

        protected void SwitchState(PlayerBaseState newState)
        {
            ExitState();
            newState.EnterState();
            ctx.CurrentState = newState;
        }
    }
}