namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerGroundedState : PlayerBaseState
    {
        public PlayerGroundedState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            ctx.moveDirection.y = -2f;
        }

        public override void UpdateState()
        {
            CheckSwitchStates();

            ctx.targetCameraY = ctx.standingCameraHeight;

            bool isSprinting = ctx.input.sprint && ctx.input.moveInput.y > 0;
            float speed = isSprinting ? ctx.sprintSpeed : ctx.walkSpeed;

            ctx.targetFov = isSprinting ? ctx.sprintFov : ctx.normalFov;
            ctx.currentBobIntensity = ctx.bobAmount * (isSprinting ? 1.5f : 1f);
            ctx.currentBobSpeed = ctx.bobSpeed * (isSprinting ? 1.3f : 1f);
            ctx.targetTilt = 0;

            ctx.targetCameraY = ctx.standingCameraHeight;

            if (ctx.enableSmoothCrouch)
            {
                ctx.characterController.height = Mathf.MoveTowards(
                    ctx.characterController.height,
                    ctx.standingCharacterControllerHeight,
                    Time.deltaTime * ctx.crouchTransitionSpeed
                );

                ctx.characterController.center = Vector3.MoveTowards(
                    ctx.characterController.center,
                    ctx.standingCharacterControllerCenter,
                    Time.deltaTime * (ctx.crouchTransitionSpeed / 2f)
                );
            }
            else
            {
                ctx.characterController.height = ctx.standingCharacterControllerHeight;
                ctx.characterController.center = ctx.standingCharacterControllerCenter;
            }

            Vector2 input = ctx.input.moveInput;
            Vector3 targetMove = ctx.transform.right * input.x + ctx.transform.forward * input.y;
            
            // Normalize diagonal movement so they don't walk 40% faster diagonally
            targetMove = Vector3.ClampMagnitude(targetMove, 1f); 

            Vector3 targetVelocity = targetMove * speed;

            // Apply acceleration or deceleration based on if the player is pressing keys
            float accelRate = (input.sqrMagnitude > 0.01f) ? ctx.groundAcceleration : ctx.groundDeceleration;

            // Smoothly move current velocity towards target (gives weight and fixes the instant robotic movement)
            ctx.currentVelocity = Vector3.MoveTowards(ctx.currentVelocity, targetVelocity, accelRate * Time.deltaTime);

            Vector3 finalVelocity = ctx.currentVelocity;
            finalVelocity.y = -5f; // Keep sticking to the ground
            
            ctx.characterController.Move(finalVelocity * Time.deltaTime);
        }

        public override void ExitState() { }

        public override void CheckSwitchStates()
        {
            if (ctx.input.jump && ctx.isGrounded)
            {
                SwitchState(factory.Jumping());
            }
            else if (ctx.input.slide && ctx.input.sprint)
            {
                SwitchState(factory.Sliding());
            }
            else if (!ctx.isGrounded)
            {
                SwitchState(factory.Fall());
            }
            else if (ctx.input.crouch && ctx.isGrounded)
            {
                SwitchState(factory.Crouching());
            }
            else if (ctx.isInWater)
            {
                SwitchState(factory.Swimming());
            }

        }
    }
}