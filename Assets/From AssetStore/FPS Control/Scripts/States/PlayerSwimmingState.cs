namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerSwimmingState : PlayerBaseState
    {
        public PlayerSwimmingState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            ctx.targetFov = ctx.normalFov - 5f;
            ctx.currentBobIntensity = ctx.bobAmount * 0.5f;
            ctx.currentBobSpeed = ctx.bobSpeed * 0.5f;
        }

        public override void UpdateState()
        {
            HandleSwimMovement();
            CheckSwitchStates();

            ctx.targetCameraY = ctx.standingCameraHeight;
        }

        public override void ExitState() { }

        public override void CheckSwitchStates()
        {
            if (!ctx.isInWater)
            {
                if (ctx.isGrounded)
                {
                    SwitchState(factory.Grounded());
                }
                else
                {
                    SwitchState(factory.Fall());
                }
            }
        }

        private void HandleSwimMovement()
        {
            float speed = ctx.input.sprint ? ctx.swimSprintSpeed : ctx.swimSpeed;

            Vector2 input = ctx.input.moveInput;
            Vector3 horizontalMove = ctx.transform.right * input.x + ctx.transform.forward * input.y;

            float verticalInput = 0;
            if (ctx.input.jump) verticalInput = 1f;
            else if (ctx.input.crouch) verticalInput = -1f;

            Vector3 targetVelocity = (horizontalMove + Vector3.up * verticalInput).normalized * speed;

            ctx.moveDirection = Vector3.Lerp(ctx.moveDirection, targetVelocity, Time.deltaTime * ctx.waterDrag);

            ctx.characterController.Move(ctx.moveDirection * Time.deltaTime);
        }
    }
}