using UnityEngine;

/// <summary>
/// Добоевое поведение альфа-оборотня (сталкинг). Отдельно от боевого WerewolfBrain.
///
/// Реализовано сейчас:
///  - Patrol      — бродит у логова; пока игрок далеко, есть шанс не заметить (стелс игрока);
///  - Stalk       — крадётся за спину, когда его НЕ видят; ЗАМИРАЕТ, когда видят;
///                  если игрок шагнул ближе во время замирания — понимает, что обнаружен;
///  - Reposition  — рывком (скачки!) уходит далеко за спину игрока, вне обзора, потом снова крадётся.
///
/// Швы на будущее: OpenPursuit, Encircle, Attack — пока заглушки в enum.
///
/// ВАЖНО: пока работает сталкинг, держи WerewolfBrain выключенным (оба зовут MoveTo).
/// Скорости подбирай относительно локомоушна: stalkSpeed НИЖЕ boundEnterSpeed (без скачков),
/// fleeSpeed ВЫШЕ boundEnterSpeed (со скачками).
/// </summary>
[RequireComponent(typeof(WerewolfPerception))]
[RequireComponent(typeof(WerewolfLocomotion))]
public class AlphaStalker : MonoBehaviour
{
    public enum AlphaState { Patrol, Stalk, Reposition /*, OpenPursuit, Encircle, Attack — позже*/ }

    [Header("Ссылки")]
    public WerewolfPerception perception;
    public WerewolfLocomotion locomotion;

    [Header("Дистанции (м)")]
    [Tooltip("Ближе этого альфа переходит из патруля в сталкинг.")]
    public float noticeRange = 30f;
    [Tooltip("Дальше этого теряет интерес и возвращается в патруль.")]
    public float loseInterestRange = 45f;
    [Tooltip("На какой дистанции пасти игрока (точка за спиной).")]
    public float preferredStalkDistance = 16f;
    [Tooltip("Если игрок при взгляде окажется ближе этого — альфа уходит.")]
    public float spookRadius = 8f;
    [Tooltip("Насколько игрок должен сократить дистанцию во время замирания, чтобы спугнуть (м).")]
    public float spookApproachMargin = 3f;

    [Header("Скорости (м/с)")]
    [Tooltip("Тихий подкрад. Держи НИЖЕ boundEnterSpeed локомоушна, чтобы не было скачков.")]
    public float stalkSpeed = 4f;
    [Tooltip("Рывок-уход. ВЫШЕ boundEnterSpeed → срывается в скачки.")]
    public float fleeSpeed = 12f;
    public float patrolSpeed = 2.5f;

    [Header("Замирание")]
    [Tooltip("Поворачиваться к игроку при замирании. Классика (Weeping Angel) — нет, стоит истуканом.")]
    public bool faceWhileFrozen = false;

    [Header("Уход за спину")]
    public float repositionDistance = 28f;
    [Tooltip("Разброс точки ухода в сторону от строгой спины (град).")]
    public float repositionSpread = 35f;

    [Header("Патруль")]
    public float patrolRadius = 20f;

    [Header("Давление сталкинга (задел под вой/призыв)")]
    [Tooltip("Копится, пока альфа близко и незамечена. Сейчас геймплейно ни на что не влияет.")]
    public float pressurePerSecond = 0.15f;
    [Tooltip("Автоматически дёргать onReadyToCall при заполнении. Обычно триггер внешний (святилище).")]
    public bool autoCallWhenPressured = false;

    /// <summary>Сигнал «альфа готова звать стаю». Подключай свой переход к фазе воя.</summary>
    public System.Action onReadyToCall;

    public AlphaState State => _state;
    public float StalkPressure01 => Mathf.Clamp01(_pressure);

    private AlphaState _state = AlphaState.Patrol;
    private Vector3 _home;
    private Vector3 _patrolTarget;
    private Vector3 _repoTarget;
    private bool _frozen;
    private float _freezeStartDist;
    private float _pressure;

    void Start()
    {
        if (perception == null) perception = GetComponent<WerewolfPerception>();
        if (locomotion == null) locomotion = GetComponent<WerewolfLocomotion>();
        _home = transform.position;
        _patrolTarget = PickPatrolPoint();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (perception == null || !perception.HasPlayer)
        {
            TickPatrol(dt, Mathf.Infinity);
            return;
        }

        float dist = perception.DistanceToPlayer;
        bool seen = perception.IsSeenByPlayer();

        switch (_state)
        {
            case AlphaState.Patrol: TickPatrol(dt, dist); break;
            case AlphaState.Stalk: TickStalk(dt, dist, seen); break;
            case AlphaState.Reposition: TickReposition(dt, dist, seen); break;
        }
    }

    // =================== Patrol ===================

    private void TickPatrol(float dt, float dist)
    {
        if (dist < noticeRange) { _state = AlphaState.Stalk; _frozen = false; return; }

        if (locomotion.MoveTo(_patrolTarget, patrolSpeed, dt))
            _patrolTarget = PickPatrolPoint();
        locomotion.FaceTowards(_patrolTarget, dt);
    }

    private Vector3 PickPatrolPoint()
    {
        Vector2 r = Random.insideUnitCircle * patrolRadius;
        return _home + new Vector3(r.x, 0f, r.y);
    }

    // =================== Stalk ===================

    private void TickStalk(float dt, float dist, bool seen)
    {
        if (dist > loseInterestRange)
        {
            _state = AlphaState.Patrol;
            _patrolTarget = PickPatrolPoint();
            return;
        }

        if (seen)
        {
            // Замираем. Не зовём MoveTo → локомоушн тормозит до нуля.
            if (!_frozen) { _frozen = true; _freezeStartDist = dist; }
            if (faceWhileFrozen) locomotion.FaceTowards(perception.PlayerPos, dt);

            // Игрок шагнул ближе → понял, что обнаружен → уходит за спину.
            bool closingIn = dist <= spookRadius || dist <= _freezeStartDist - spookApproachMargin;
            if (closingIn) EnterReposition();
        }
        else
        {
            _frozen = false;

            // Крадётся к точке за спиной игрока.
            Vector3 slot = ComputeStalkSlot();
            locomotion.MoveTo(slot, stalkSpeed, dt);
            locomotion.FaceTowards(perception.PlayerPos, dt);

            // Копим давление, пока близко и незамечен.
            if (dist <= preferredStalkDistance + 2f)
            {
                _pressure += pressurePerSecond * dt;
                if (autoCallWhenPressured && _pressure >= 1f) onReadyToCall?.Invoke();
            }
        }
    }

    // Точка сталкинга: за спиной игрока на preferredStalkDistance.
    private Vector3 ComputeStalkSlot()
    {
        Vector3 behind = -perception.PlayerForwardFlat;
        return perception.PlayerPos + behind * preferredStalkDistance;
    }

    // =================== Reposition (уход за спину) ===================

    private void EnterReposition()
    {
        _state = AlphaState.Reposition;
        _frozen = false;
        _repoTarget = ComputeBehindPoint();
    }

    private void TickReposition(float dt, float dist, bool seen)
    {
        locomotion.MoveTo(_repoTarget, fleeSpeed, dt);   // высокая скорость → скачки
        locomotion.FaceTowards(_repoTarget, dt);

        // Снова попал в обзор по пути — продолжаем уходить, перевыбираем точку дальше за спину.
        if (seen) _repoTarget = ComputeBehindPoint();

        // Оторвался и вне обзора — возвращаемся к скрытному подкраду.
        if (!seen && dist >= preferredStalkDistance)
        {
            _state = AlphaState.Stalk;
            _frozen = false;
        }
    }

    private Vector3 ComputeBehindPoint()
    {
        Vector3 behind = -perception.PlayerForwardFlat;
        float j = Random.Range(-repositionSpread, repositionSpread) * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(
            behind.x * Mathf.Cos(j) - behind.z * Mathf.Sin(j),
            0f,
            behind.x * Mathf.Sin(j) + behind.z * Mathf.Cos(j));
        return perception.PlayerPos + dir * repositionDistance;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (perception == null || !perception.HasPlayer) return;
        Vector3 p = perception.PlayerPos;

        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.7f); Gizmos.DrawWireSphere(p, spookRadius);
        Gizmos.color = new Color(0.9f, 0.8f, 0.2f, 0.6f); Gizmos.DrawWireSphere(p, preferredStalkDistance);
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.4f); Gizmos.DrawWireSphere(p, noticeRange);

        if (Application.isPlaying)
        {
            Vector3 t = _state == AlphaState.Reposition ? _repoTarget
                      : _state == AlphaState.Stalk ? ComputeStalkSlot()
                      : _patrolTarget;
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, t);
            Gizmos.DrawWireCube(t, Vector3.one * 0.6f);
        }
    }
#endif
}
