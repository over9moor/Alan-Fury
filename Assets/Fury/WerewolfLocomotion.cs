using UnityEngine;

/// <summary>
/// Движение оборотня через CharacterController. Лестница аллюров:
///  - низ/середина — бег по земле с импульсом шага (StepController), как у игрока;
///  - верх — скачки: включаются только после разбега (скорость ≥ boundEnterSpeed,
///    удержанная boundChargeTime), чейнятся с переносом импульса и за счёт
///    boundTakeoffBoost дают БОЛЬШЕ метров в секунду, чем бег — самый быстрый аллюр;
///  - упор в рельеф впереди — перепрыгивание препятствия.
///
/// Мозг лишь говорит «беги к точке с такой скоростью» (MoveTo), а выбор аллюра —
/// следствие фактической скорости, поэтому WerewolfBrain от этого не зависит.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class WerewolfLocomotion : MonoBehaviour
{
    [Header("Граница карты (опционально)")]
    public MapBoundary boundary;

    [Header("Разгон/торможение")]
    public float acceleration = 18f;
    public float deceleration = 22f;
    [Tooltip("На какой дистанции до цели начинать тормозить (м).")]
    public float slowdownDistance = 2.5f;

    [Header("Наземная походка (шаги)")]
    [Tooltip("У этих гейтов важны только speed/stepDistance/stepDuration/stepFrequency. " +
             "acceleration/deceleration НЕ используются — разгон берётся из полей выше.")]
    public GaitConfig trot = new GaitConfig
    {
        speed = 3.5f,
        stepDistance = 0.4f,
        stepDuration = 0.28f,
        stepFrequency = 2.6f
    };
    public GaitConfig gallop = new GaitConfig
    {
        speed = 9f,
        stepDistance = 0.9f,
        stepDuration = 0.18f,
        stepFrequency = 3.6f
    };

    [Header("Скачки — верхний аллюр (самый быстрый)")]
    [Tooltip("Скорость, выше которой (удержав boundChargeTime) бег переходит в скачки (м/с).")]
    public float boundEnterSpeed = 9f;
    [Tooltip("Опускаемся ниже — скачки прекращаются (гистерезис, м/с).")]
    public float boundExitSpeed = 6.5f;
    [Tooltip("Сколько надо держать высокую скорость на земле перед первым скачком (сек).")]
    public float boundChargeTime = 0.25f;
    [Tooltip("Множитель горизонт. импульса на отталкивании — делает скачок дальше бега.")]
    public float boundTakeoffBoost = 1.2f;
    [Tooltip("Потолок горизонтальной скорости в скачках (м/с).")]
    public float maxBoundSpeed = 14f;
    [Tooltip("Контакт с землёй между скачками (сек). Мал = слитный галоп, велик = отдельные прыжки.")]
    public float boundGroundTime = 0.08f;
    public float minBoundHeight = 0.5f;
    public float maxBoundHeight = 1.4f;
    [Tooltip("Скорость, при которой скачок максимально высокий/длинный (м/с).")]
    public float boundReferenceSpeed = 12f;

    [Header("Перепрыгивание рельефа")]
    public float obstacleLeapHeight = 1.6f;
    [Tooltip("Дальность проверки препятствия впереди (м).")]
    public float obstacleProbe = 0.6f;
    [Tooltip("Минимальный горизонт. импульс при перепрыгивании препятствия (м/с).")]
    public float obstacleMinSpeed = 6f;

    [Header("Гравитация")]
    public float gravity = -20f;

    [Header("Поворот")]
    public float rotationSpeed = 6f;

    [Header("Прибытие")]
    public float arriveThreshold = 1.5f;

    [Header("Старт: посадка на землю")]
    public LayerMask groundLayers = ~0;
    public float groundProbeHeight = 50f;

    private CharacterController _cc;
    private Vector3 _horizVel;
    private float _vertVel;
    private bool _leaping;        // сейчас в воздухе (скачок/бросок)
    private bool _bounding;       // режим галоп-скачков (поверх _leaping)
    private float _chargeTimer;   // накопленное время высокой скорости на земле
    private float _groundTimer;   // контакт с землёй с момента приземления
    private float _stepImpulse;   // импульс шага в этом кадре (наземная походка)
    private bool _placed;

    private readonly StepController _step = new StepController();

    private int _moveFrame = -1;
    private Vector3 _moveTarget;
    private float _moveSpeed;

    public bool IsGrounded => _cc != null && _cc.isGrounded;
    public bool IsLeaping => _leaping;
    public bool IsBounding => _bounding;
    public float CurrentSpeed => _horizVel.magnitude;
    public float StepCurve => _step.Curve;   // для боба/анимации/звука

    void Awake() => _cc = GetComponent<CharacterController>();
    void Start() { if (boundary == null) boundary = GetComponent<MapBoundary>(); }

    /// <summary>Заново посадить на землю (например, после перегенерации карты).</summary>
    public void RequestGroundSnap() => _placed = false;

    // =================== Намерение от мозга ===================

    /// <summary>Двигаться к цели с заданной скоростью. true, когда дошли.</summary>
    public bool MoveTo(Vector3 target, float speed, float dt)
    {
        _moveFrame = Time.frameCount;
        _moveTarget = target;
        _moveSpeed = speed;
        return !_leaping && FlatDistance(target) <= arriveThreshold;
    }

    /// <summary>Одиночный бросок точно в точку (прыжок-наскок в бою).</summary>
    public void Leap(Vector3 target, float height)
    {
        if (_leaping || !_cc.isGrounded) return;
        Vector3 to = target - transform.position; to.y = 0f;
        float dist = to.magnitude;
        Vector3 dir = dist > 0.0001f ? to / dist : transform.forward;
        float g = -gravity;
        float vUp = Mathf.Sqrt(2f * g * height);
        float airTime = 2f * vUp / g;
        _horizVel = dir * (airTime > 0.0001f ? dist / airTime : 0f);
        _vertVel = vUp;
        _leaping = true; _groundTimer = 0f;
        _step.Cancel();
    }

    /// <summary>Вертикальный прыжок на месте.</summary>
    public void Jump(float height)
    {
        if (!_cc.isGrounded || _leaping) return;
        _vertVel = Mathf.Sqrt(2f * -gravity * height);
    }

    public void FaceTowards(Vector3 worldPoint, float dt)
    {
        Vector3 look = worldPoint - transform.position; look.y = 0f;
        if (look.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(look.normalized, Vector3.up), rotationSpeed * dt);
    }

    // =================== Физика (после мозга) ===================

    void LateUpdate()
    {
        if (!_placed) { if (TryPlaceOnGround()) _placed = true; return; }

        float dt = Time.deltaTime;
        bool active = _moveFrame == Time.frameCount;
        Vector3 pos = transform.position;

        _stepImpulse = 0f;
        if (_cc.isGrounded) _groundTimer += dt;

        if (_leaping)
        {
            // В полёте горизонталь зафиксирована — баллистика.
        }
        else
        {
            // --- Горизонтальная скорость: разгон/торможение ---
            if (active)
            {
                Vector3 to = _moveTarget - pos; to.y = 0f;
                float dist = to.magnitude;
                Vector3 dir = dist > 0.001f ? to / dist : Vector3.zero;
                float targetSpeed = dist < slowdownDistance
                    ? Mathf.Lerp(0f, _moveSpeed, dist / slowdownDistance)
                    : _moveSpeed;
                Vector3 targetVel = dir * targetSpeed;
                float rate = targetVel.magnitude > _horizVel.magnitude ? acceleration : deceleration;
                _horizVel = Vector3.MoveTowards(_horizVel, targetVel, rate * dt);
            }
            else
            {
                _horizVel = Vector3.MoveTowards(_horizVel, Vector3.zero, deceleration * dt);
            }

            float spd = _horizVel.magnitude;

            // --- Заряд скачка + гистерезис ---
            if (_cc.isGrounded && spd >= boundEnterSpeed) _chargeTimer += dt;
            else if (spd < boundEnterSpeed) _chargeTimer = 0f;
            if (_bounding && spd < boundExitSpeed) _bounding = false;

            // --- Выбор аллюра на очередном такте контакта с землёй ---
            bool beat = _cc.isGrounded && _groundTimer >= boundGroundTime;

            if (beat && active && ObstacleAhead())
            {
                StartBound(obstacleLeapHeight, obstacleMinSpeed, false);   // перепрыгнуть рельеф
            }
            else if (beat && (_bounding || _chargeTimer >= boundChargeTime) && spd >= boundExitSpeed)
            {
                _bounding = true;
                StartBound(BoundHeightForSpeed(spd), 0f, true);            // галоп-скачок (верхний аллюр)
            }
            else if (active && spd > 0.1f)
            {
                // Наземная походка с импульсом шага (как у игрока)
                _step.TryStart(spd, trot, gallop);
                _stepImpulse = _step.Tick(dt);
            }
            else
            {
                _step.Cancel();
            }
        }

        // --- Гравитация ---
        if (_cc.isGrounded && _vertVel <= 0f)
        {
            if (_leaping) { _leaping = false; _groundTimer = 0f; } // приземлились, импульс сохраняем
            _vertVel = -2f;
        }
        else _vertVel += gravity * dt;

        // --- Движение: горизонталь (+ импульс шага) через границу карты + вертикаль ---
        Vector3 stepVec = (_stepImpulse != 0f && _horizVel.sqrMagnitude > 0.0001f)
            ? _horizVel.normalized * _stepImpulse
            : Vector3.zero;

        Vector3 horiz = (_horizVel + stepVec) * dt; horiz.y = 0f;
        if (boundary != null && boundary.IsReady) horiz = boundary.Constrain(pos, horiz);
        _cc.Move(horiz + Vector3.up * (_vertVel * dt));
    }

    // =================== helpers ===================

    private void StartBound(float height, float minHorizontalSpeed, bool boost)
    {
        Vector3 dir = _horizVel.sqrMagnitude > 0.0001f ? _horizVel.normalized : transform.forward;
        float spd = Mathf.Max(_horizVel.magnitude, minHorizontalSpeed);
        if (spd < 0.01f) return;

        if (boost) spd = Mathf.Min(spd * boundTakeoffBoost, maxBoundSpeed); // взрывное отталкивание

        _horizVel = dir * spd;                 // сохраняем/задаём горизонтальный импульс
        _vertVel = Mathf.Sqrt(2f * -gravity * height);
        _leaping = true; _groundTimer = 0f;
        _step.Cancel();
    }

    private float BoundHeightForSpeed(float spd)
    {
        float t = Mathf.InverseLerp(boundExitSpeed, boundReferenceSpeed, spd);
        return Mathf.Lerp(minBoundHeight, maxBoundHeight, t);
    }

    private bool ObstacleAhead()
    {
        if (_horizVel.sqrMagnitude < 0.04f) return false;
        Vector3 dir = _horizVel.normalized;
        Vector3 origin = transform.position + Vector3.up * (_cc.stepOffset + 0.15f);
        float dist = _cc.radius + obstacleProbe;
        return Physics.Raycast(origin, dir, dist, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private bool TryPlaceOnGround()
    {
        _cc.enabled = false;
        Vector3 origin = transform.position + Vector3.up * groundProbeHeight;
        bool found = Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                     groundProbeHeight * 2f, groundLayers,
                                     QueryTriggerInteraction.Ignore);
        if (found)
        {
            Vector3 p = transform.position;
            p.y = hit.point.y + _cc.skinWidth + _cc.height * 0.5f - _cc.center.y + 0.02f;
            transform.position = p;
            _vertVel = 0f;
        }
        _cc.enabled = true;
        return found;
    }

    private float FlatDistance(Vector3 p)
    {
        Vector3 d = p - transform.position; d.y = 0f;
        return d.magnitude;
    }
}
