using UnityEngine;

namespace EasyPeasyFirstPersonController
{
    public interface IInputManager
    {
        Vector2 moveInput { get; }
        Vector2 lookInput { get; }
        bool jump { get; }
        bool sprint { get; }
        bool crouch { get; }
        bool slide { get; }
    }
}