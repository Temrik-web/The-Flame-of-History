using UnityEngine;
using UnityEngine.UI;

public class WeaponUI : MonoBehaviour
{
    public Wep weapon;
    public Text ammoText;
    public Text modeText;
    public Text hintText;

    void Update()
    {
        if (weapon == null) return;

        ammoText.text = $"Патроны: {weapon.currentAmmo}/{weapon.maxAmmo}";
        if (weapon.IsReloading) ammoText.text += " (перезарядка...)";

        string mode = weapon.currentFireMode == Wep.FireMode.Auto ? "Авто" : "Одиночный";
        modeText.text = "Режим: " + mode;
        hintText.text = "R - перезарядка | V - режим огня";
    }
}