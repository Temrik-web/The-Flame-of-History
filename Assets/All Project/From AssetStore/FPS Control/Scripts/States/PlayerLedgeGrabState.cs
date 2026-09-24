namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerLedgeGrabState : PlayerBaseState
    {
        private Vector3 startPosition;
        private Vector3 targetPosition;
        private float climbTimer;
        private float hangTimer;
        private bool isClimbing;
        private Vector3 grabForward;

        public PlayerLedgeGrabState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            ctx.CheckLedge(out targetPosition);
            startPosition = ctx.transform.position;
            ctx.moveDirection = Vector3.zero;
            isClimbing = false;
            climbTimer = 0;
            hangTimer = 0;
            
            grabForward = ctx.transform.forward;
            grabForward.y = 0;
            grabForward.Normalize();
        }

        public override void UpdateState()
        {
            if (!isClimbing)
            {
                hangTimer += Time.deltaTime;

                if (ctx.useClimbTilt)
                {
                    float impact = Mathf.Exp(-hangTimer * 10f);
                    float wobble = Mathf.Sin(hangTimer * 2f) * 0.04f;
                    
                    ctx.targetCameraY = (ctx.standingCameraHeight - 0.25f) - (impact * 0.2f) + wobble;
                    ctx.targetTilt = Mathf.Sin(hangTimer * 1.5f) * 1.2f;
                    ctx.targetFov = ctx.normalFov + (impact * 3f);
                }
                
                if (ctx.input.jump || ctx.input.moveInput.y > 0)
                {
                    Vector3 currentForward = ctx.transform.forward;
                    currentForward.y = 0;
                    currentForward.Normalize();

                    if (Vector3.Angle(grabForward, currentForward) > 60f)
                    {
                        ctx.currentLedgeCooldown = 0.5f;
                        SwitchState(factory.Jumping());
                        return;
                    }
                    else
                    {
                        isClimbing = true;
                        ctx.characterController.enabled = false;
                    }
                }
                
                if (ctx.input.crouch) 
                {
                    ctx.currentLedgeCooldown = 0.5f;
                    SwitchState(factory.Fall());
                }
            }
            else
            {
                HandleOrganicClimb();
            }
        }

        public override void ExitState()
        {
            if (!ctx.characterController.enabled)
                ctx.characterController.enabled = true;
        }

        public override void CheckSwitchStates() { }

        private void HandleOrganicClimb()
        {
            climbTimer += Time.deltaTime;
            float linearT = Mathf.Clamp01(climbTimer / ctx.climbDuration);

            float easedT = linearT * linearT * linearT * (linearT * (6f * linearT - 15f) + 10f);

            float heightArc = Mathf.Sin(linearT * Mathf.PI) * ctx.climbHeightArc;

            Vector3 targetHorizontal = new Vector3(targetPosition.x, startPosition.y, targetPosition.z);
            Vector3 currentPos = Vector3.Lerp(startPosition, targetHorizontal, easedT);

            float currentHeight = Mathf.Lerp(startPosition.y, targetPosition.y, easedT);

            ctx.transform.position = new Vector3(currentPos.x, currentHeight + heightArc, currentPos.z);

            if (ctx.useClimbTilt)
            {
                ctx.targetTilt = Mathf.Sin(linearT * Mathf.PI) * ctx.climbTiltAmount;
                ctx.targetCameraY = ctx.standingCameraHeight + (Mathf.Sin(linearT * Mathf.PI) * 0.15f);
            }

            if (linearT >= 1f)
            {
                ctx.transform.position = targetPosition;
                SwitchState(factory.Grounded());
            }
        }
    }
}