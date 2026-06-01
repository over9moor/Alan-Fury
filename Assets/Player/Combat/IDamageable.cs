using UnityEngine;

public interface IDamageable
{
    void TakeDamage(float amount);
    void ApplyKnockback(Vector3 force);
}
