namespace EasyPeasyFirstPersonController
{
    using UnityEngine;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
#endif

    public class InputManager : MonoBehaviour, IInputManager
    {
        public Vector2 moveInput
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current == null) return Vector2.zero;
                Vector2 input = Vector2.zero;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1;
                return input.normalized;
#else
                return new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
#endif
            }
        }

        public Vector2 lookInput
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current == null) return Vector2.zero;
                // Multiply by a small delta factor to match the feeling of the old Input.GetAxis
                return Mouse.current.delta.ReadValue() * 0.05f; 
#else
                return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
            }
        }

        public bool jump
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
#else
                return Input.GetKey(KeyCode.Space);
#endif
            }
        }

        public bool sprint
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
                return Input.GetKey(KeyCode.LeftShift);
#endif
            }
        }

        public bool crouch
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
#endif
            }
        }

        public bool slide
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
#endif
            }
        }
    }
}