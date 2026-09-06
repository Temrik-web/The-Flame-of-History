namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerFallState : PlayerBaseState
    {
        public PlayerFallState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            ctx.targetFov = ctx.normalFov;
            ctx.currentBobIntensity = 0;
            ctx.targetTilt = 0;
        }

        public override void UpdateState()
        {
            ApplyGravity();
            HandleAirMovement();
            CheckSwitchStates();
        }

        public override void ExitState() { }

        public override void CheckSwitchStates()
        {
            if (ctx.isGrounded && ctx.moveDirection.y <= 0)
            {
                SwitchState(factory.Grounded());
            }
            else if (ctx.CheckLedge(out _))
            {
                SwitchState(factory.LedgeGrab());
            }
            else if (ctx.isInWater)
            {
                SwitchState(factory.Swimming());
            }

        }

        private void ApplyGravity()
        {
            ctx.moveDirection.y -= ctx.gravity * Time.deltaTime;
            ctx.characterController.Move(new Vector3(0, ctx.moveDirection.y, 0) * Time.deltaTime);
        }

        private void HandleAirMovement()
        {
            Vector2 input = ctx.input.moveInput;
            Vector3 targetMove = ctx.transform.right * input.x + ctx.transform.forward * input.y;
            targetMove = Vector3.ClampMagnitude(targetMove, 1f);
            
            Vector3 targetVelocity = targetMove * ctx.walkSpeed;
            float airAccel = 5f;
            
            ctx.currentVelocity = Vector3.MoveTowards(ctx.currentVelocity, targetVelocity, airAccel * Time.deltaTime);
            
            Vector3 finalMove = ctx.currentVelocity;
            finalMove.y = 0; 
            
            ctx.characterController.Move(finalMove * Time.deltaTime);
        }
    }
}