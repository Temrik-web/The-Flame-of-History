namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerJumpingState : PlayerBaseState
    {
        public PlayerJumpingState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            HandleJump();
        }

        public override void UpdateState()
        {
            ctx.targetFov = ctx.normalFov;
            ctx.currentBobIntensity = 0;
            ctx.targetTilt = 0;

            CheckSwitchStates();
            ApplyGravity();
            HandleAirMovement();
        }
        public override void ExitState() { }

        public override void CheckSwitchStates()
        {
            if (ctx.isGrounded && ctx.moveDirection.y < 0)
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

        private void HandleJump()
        {
            ctx.moveDirection.y = ctx.jumpSpeed;
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
            
            // Allow some air control (acceleration is much lower in the air than on the ground)
            float airAccel = 5f;
            
            // Smoothly move current velocity towards target. 
            // If we slide-jumped, currentVelocity will start very high (e.g., 12m/s) and slowly drop to walkSpeed (3m/s) mid-air.
            ctx.currentVelocity = Vector3.MoveTowards(ctx.currentVelocity, targetVelocity, airAccel * Time.deltaTime);
            
            Vector3 finalMove = ctx.currentVelocity;
            // Y is handled by ApplyGravity
            finalMove.y = 0; 
            
            ctx.characterController.Move(finalMove * Time.deltaTime);
        }
    }
}