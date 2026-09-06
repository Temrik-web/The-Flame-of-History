namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerCrouchingState : PlayerBaseState
    {
        public PlayerCrouchingState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            if (!ctx.enableSmoothCrouch)
            {
                float crouchHeight = ctx.crouchingCharacterControllerHeight;
                ctx.characterController.height = crouchHeight;
                ctx.characterController.center = new Vector3(0, crouchHeight / 2f, 0);
            }
        }

        public override void UpdateState()
        {
            CheckSwitchStates();
            HandleCrouchMovement();

            if (ctx.enableSmoothCrouch)
            {
                ctx.characterController.height = Mathf.MoveTowards(
                    ctx.characterController.height,
                    ctx.crouchingCharacterControllerHeight,
                    Time.deltaTime * ctx.crouchTransitionSpeed
                );

                ctx.characterController.center = Vector3.MoveTowards(
                    ctx.characterController.center,
                    new Vector3(0, ctx.crouchingCharacterControllerHeight / 2f, 0),
                    Time.deltaTime * (ctx.crouchTransitionSpeed / 2f)
                );
            }

            ctx.targetCameraY = ctx.crouchingCameraHeight;
        }

        public override void ExitState()
        {

        }

        public override void CheckSwitchStates()
        {
            if (!ctx.input.crouch && !ctx.HasCeiling())
            {
                SwitchState(factory.Grounded());
            }
            else if (!ctx.isGrounded)
            {
                SwitchState(factory.Fall());
            }
        }

        private void HandleCrouchMovement()
        {
            Vector2 input = ctx.input.moveInput;
            Vector3 targetMove = ctx.transform.right * input.x + ctx.transform.forward * input.y;
            targetMove = Vector3.ClampMagnitude(targetMove, 1f);

            Vector3 targetVelocity = targetMove * ctx.crouchSpeed;
            float accelRate = (input.sqrMagnitude > 0.01f) ? ctx.groundAcceleration : ctx.groundDeceleration;

            ctx.currentVelocity = Vector3.MoveTowards(ctx.currentVelocity, targetVelocity, accelRate * Time.deltaTime);

            Vector3 finalVelocity = ctx.currentVelocity;
            
            if (ctx.isGrounded) finalVelocity.y = -10f;
            else finalVelocity.y = 0f;
            
            ctx.characterController.Move(finalVelocity * Time.deltaTime);
        }
    }
}