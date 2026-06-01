using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Combat/Weapon Data")]
public class WeaponData : ScriptableObject
{
    public enum WeaponType { Sword, Shield, Spear, Axe, Dagger, Bow, Staff }

    [Header("íÀÛÅÅ")]
    public WeaponType type;
    public string weaponName = "íÏÑÔÕÅ";
    public Sprite icon;

    [Header("ñÏÍÌ")]
    public float damage = 10f;
    public float staggerForce = 5f;

    [Header("àÊÕÔÌÕÈ ÀÍÈ")]
    public float attackRange = 2f;
    public float attackRadius = 1f;
    public float attackHeight = 1.5f;
    public Vector3 hitboxOffset = Vector3.forward;

    [Header("äŞÊİÌÕÈ ÀÍÈ")]
    public bool isRanged;
    public bool useCharge;
    public float chargeDuration = 1f;
    public float minChargePercent = 0.3f;
    public float maxHoldTime = 3f;        // Íîâîå: ìàêñèìàëüíîå âğåìÿ óäåğæàíèÿ
    public GameObject projectilePrefab;
    public float projectileSpeed = 20f;
    public float projectileLifetime = 3f;
    public int projectilesPerShot = 1;
    public float spreadAngle = 0f;

    [Header("ğŞÈËÕÌÖÕ")]
    public float windupDuration = 0.15f;
    public float attackDuration = 0.2f;
    public float cooldownDuration = 0.3f;

    [Header("ñŞß ğÕÉÕ áÏÅÄÕÊÅÌÚ")]
    public float tickInterval = 0.1f;      // Íîâîå: èíòåğâàë ìåæäó òèêàìè óğîíà

    [Header("ÿĞŞËÕÌŞ")]
    public float staminaCost = 15f;

    [Header("ÿÊÍÕ")]
    public LayerMask targetLayers;
}
