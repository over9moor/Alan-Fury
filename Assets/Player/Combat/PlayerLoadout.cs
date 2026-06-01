using UnityEngine;

public class PlayerLoadout : MonoBehaviour
{
    [Header("Слоты оружия")]
    public WeaponData rightHandWeapon;
    public WeaponData leftHandWeapon;

    public WeaponData GetMainWeapon() => rightHandWeapon;
    public WeaponData GetOffhandWeapon() => leftHandWeapon;
    public bool HasShield() => leftHandWeapon != null && leftHandWeapon.type == WeaponData.WeaponType.Shield;
}
