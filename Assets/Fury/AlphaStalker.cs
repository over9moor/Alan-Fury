using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Добоевое поведение альфа-оборотня (сталкинг). Отдельно от боевого WerewolfBrain.
///
/// Три зоны по горизонтальной дистанции до игрока:
///  - dist &lt; minDistance         → РАЗРЫВ ДИСТАНЦИИ: быстро уходит прочь дугой (fleeSpeed),
///                                    лицом по ходу бега, не пересекая игрока. Как только
///                                    dist ≥ minDistance — возвращается к скрытной слежке.
///  - minDistance ≤ dist ≤ maxDistance → СЛЕЖКА: крадётся (stalkSpeed) к укрытию за спиной
///                                    игрока, сидит в нём и наблюдает, иногда меняет позицию.
///  - dist &gt; maxDistance         → ДОГОН: быстрее (chaseSpeed, без крадущегося темпа)
///                                    двигается к укрытию ближе к игроку, сокращая разрыв.
///
/// Туман: объекты с тегом coverTag (FogPlacer вешает его на очаги).
/// (IsConcealedAt оставлен на будущее, сейчас НЕ вызывается: туман не делает невидимым.)
///
/// Швы на будущее: OpenPursuit, Encircle, Attack — пока заглушки в enum.
///
/// ВАЖНО: пока работает сталкинг, держи WerewolfBrain выключенным (оба зовут MoveTo).
/// Скорости относительно локомоушна: stalkSpeed НИЖЕ boundEnterSpeed (без скачков),
/// fleeSpeed ВЫШЕ boundEnterSpeed (со скачками). chaseSpeed — посередине.
/// </summary>
[RequireComponent(typeof(WerewolfPerception))]
[RequireComponent(typeof(WerewolfLocomotion))]
public class AlphaStalker : MonoBehaviour
{
    public enum AlphaState { Stalk, Reposition /*, OpenPursuit, Encircle, Attack — позже*/ }

    [Header("Ссылки")]
    public WerewolfPerception perception;
    public WerewolfLocomotion locomotion;

    [Header("Зоны (м, до игрока)")]
    [Tooltip("Ближе этого — разрывает дистанцию (убегает).")]
    public float minDistance = 25f;
    [Tooltip("Дальше этого — догоняет. Между min и max — сидит в укрытии и наблюдает.")]
    public float maxDistance = 35f;

    [Header("Укрытия (туман)")]
    [Tooltip("Тег объектов-укрытий. Создай его в Project Settings → Tags. FogPlacer вешает его на очаги.")]
    public string coverTag = "FogPatch";
    [Tooltip("Радиус укрытия (м): внутри него оборотень считается спрятанным.")]
    public float coverRadius = 12f;
    [Tooltip("Допуск к дальней границе полосы при поиске очага тумана (м).")]
    public float coverSnapRadius = 14f;
    [Tooltip("Насколько близко к центру очага считать, что 'дошёл' и можно сидеть (м).")]
    public float coverArriveRadius = 2.5f;
    [Tooltip("Сколько секунд сидеть в укрытии, прежде чем сменить его.")]
    public float holdSeconds = 5f;

    [Header("Скорости (м/с)")]
    [Tooltip("Подкрад в полосе слежки. НИЖЕ boundEnterSpeed локомоушна — без скачков.")]
    public float stalkSpeed = 4f;
    [Tooltip("Догон, когда дальше maxDistance. Быстрее подкрада, красться не нужно.")]
    public float chaseSpeed = 8f;
    [Tooltip("Разрыв дистанции. ВЫШЕ boundEnterSpeed → срывается в скачки.")]
    public float fleeSpeed = 12f;

    [Header("Уход (разрыв дистанции)")]
    [Tooltip("На какую дистанцию ОТ игрока отбегать при разрыве (целься в полосу).")]
    public float repositionDistance = 32f;
    [Tooltip("Угол дуги вбок при уходе (град): больше — сильнее обходит сбоку, а не строго назад.")]
    public float repositionSpread = 60f;

    [Header("Давление сталкинга (задел под вой/призыв)")]
    [Tooltip("Копится, пока альфа в полосе слежки. Сейчас геймплейно ни на что не влияет.")]
    public float pressurePerSecond = 0.15f;
    [Tooltip("Автоматически дёргать onReadyToCall при заполнении. Обычно триггер внешний (святилище).")]
    public bool autoCallWhenPressured = false;

    /// <summary>Сигнал «альфа готова звать стаю». Подключай свой переход к фазе воя.</summary>
    public System.Action onReadyToCall;

    public AlphaState State => _state;
    public float StalkPressure01 => Mathf.Clamp01(_pressure);

    private AlphaState _state = AlphaState.Stalk;
    private Vector3 _repoTarget;
    private float _repoAngle;     // фиксированный угол дуги ухода (рад), выбран при входе
    private float _holdTimer;     // сколько уже сидим в текущем укрытии
    private float _pressure;

    // Кэш укрытий: собирается один раз при старте (туман статичен после генерации).
    private readonly List<Transform> _covers = new List<Transform>();
    private Transform _currentCover; // куда идём / где сидим
    private Transform _avoidCover;   // от какого очага только что отошли (чтобы сменить, а не вернуться)

    void Start()
    {
        if (perception == null) perception = GetComponent<WerewolfPerception>();
        if (locomotion == null) locomotion = GetComponent<WerewolfLocomotion>();
        RefreshCovers();
    }

    // =================== Укрытия ===================

    /// <summary>Пересобрать кэш укрытий (дёргай, если туман пересоздался в рантайме).</summary>
    public void RefreshCovers()
    {
        _covers.Clear();
        if (string.IsNullOrEmpty(coverTag)) return;

        GameObject[] found;
        try { found = GameObject.FindGameObjectsWithTag(coverTag); }
        catch (UnityException)
        {
            Debug.LogWarning($"AlphaStalker: тег '{coverTag}' не создан в Project Settings → Tags.");
            return;
        }
        foreach (var go in found) _covers.Add(go.transform);
    }

    /// <summary>
    /// Скрыт ли hider от seeker'а укрытием. Сейчас НЕ используется
    /// (туман не считается рейкастом против игрока), оставлено на будущее.
    /// </summary>
    public bool IsConcealedAt(Vector3 hider, Vector3 seeker)
    {
        float r2 = coverRadius * coverRadius;
        for (int i = 0; i < _covers.Count; i++)
        {
            if (_covers[i] == null) continue;
            Vector3 c = _covers[i].position;
            if (FlatSqr(hider, c) <= r2 && FlatSqr(seeker, c) > r2)
                return true;
        }
        return false;
    }

    // Очаг годен: в полосе дистанций от игрока И за его спиной (не перед лицом).
    // Полоса: [minDistance, maxDistance + coverSnapRadius] — не ближе зоны бегства,
    // с допуском по дальней границе, чтобы было к чему подтягиваться при догоне.
    private bool CoverIsValid(Vector3 c, Vector3 playerPos, Vector3 back)
    {
        float coverMin = minDistance;
        float coverMax = maxDistance + coverSnapRadius;
        float toPlayer = FlatSqr(playerPos, c);
        if (toPlayer < coverMin * coverMin || toPlayer > coverMax * coverMax) return false;

        Vector3 d = c - playerPos; d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) return true;
        d.Normalize();
        return Vector3.Dot(d, back) > 0.2f; // в пределах ~78° от строгой спины
    }

    // Ближайший к нам валидный очаг за спиной игрока. avoid — очаг, который пропускаем,
    // если есть альтернатива (чтобы сменить укрытие, а не сесть обратно в то же).
    private Transform FindCover(Transform avoid)
    {
        Vector3 playerPos = perception.PlayerPos;
        Vector3 back = -perception.PlayerForwardFlat;
        Vector3 self = transform.position;

        Transform best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < _covers.Count; i++)
        {
            if (_covers[i] == null || _covers[i] == avoid) continue;
            Vector3 c = _covers[i].position;
            if (!CoverIsValid(c, playerPos, back)) continue;

            float toSelf = FlatSqr(self, c);
            if (toSelf < bestSqr) { best = _covers[i]; bestSqr = toSelf; }
        }
        return best;
    }

    private static float FlatSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Нет игрока — стоим на месте.
        if (perception == null || !perception.HasPlayer) return;

        float dist = perception.DistanceToPlayer;

        switch (_state)
        {
            case AlphaState.Stalk: TickStalk(dt, dist); break;
            case AlphaState.Reposition: TickReposition(dt, dist); break;
        }
    }

    // =================== Stalk (слежка + догон) ===================

    private void TickStalk(float dt, float dist)
    {
        // Зона бегства: игрок слишком близко — разрываем дистанцию.
        if (dist < minDistance) { EnterReposition(); return; }

        // Скорость: дальше полосы — догоняем быстрее, в полосе — крадёмся.
        float moveSpeed = dist > maxDistance ? chaseSpeed : stalkSpeed;

        // Текущий очаг ещё годен? Если игрок ушёл — очаг выпадет из полосы.
        if (_currentCover != null && !CoverIsValid(_currentCover.position,
                perception.PlayerPos, -perception.PlayerForwardFlat))
        {
            _avoidCover = null;
            _currentCover = null;
            _holdTimer = 0f;
        }

        // Нет цели — выбираем ближайший очаг (избегая того, от которого только что отошли).
        if (_currentCover == null)
        {
            _currentCover = FindCover(_avoidCover);
            if (_currentCover == null) _avoidCover = null; // нет альтернативы — снимаем запрет
            _holdTimer = 0f;
        }

        // Укрытий рядом нет — двигаемся к точке в полосе за спиной игрока.
        if (_currentCover == null)
        {
            locomotion.MoveTo(ComputeStalkSlot(), moveSpeed, dt);
            locomotion.FaceTowards(perception.PlayerPos, dt);
            AccumulatePressure(dist, dt);
            return;
        }

        Vector3 cover = _currentCover.position;
        bool arrived = FlatSqr(transform.position, cover) <= coverArriveRadius * coverArriveRadius;

        if (!arrived)
        {
            // Идём к укрытию (крадёмся или догоняем — по moveSpeed).
            locomotion.MoveTo(cover, moveSpeed, dt);
            locomotion.FaceTowards(perception.PlayerPos, dt);
        }
        else
        {
            // Сидим в укрытии и наблюдаем (MoveTo НЕ зовём → локомоушн стоит).
            locomotion.FaceTowards(perception.PlayerPos, dt);
            _holdTimer += dt;
            if (_holdTimer >= holdSeconds)
            {
                // Пора сменить укрытие: запоминаем текущее как нежелательное.
                _avoidCover = _currentCover;
                _currentCover = null;
                _holdTimer = 0f;
            }
            AccumulatePressure(dist, dt);
        }
    }

    private void AccumulatePressure(float dist, float dt)
    {
        if (dist <= maxDistance + coverSnapRadius)
        {
            _pressure += pressurePerSecond * dt;
            if (autoCallWhenPressured && _pressure >= 1f) onReadyToCall?.Invoke();
        }
    }

    // Запасная точка слежки (если тумана нет): за спиной игрока в середине полосы.
    private Vector3 ComputeStalkSlot()
    {
        Vector3 behind = -perception.PlayerForwardFlat;
        float bandMid = (minDistance + maxDistance) * 0.5f;
        return perception.PlayerPos + behind * bandMid;
    }

    // =================== Reposition (разрыв дистанции дугой) ===================

    private void EnterReposition()
    {
        _state = AlphaState.Reposition;
        _currentCover = null;
        _avoidCover = null;
        _holdTimer = 0f;
        // Случайная сторона дуги, угол фиксируем — обходим стабильно влево или вправо.
        float sign = Random.value < 0.5f ? -1f : 1f;
        _repoAngle = sign * repositionSpread * Mathf.Deg2Rad;
    }

    private void TickReposition(float dt, float dist)
    {
        // Точку считаем каждый кадр от ТЕКУЩЕГО игрока (он движется), угол дуги зафиксирован.
        _repoTarget = ComputeFleePoint(_repoAngle);
        locomotion.MoveTo(_repoTarget, fleeSpeed, dt);   // высокая скорость → скачки
        locomotion.FaceTowards(_repoTarget, dt);

        // Разорвал дистанцию до полосы — назад к скрытной слежке.
        if (dist >= minDistance)
            _state = AlphaState.Stalk;
    }

    // Точка ухода: направление ОТ игрока К оборотню, отклонённое вбок на угол дуги.
    // Так он уходит наружу и в сторону, а не сквозь игрока.
    private Vector3 ComputeFleePoint(float angleRad)
    {
        Vector3 away = perception.DirFromPlayerFlat;
        Vector3 dir = new Vector3(
            away.x * Mathf.Cos(angleRad) - away.z * Mathf.Sin(angleRad),
            0f,
            away.x * Mathf.Sin(angleRad) + away.z * Mathf.Cos(angleRad));
        return perception.PlayerPos + dir * repositionDistance;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (perception == null || !perception.HasPlayer) return;
        Vector3 p = perception.PlayerPos;

        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.7f); Gizmos.DrawWireSphere(p, minDistance);
        Gizmos.color = new Color(0.9f, 0.8f, 0.2f, 0.6f); Gizmos.DrawWireSphere(p, maxDistance);

        Gizmos.color = new Color(0.7f, 0.7f, 0.9f, 0.35f);
        for (int i = 0; i < _covers.Count; i++)
            if (_covers[i] != null) Gizmos.DrawWireSphere(_covers[i].position, coverRadius);

        if (Application.isPlaying)
        {
            Vector3 t = _state == AlphaState.Reposition ? _repoTarget
                      : _currentCover != null ? _currentCover.position
                      : ComputeStalkSlot();
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, t);
            Gizmos.DrawWireCube(t, Vector3.one * 0.6f);
        }
    }
#endif
}
