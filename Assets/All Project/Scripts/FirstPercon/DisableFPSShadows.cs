using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Убирает тени от тела и экипировки FPS-персонажа.</summary>
[DisallowMultipleComponent]
public sealed class DisableFPSShadows : MonoBehaviour
{
    // Учитывает даже неактивные модели оружия внутри персонажа.
    private void OnEnable()
    {
        DisableChildShadows();
    }

    // Повторяет настройку после инициализации остальных компонентов сцены.
    private void Start()
    {
        DisableChildShadows();
    }

    private void DisableChildShadows()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            renderer.shadowCastingMode = ShadowCastingMode.Off;
    }
}
