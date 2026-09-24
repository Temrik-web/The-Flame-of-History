namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerSlidingState : PlayerBaseState
    {
        private float slideTimer;
        private Vector3 slideDirection;
        public PlayerSlidingState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            slideTimer = ctx.slideDuration;

            if (!ctx.enableSmoothCrouch)
            {
                float crouchHeight = ctx.crouchingCharacterControllerHeight;
                ctx.characterController.height = crouchHeight;
                ctx.characterController.center = new Vector3(0, crouchHeight / 2f, 0);
            }

            slideDirection = ctx.transform.forward;
        }

        public override void UpdateState()
        {
            if (ctx.enableSlopeSliding)
            {
                HandleSlopeFriction();
            }
            else
            {
                slideTimer -= Time.deltaTime;
            }

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

            float progress = Mathf.Clamp01(slideTimer / ctx.slideDuration);

            ctx.targetFov = ctx.sprintFov + (ctx.slideFovBoost * progress);
            ctx.currentBobIntensity = 0;
            ctx.targetTilt = -5f * progress;
            ctx.targetCameraY = ctx.crouchingCameraHeight;

            HandleSlideMovement(progress);
            CheckSwitchStates();
        }

        private void HandleSlopeFriction()
        {
            if (Physics.Raycast(ctx.transform.position, Vector3.down, out RaycastHit hit, 2f, ctx.groundMask))
            {
                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
                Vector3 projectedSlideDir = Vector3.ProjectOnPlane(slideDirection, hit.normal).normalized;

                if (slopeAngle > 5f && projectedSlideDir.y < -0.05f)
                {
                    slideTimer += Time.deltaTime;
                    slideTimer = Mathf.Min(slideTimer, ctx.slideDuration);
                }
                else if (slopeAngle > 5f && projectedSlideDir.y > 0.05f)
                {
                    slideTimer -= Time.deltaTime * ctx.slideUphillFriction;
                }
                else
                {
                    slideTimer -= Time.deltaTime;
                }
                
                slideDirection = projectedSlideDir;
            }
            else
            {
                slideTimer -= Time.deltaTime;
            }
        }

        public override void ExitState() { }

        public override void CheckSwitchStates()
        {
            if (ctx.input.jump && ctx.isGrounded && !ctx.HasCeiling())
            {
                SwitchState(factory.Jumping());
                return;
            }

            if (slideTimer <= 0 || !ctx.isGrounded)
            {
                if (ctx.HasCeiling() || ctx.input.crouch)
                {
                    SwitchState(factory.Crouching());
                }
                else
                {
                    SwitchState(factory.Grounded());
                }
            }
        }

        private void HandleSlideMovement(float progress)
        {
            float speedCurve = Mathf.Pow(progress, 0.5f);
            float speed = ctx.slideSpeed * Mathf.Lerp(0.5f, 1f, speedCurve);

            float strafeInput = ctx.input.moveInput.x;
            Vector3 steerVector = ctx.transform.right * strafeInput * ctx.slideSteerControl;
            
            Vector3 finalMove = (slideDirection * speed) + steerVector;
            
            ctx.currentVelocity = finalMove;

            ctx.characterController.Move(finalMove * Time.deltaTime);

            Vector3 actualVelocity = ctx.characterController.velocity;
            actualVelocity.y = 0;
            
            float intendedSpeed = finalMove.magnitude;
            float actualSpeed = actualVelocity.magnitude;

            if (intendedSpeed > 4f && actualSpeed < intendedSpeed * 0.2f)
            {
                Vector3 crashVector = finalMove - actualVelocity;
                Vector3 localCrashDirection = ctx.transform.InverseTransformDirection(crashVector);

                ctx.TriggerCameraShake(0.15f, 0.4f, localCrashDirection);
                
                SwitchState(factory.Crouching());
                return;
            }

            if (ctx.isGrounded) ctx.moveDirection.y = -20f;
            else ctx.moveDirection.y = 0;
            ctx.characterController.Move(new Vector3(0, ctx.moveDirection.y, 0) * Time.deltaTime);
        }
    }
}