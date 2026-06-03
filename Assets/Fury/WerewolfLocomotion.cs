using UnityEngine;

/// <summary>
/// Движение оборотня через CharacterController.
/// Походка выбирается автоматически:
///  - ниже boundSpeedThreshold — бег шагами по земле;
///  - выше — прыжки-скачки (чем быстрее, тем выше и длиннее дуга);
///  - упор в рельеф впереди — перепрыгивание препятствия.
///
/// Мозг лишь говорит «беги к точке с такой скоростью» (MoveTo),
/// а скачки — следствие скорости, поэтому WerewolfBrain не зависит
/// от способа передвижения.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class WerewolfLocomotion : MonoBehaviour
{
    [Header("Граница карты (опционально)")]
    public MapBoundary boundary;

    [Header("Бег (шаги)")]
    public float acceleration = 18f;
    public float deceleration = 22f;
    [Tooltip("На какой дистанции до цели начинать тормозить (м).")]
    public float slowdownDistance = 2.5f;

    [Header("Скачки (на скорости)")]
    [Tooltip("Выше этой горизонтальной скорости бег переходит в скачки (м/с).")]
    public float boundSpeedThreshold = 6f;
    [Tooltip("Скорость, при которой скачок максимально высокий/длинный (м/с).")]
    public float boundReferenceSpeed = 12f;
    public float minBoundHeight = 0.5f;
    public float maxBoundHeight = 1.4f;
    [Tooltip("Контакт с землёй между скачками (сек). Мал = галоп, велик = отдельные прыжки.")]
    public float boundGroundTime = 0.08f;

    [Header("Перепрыгивание рельефа")]
    public float obstacleLeapHeight = 1.6f;
    [Tooltip("Дальность проверки препятствия впереди (м).")]
    public float obstacleProbe = 0.6f;

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
    private bool _leaping;
    private float _groundTimer;
    private bool _placed;

    private int _moveFrame = -1;
    private Vector3 _moveTarget;
    private float _moveSpeed;

    public bool IsGrounded => _cc != null && _cc.isGrounded;
    public bool IsLeaping => _leaping;
    public float CurrentSpeed => _horizVel.magnitude;

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

        if (_cc.isGrounded) _groundTimer += dt;

        if (_leaping)
        {
            // В полёте горизонталь зафиксирована — баллистика.
        }
        else
        {
            // Горизонтальная скорость с разгоном/торможением
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

            // Походка: шаги или скачок
            if (_cc.isGrounded && _groundTimer >= boundGroundTime)
            {
                float spd = _horizVel.magnitude;
                if (active && ObstacleAhead())
                    StartBound(obstacleLeapHeight, boundSpeedThreshold); // перепрыгнуть рельеф
                else if (spd > boundSpeedThreshold)
                    StartBound(BoundHeightForSpeed(spd), 0f);            // скачок на скорости
            }
        }

        // Гравитация
        if (_cc.isGrounded && _vertVel <= 0f)
        {
            if (_leaping) { _leaping = false; _groundTimer = 0f; } // приземлились, импульс сохраняем
            _vertVel = -2f;
        }
        else _vertVel += gravity * dt;

        // Движение: горизонталь через границу карты + вертикаль
        Vector3 horiz = _horizVel * dt; horiz.y = 0f;
        if (boundary != null && boundary.IsReady) horiz = boundary.Constrain(pos, horiz);
        _cc.Move(horiz + Vector3.up * (_vertVel * dt));
    }

    // =================== helpers ===================

    private void StartBound(float height, float minHorizontalSpeed)
    {
        Vector3 dir = _horizVel.sqrMagnitude > 0.0001f ? _horizVel.normalized : transform.forward;
        float spd = Mathf.Max(_horizVel.magnitude, minHorizontalSpeed);
        if (spd < 0.01f) return;

        _horizVel = dir * spd;                 // сохраняем/задаём горизонтальный импульс
        _vertVel = Mathf.Sqrt(2f * -gravity * height);
        _leaping = true; _groundTimer = 0f;
    }

    private float BoundHeightForSpeed(float spd)
    {
        float t = Mathf.InverseLerp(boundSpeedThreshold, boundReferenceSpeed, spd);
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
