using UnityEngine;

/// <summary>
/// Добоевое поведение альфа-оборотня (сталкинг). Отдельно от боевого WerewolfBrain.
///
/// Альфа — тень: держит слот на КОЛЬЦЕ вокруг игрока (сзади/с фланга) на безопасной
/// дистанции и наблюдает. Никакого патруля и потери интереса — всегда нацелена на игрока.
///
///  - Shadow  — крадётся по орбите к слоту за спиной, пока его НЕ видят. Скорость
///              падает по мере приближения к слоту: далеко идёт быстро (скачки),
///              у слота — крад. Ближе minDistance не подходит, при наезде — пятится.
///  - Frozen  — увидели: ЗАМИРАЕТ истуканом и таится (вдруг не заметили). Отвели
///              взгляд — снова крадётся; подошли вплотную — срывается в уход.
///  - Flee    — рывок: меняет фланг, быстро уходит за спину на другую сторону,
///              рвёт линию видимости, затем снова Shadow — уже с нового бока.
///
/// Швы на будущее: OpenPursuit, Encircle, Attack — пока заглушки в enum.
///
/// ВАЖНО: пока работает сталкинг, держи WerewolfBrain выключенным (оба зовут MoveTo).
/// Скорости подбирай относительно локомоушна: creepSpeed НИЖЕ boundEnterSpeed (без скачков),
/// approachSpeed/fleeSpeed ВЫШЕ boundEnterSpeed (со скачками).
/// </summary>
[RequireComponent(typeof(WerewolfPerception))]
[RequireComponent(typeof(WerewolfLocomotion))]
public class AlphaStalker : MonoBehaviour
{
    public enum AlphaState { Shadow, Frozen, Flee /*, OpenPursuit, Encircle, Attack — позже*/ }

    [Header("Ссылки")]
    public WerewolfPerception perception;
    public WerewolfLocomotion locomotion;

    [Header("Дистанции (м)")]
    [Tooltip("Радиус кольца, на котором альфа наблюдает за игроком.")]
    public float shadowDistance = 30f;
    [Tooltip("Жёсткий минимум: ближе этого альфа не подходит, при наезде пятится.")]
    public float minDistance = 20f;
    [Tooltip("Если игрок при замирании окажется ближе этого — альфа уходит.")]
    public float spookRadius = 16f;
    [Tooltip("Насколько игрок должен сократить дистанцию во время замирания, чтобы спугнуть (м).")]
    public float spookApproachMargin = 6f;

    [Header("Слот и орбита")]
    [Tooltip("Смещение слота от прямой спины игрока в сторону фланга (град).")]
    public float flankBias = 50f;
    [Tooltip("На сколько градусов кольца забегать вперёд к слоту за такт (даёт орбиту).")]
    public float orbitStepAngle = 35f;

    [Header("Выжидание")]
    [Tooltip("Допуск у слота (м): пока альфа внутри него — стоит и наблюдает, не семенит за игроком.")]
    public float holdBand = 3.5f;

    [Header("Скорости (м/с)")]
    [Tooltip("Тихий подкрад у слота. НИЖЕ boundEnterSpeed локомоушна — без скачков.")]
    public float creepSpeed = 4f;
    [Tooltip("Быстрый заход по дуге. В Shadow зажимается НИЖЕ boundEnterSpeed (без скачков).")]
    public float approachSpeed = 8f;
    [Tooltip("Скорость обхода при уходе. Зажимается НИЖЕ boundEnterSpeed (по земле): со скачками выбрасывало за кольцо.")]
    public float fleeSpeed = 12f;
    [Tooltip("Дистанция (м) от слота, на которой скорость уже максимальная (плавный спад).")]
    public float speedRampDistance = 8f;

    [Header("Замирание")]
    [Tooltip("Поворачиваться к игроку при замирании. Классика (Weeping Angel) — нет, стоит истуканом.")]
    public bool faceWhileFrozen = false;

    [Header("Давление сталкинга (задел под вой/призыв)")]
    [Tooltip("Копится, пока альфа близко и незамечена. Сейчас геймплейно ни на что не влияет.")]
    public float pressurePerSecond = 0.15f;
    [Tooltip("Автоматически дёргать onReadyToCall при заполнении. Обычно триггер внешний (святилище).")]
    public bool autoCallWhenPressured = false;

    /// <summary>Сигнал «альфа готова звать стаю». Подключай свой переход к фазе воя.</summary>
    public System.Action onReadyToCall;

    public AlphaState State => _state;
    public float StalkPressure01 => Mathf.Clamp01(_pressure);

    private AlphaState _state = AlphaState.Shadow;
    private int _flankSign = 1;        // +1/-1 — с какой стороны от прямой спины слот
    private float _freezeStartDist;
    private float _pressure;
    private bool _waiting;

    void Start()
    {
        if (perception == null) perception = GetComponent<WerewolfPerception>();
        if (locomotion == null) locomotion = GetComponent<WerewolfLocomotion>();
        _flankSign = Random.value < 0.5f ? -1 : 1;
    }

    void Update()
    {
        // Нет игрока — стоим (локомоушн сам тормозит, т.к. MoveTo не зовём).
        if (perception == null || !perception.HasPlayer) return;

        float dt = Time.deltaTime;
        float dist = perception.DistanceToPlayer;
        bool seen = perception.IsSeenByPlayer();

        switch (_state)
        {
            case AlphaState.Shadow: TickShadow(dt, dist, seen); break;
            case AlphaState.Frozen: TickFrozen(dt, dist, seen); break;
            case AlphaState.Flee: TickFlee(dt, dist, seen); break;
        }
    }

    // =================== Shadow ===================

    private void TickShadow(float dt, float dist, bool seen)
    {
        if (seen) { EnterFrozen(dist); return; }

        // Игрок наехал ближе минимума — пятимся прямо наружу, по земле.
        if (dist < minDistance)
        {
            Vector3 outPt = perception.PlayerPos + perception.DirFromPlayerFlat * shadowDistance;
            locomotion.MoveTo(outPt, Mathf.Min(approachSpeed, GroundSpeedCap()), dt);
            locomotion.FaceTowards(perception.PlayerPos, dt);
            _waiting = false;
            return;
        }

        float gap = FlatDistance(SlotPoint());

        // Диапазон выжидания (с гистерезисом): пока альфа в допуске у слота — стоит и
        // наблюдает, не дёргаясь за каждым шагом игрока. Двинется, когда слот уползёт.
        if (_waiting) { if (gap > holdBand * 1.5f) _waiting = false; }
        else if (gap <= holdBand) _waiting = true;

        if (_waiting)
        {
            locomotion.FaceTowards(perception.PlayerPos, dt);   // MoveTo не зовём → стоит
            AccumulatePressure(dt, dist);
            return;
        }

        // Орбита: забегаем по кольцу к слоту, скорость падает у слота.
        // По земле (ниже порога скачка), иначе баллистика уносит мимо дуги.
        Vector3 waypoint = OrbitWaypoint();
        float speed = Mathf.Lerp(creepSpeed, approachSpeed, Mathf.Clamp01(gap / speedRampDistance));
        speed = Mathf.Min(speed, GroundSpeedCap());

        locomotion.MoveTo(waypoint, speed, dt);
        locomotion.FaceTowards(perception.PlayerPos, dt);
        AccumulatePressure(dt, dist);
    }

    private void AccumulatePressure(float dt, float dist)
    {
        if (dist <= shadowDistance + 2f)
        {
            _pressure += pressurePerSecond * dt;
            if (autoCallWhenPressured && _pressure >= 1f) onReadyToCall?.Invoke();
        }
    }

    // =================== Frozen ===================

    private void EnterFrozen(float dist)
    {
        _state = AlphaState.Frozen;
        _freezeStartDist = dist;
    }

    private void TickFrozen(float dt, float dist, bool seen)
    {
        // Не зовём MoveTo → локомоушн тормозит до нуля, альфа замирает.
        if (faceWhileFrozen) locomotion.FaceTowards(perception.PlayerPos, dt);

        // Подошёл вплотную / сократил дистанцию → понял, что обнаружен → уходит.
        bool closingIn = dist <= spookRadius || dist <= _freezeStartDist - spookApproachMargin;
        if (closingIn) { EnterFlee(); return; }

        // Отвёл взгляд — таиться больше не нужно, снова крадёмся с того же бока.
        if (!seen) _state = AlphaState.Shadow;
    }

    // =================== Flee ===================

    private void EnterFlee()
    {
        _state = AlphaState.Flee;
        _flankSign = -_flankSign;            // обратно зайдём с другого бока
        _waiting = false;
    }

    private void TickFlee(float dt, float dist, bool seen)
    {
        // Уходим, ОБХОДЯ игрока по кольцу к противоположному флангу (slot уже перевёрнут).
        // Цель — точка на кольце возле игрока, поэтому скачки безопасны (к краю не унесёт),
        // а дуга сама держит дистанцию: если игрок близко, орбита уводит к shadowDistance.
        Vector3 waypoint = OrbitWaypoint();
        locomotion.MoveTo(waypoint, Mathf.Min(fleeSpeed, GroundSpeedCap()), dt);   // по земле — без выброса
        locomotion.FaceTowards(waypoint, dt);         // смотрит, куда бежит

        // Обогнул спину и вышел на новый фланг на безопасной дистанции — снова наблюдает.
        float offset = Mathf.DeltaAngle(BehindDeg(), CurrentDeg());     // где альфа относительно спины
        bool onNewFlank = Mathf.Sign(offset) == Mathf.Sign(flankBias * _flankSign)
                          && Mathf.Abs(offset) >= flankBias * 0.5f;
        if (onNewFlank && dist >= shadowDistance - 2f) { _state = AlphaState.Shadow; _waiting = false; }
    }

    // =================== Геометрия кольца ===================

    // Угол прямой спины игрока (град).
    private float BehindDeg()
    {
        Vector3 behind = -perception.PlayerForwardFlat;
        return Mathf.Atan2(behind.z, behind.x) * Mathf.Rad2Deg;
    }

    // Целевой угол слота на кольце (спина + фланговое смещение).
    private float SlotDeg() => BehindDeg() + flankBias * _flankSign;

    // Текущий угол альфы вокруг игрока (град).
    private float CurrentDeg()
    {
        Vector3 d = perception.DirFromPlayerFlat;
        return Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
    }

    private static Vector3 DirFromDeg(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(r), 0f, Mathf.Sin(r));
    }

    // Точка слота (где альфа хочет в итоге стоять).
    private Vector3 SlotPoint() => perception.PlayerPos + DirFromDeg(SlotDeg()) * shadowDistance;

    // Ближний waypoint на кольце — забег по дуге к слоту.
    private Vector3 OrbitWaypoint()
    {
        float delta = Mathf.DeltaAngle(CurrentDeg(), SlotDeg());     // кратчайший знаковый угол
        float step = Mathf.Clamp(delta, -orbitStepAngle, orbitStepAngle);
        return perception.PlayerPos + DirFromDeg(CurrentDeg() + step) * shadowDistance;
    }

    // Потолок наземной скорости: чуть ниже порога скачка, чтобы Shadow не срывался
    // в баллистические прыжки и точно отрабатывал дугу. Скачки оставлены только для Flee.
    private float GroundSpeedCap()
    {
        float leap = locomotion != null ? locomotion.boundEnterSpeed : 9f;
        return Mathf.Max(creepSpeed, leap - 0.5f);
    }

    private float FlatDistance(Vector3 p)
    {
        Vector3 d = p - transform.position; d.y = 0f;
        return d.magnitude;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (perception == null || !perception.HasPlayer) return;
        Vector3 p = perception.PlayerPos;

        Gizmos.color = new Color(0.9f, 0.2f, 0.2f, 0.7f); Gizmos.DrawWireSphere(p, minDistance);
        Gizmos.color = new Color(0.2f, 0.7f, 0.5f, 0.6f); Gizmos.DrawWireSphere(p, shadowDistance);
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.5f); Gizmos.DrawWireSphere(p, spookRadius);

        if (Application.isPlaying)
        {
            Vector3 t = (_state == AlphaState.Flee || _state == AlphaState.Shadow)
                      ? OrbitWaypoint()
                      : transform.position;
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, t);
            Gizmos.DrawWireCube(t, Vector3.one * 0.6f);

            Gizmos.color = new Color(0.9f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(SlotPoint(), Vector3.one * 0.8f);
        }
    }
#endif
}
