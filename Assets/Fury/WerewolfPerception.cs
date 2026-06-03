using UnityEngine;

/// <summary>
/// ≈динственный источник правды о игроке дл€ мозга оборотн€.
/// —читает дистанцию (по горизонтали), направление и смотрит ли игрок на нас.
/// Ќичего не двигает и не решает Ч только наблюдает.
/// </summary>
public class WerewolfPerception : MonoBehaviour
{
    [Header("»грок")]
    [Tooltip("≈сли не задан Ч ищетс€ по тегу при старте.")]
    public Transform player;
    public string playerTag = "Player";

    [Header(" онус взгл€да игрока")]
    [Tooltip("ѕолуугол конуса (град). ≈сли оборотень в этом конусе Ч считаетс€, что игрок смотрит на него.")]
    [Range(0f, 180f)] public float viewAngleThreshold = 35f;

    public bool HasPlayer => player != null;
    public Vector3 PlayerPos => player.position;

    void Awake()
    {
        if (player == null && !string.IsNullOrEmpty(playerTag))
        {
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go != null) player = go.transform;
        }
    }

    /// <summary>√оризонтальна€ дистанци€ до игрока (Y игнорируем).</summary>
    public float DistanceToPlayer
    {
        get
        {
            if (player == null) return Mathf.Infinity;
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }
    }

    /// <summary>√оризонтальное направление от игрока к оборотню (нормализованное).</summary>
    public Vector3 DirFromPlayerFlat
    {
        get
        {
            if (player == null) return Vector3.forward;
            Vector3 d = transform.position - player.position;
            d.y = 0f;
            return d.sqrMagnitude < 0.0001f ? Vector3.forward : d.normalized;
        }
    }

    /// <summary>—мотрит ли игрок примерно в нашу сторону.</summary>
    public bool PlayerLookingAtMe
    {
        get
        {
            if (player == null) return false;
            Vector3 fwd = player.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return false;
            fwd.Normalize();
            float angle = Vector3.Angle(fwd, DirFromPlayerFlat);
            return angle < viewAngleThreshold;
        }
    }
}