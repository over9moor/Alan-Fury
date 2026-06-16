using UnityEngine;

[System.Serializable]
public struct GaitConfig
{
    public float speed;          // базовая скорость (м/с)
    public float acceleration;   // разгон (выше = отзывчивее)
    public float deceleration;   // торможение (выше = резче, ниже = скользит)
    public float stepDistance;   // импульс шага поверх базовой скорости
    public float stepDuration;   // длина шага (сек)
    public float stepFrequency;  // шагов в секунду
}

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement3D : MonoBehaviour
{
    [Header("Режимы движения")]
    public GaitConfig walk = new GaitConfig
    {
        speed = 4f,
        acceleration = 25f,
        deceleration = 30f,   // тормозит почти мгновенно
        stepDistance = 0.5f,
        stepDuration = 0.35f,
        stepFrequency = 2.2f
    };

    public GaitConfig run = new GaitConfig
    {
        speed = 8f,
        acceleration = 18f,
        deceleration = 14f,   // чуть скользит
        stepDistance = 1.0f,
        stepDuration = 0.22f,
        stepFrequency = 3.2f
    };

    public GaitConfig sprint = new GaitConfig
    {
        speed = 13f,
        acceleration = 12f,
        deceleration = 6f,    // заметная инерция
        stepDistance = 1.6f,
        stepDuration = 0.16f,
        stepFrequency = 4.5f
    };

    [Header("Уворот")]
    public float dodgeSpeed = 14f;
    public float dodgeDuration = 0.25f;
    public float dodgeCooldown = 0.6f;

    [Header("Перекат")]
    public float rollSpeed = 11f;
    public float rollDuration = 0.4f;
    public float rollCooldown = 1.2f;

    [Header("Прочее")]
    public float rotationSpeed = 15f;
    public float gravity = -20f;
    
    [Header("Анимация")]
    public Animator animator;

    [Header("Граница карты (опционально)")]
    [Tooltip("Если не задана — берётся MapBoundary с этого же объекта.")]
    public MapBoundary boundary;

    // Компоненты
    private CharacterController _controller;
    private Camera _mainCamera;

    // Состояние движения
    private Vector3 _velocity;          // горизонтальная скорость
    private float _verticalVelocity;
    private GaitConfig _currentGait;
    private bool _isRunning;
    // Поворот (снап по 45°)
    private float _lastActiveAngle = -180f; // последнее активное направление; старт — вниз-вправо

    // Манёвры
    private bool _isDodging;
    private bool _isRolling;
    private float _maneuverTimer;
    private float _maneuverSpeed;
    private Vector3 _maneuverDir;
    private float _lastDodgeTime;
    private float _lastRollTime;

    // Шаги
    private readonly StepController _step = new StepController();

    // ──────────────────────────────────────────────

    void Start()
    {
        _controller = GetComponent<CharacterController>();
        _mainCamera = Camera.main;
        _currentGait = walk;

        if (boundary == null) boundary = GetComponent<MapBoundary>();
    }

    void Update()
    {
        HandleManeuvers();

        if (_isDodging || _isRolling)
        {
            ApplyGravity();
            return;
        }

        HandleGait();
        HandleMovement();
        HandleRotation();
        ApplyGravity();

        if (animator != null)
            animator.SetBool("IsMoving", _velocity.magnitude > 0.1f);
    }

    // ──────────────────────────────────────────────
    // Перемещение через границу карты
    // ──────────────────────────────────────────────

    // Все горизонтальные перемещения идут сюда: граница гасит движение
    // наружу в приграничной полосе и не пускает за край (в т.ч. перекатом).
    void MoveHorizontal(Vector3 delta)
    {
        if (boundary != null && boundary.IsReady)
            delta = boundary.Constrain(transform.position, delta);

        _controller.Move(delta);
    }

    // ──────────────────────────────────────────────
    // Манёвры
    // ──────────────────────────────────────────────

    void HandleManeuvers()
    {
        if (Input.GetKeyDown(KeyCode.LeftAlt)
            && Time.time - _lastDodgeTime > dodgeCooldown
            && _maneuverDir.magnitude > 0.1f)
        {
            StartManeuver(dodgeSpeed, dodgeDuration, ref _isDodging, ref _lastDodgeTime);
        }
        else if (Input.GetKeyDown(KeyCode.Space)
            && Time.time - _lastRollTime > rollCooldown
            && _maneuverDir.magnitude > 0.1f)
        {
            StartManeuver(rollSpeed, rollDuration, ref _isRolling, ref _lastRollTime);
        }

        if (!_isDodging && !_isRolling) return;

        _maneuverTimer -= Time.deltaTime;
        if (_maneuverTimer <= 0f)
        {
            _isDodging = false;
            _isRolling = false;
            return;
        }

        MoveHorizontal(_maneuverDir * _maneuverSpeed * Time.deltaTime);
    }

    void StartManeuver(float speed, float duration, ref bool flag, ref float lastTime)
    {
        flag = true;
        lastTime = Time.time;
        _maneuverTimer = duration;
        _maneuverSpeed = speed;
        _velocity = _maneuverDir * speed;
        _step.Cancel();
    }

    // ──────────────────────────────────────────────
    // Выбор режима
    // ──────────────────────────────────────────────

    void HandleGait()
    {
        if (Input.GetKeyDown(KeyCode.CapsLock))
            _isRunning = !_isRunning;

        if (Input.GetKey(KeyCode.LeftShift))
            _currentGait = sprint;
        else if (_isRunning)
            _currentGait = run;
        else
            _currentGait = walk;
    }

    // ──────────────────────────────────────────────
    // Движение
    // ──────────────────────────────────────────────

    void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v).normalized;

        if (input.magnitude > 0.1f && _mainCamera != null)
        {
            // Направление относительно камеры
            Vector3 forward = _mainCamera.transform.forward;
            Vector3 right = _mainCamera.transform.right;
            forward.y = 0f; right.y = 0f;
            forward.Normalize(); right.Normalize();

            Vector3 targetDir = (forward * input.z + right * input.x).normalized;
            _maneuverDir = targetDir;

            // Целевая скорость = базовая + импульс шага
            _step.TryStart(_currentGait.speed, walk,
                Input.GetKey(KeyCode.LeftShift) ? sprint : run);
            float stepImpulse = _step.Tick(Time.deltaTime);

            Vector3 targetVelocity = targetDir * (_currentGait.speed + stepImpulse);

            // Плавный разгон через SmoothDamp
            _velocity = Vector3.MoveTowards(
                _velocity,
                targetVelocity,
                _currentGait.acceleration * Time.deltaTime);

            MoveHorizontal(_velocity * Time.deltaTime);
        }
        else
        {
            _step.Cancel();

            // Торможение — у walk резкое, у sprint плавное
            if (_velocity.magnitude > 0.05f)
            {
                _velocity = Vector3.MoveTowards(
                    _velocity,
                    Vector3.zero,
                    _currentGait.deceleration * Time.deltaTime);
                MoveHorizontal(_velocity * Time.deltaTime);
            }
            else
            {
                _velocity = Vector3.zero;
            }
        }
    }

    // ──────────────────────────────────────────────
    // Поворот на мышь
    // ──────────────────────────────────────────────

    void HandleRotation()
    {
        if (_mainCamera == null) return;

        if (_velocity.magnitude > 0.1f)
        {
            // Движение: снап направления на мышь к ближайшему из 8 (шаг 45°)
            Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
            if (new Plane(Vector3.up, transform.position).Raycast(ray, out float dist))
            {
                Vector3 look = ray.GetPoint(dist) - transform.position;
                look.y = 0f;
                if (look.magnitude > 0.1f)
                {
                    float angle = Quaternion.LookRotation(look).eulerAngles.y;
                    float snapped = Mathf.Round(angle / 45f) * 45f;
                    _lastActiveAngle = Mathf.DeltaAngle(0f, snapped);
                    transform.rotation = Quaternion.Euler(0f, snapped, 0f);
                }
            }
        }
        else
        {
            // Покой: вниз-вправо (-180) или вниз-влево (-90)
            float a = Mathf.DeltaAngle(0f, _lastActiveAngle);
            bool left = (a == -135f || a == -90f || a == -45f || a == 0f);
            float rest = left ? -90f : -180f;
            transform.rotation = Quaternion.Euler(0f, rest, 0f);
        }
    }


    void ApplyGravity()
    {
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        else
            _verticalVelocity += gravity * Time.deltaTime;

        _controller.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
    }
}
