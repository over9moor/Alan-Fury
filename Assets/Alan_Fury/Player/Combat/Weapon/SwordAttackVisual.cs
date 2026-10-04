using UnityEngine;

public class SwordAttackVisual : MonoBehaviour
{
    [Header("Спрайт атаки")]
    public Sprite arcSprite;
    public Color arcColor = new Color(1f, 0.2f, 0.2f, 0.7f);
    public float arcRadius = 2f;
    public float arcHeight = 1.5f;
    [Tooltip("Насколько дуга вылетает вперёд за время жизни (м).")]
    public float flyDistance = 0.7f;

    [Header("Индикатор замаха")]
    public Color windupColor = new Color(1f, 1f, 0f, 0.4f);

    [Header("Меши слэша")]
    [Tooltip("Меш фазы 'жёлтая зона' (телеграф). Выбор по comboIndex % Length.")]
    public GameObject[] preSlashMeshes;
    [Tooltip("Меш фазы 'красная волна' (sweep). Выбор по comboIndex % Length.")]
    public GameObject[] slashMeshes;
    [Tooltip("Меш после удара. Выбор по comboIndex % Length.")]
    public GameObject[] afterSlashMeshes;
    [Tooltip("Сколько держать after-slash меш после гашения дуги (сек).")]
    public float afterObjectDuration = 0.5f;

    [Header("Меши тычка (Capsule)")]
    [Tooltip("Меш фазы замаха тычка (телеграф).")]
    public GameObject thrustMesh;
    [Tooltip("Меш после тычка. Держится afterObjectDuration.")]
    public GameObject afterThrustMesh;

    private SpriteRenderer arcRenderer;
    private SpriteRenderer windupRenderer;

    private WeaponHitbox _hitbox;
    private bool isShowingArc;
    private float arcTimer;
    private float arcDuration;
    private Vector3 flyDir;
    private Vector3 arcStartPos;

    private bool isShowingAfter;
    private float afterTimer;

    private GameObject _activeTelegraphObject;
    private GameObject _activeStrikeObject;
    private GameObject _activeAfterObject;

    private int _lastComboIndex;
    private int _pendingWindupCombo;
    private bool _telegraphWasOn;

    private bool _thrustWindupWasOn;
    private bool _thrustAfterShowing;
    private float _thrustAfterTimer;

    void Awake()
    {
        _hitbox = GetComponent<WeaponHitbox>();
        if (_hitbox == null) _hitbox = GetComponentInParent<WeaponHitbox>();

        // --- Спрайт атаки ---
        GameObject arcObj = new GameObject("ArcSprite");
        arcObj.transform.SetParent(transform);
        arcObj.transform.localPosition = Vector3.zero;

        arcRenderer = arcObj.AddComponent<SpriteRenderer>();
        arcRenderer.sprite = arcSprite;
        arcRenderer.color = arcColor;
        arcRenderer.sortingOrder = 100;
        arcRenderer.enabled = false;

        // --- Индикатор замаха ---
        GameObject windupObj = new GameObject("WindupSprite");
        windupObj.transform.SetParent(transform);
        windupObj.transform.localPosition = Vector3.zero;

        windupRenderer = windupObj.AddComponent<SpriteRenderer>();
        windupRenderer.sprite = arcSprite;
        windupRenderer.color = windupColor;
        windupRenderer.sortingOrder = 99;
        windupRenderer.enabled = false;

        // --- Меши: стартуют скрытыми ---
        DeactivateAll(preSlashMeshes);
        DeactivateAll(slashMeshes);
        DeactivateAll(afterSlashMeshes);
        if (thrustMesh != null) thrustMesh.SetActive(false);
        if (afterThrustMesh != null) afterThrustMesh.SetActive(false);
    }

    static void DeactivateAll(GameObject[] arr)
    {
        if (arr == null) return;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i] != null) arr[i].SetActive(false);
    }

    static GameObject Pick(GameObject[] arr, int comboIndex)
    {
        if (arr == null || arr.Length == 0) return null;
        int len = arr.Length;
        int idx = comboIndex % len;
        if (idx < 0) idx += len;
        return arr[idx];
    }

    bool IsThrustShape()
    {
        return _hitbox != null && _hitbox.CurrentShape == HitZoneShape.Capsule;
    }

    void Update()
    {
        UpdateTelegraphMesh();
        UpdateThrustMeshes();

        if (isShowingAfter)
        {
            afterTimer -= Time.deltaTime;
            if (afterTimer <= 0f)
            {
                isShowingAfter = false;
                if (_activeAfterObject != null)
                {
                    _activeAfterObject.SetActive(false);
                    _activeAfterObject = null;
                }
            }
        }

        if (!isShowingArc) return;

        arcTimer -= Time.deltaTime;

        if (arcTimer <= 0f)
        {
            HideArc();
            return;
        }

        float t = arcDuration > 0.0001f ? Mathf.Clamp01(arcTimer / arcDuration) : 0f;
        Color c = arcColor;
        c.a = arcColor.a * Mathf.Lerp(0.25f, 1f, t);
        arcRenderer.color = c;

        if (_hitbox != null && _hitbox.IsSweeping)
        {
            _hitbox.GetStrikePose(out _, out Vector3 tip, out Vector3 cut);
            Vector3 mid = Vector3.Lerp(_hitbox.ZoneOrigin, tip, 0.72f) + Vector3.up * (arcHeight * 0.4f);
            float yaw = Mathf.Atan2(cut.x, cut.z) * Mathf.Rad2Deg;
            arcRenderer.transform.position = mid;
            arcRenderer.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
        }
        else
        {
            arcRenderer.transform.position = arcStartPos + flyDir * (flyDistance * (1f - t));
        }
    }

    // Меш фазы "жёлтая зона" — привязан к IsTelegraphing хитбокса.
    // Для Capsule (тычок) сюда не лезем — там работает UpdateThrustMeshes.
    void UpdateTelegraphMesh()
    {
        if (_hitbox == null) return;
        bool on = _hitbox.IsTelegraphing && !IsThrustShape();

        if (on)
        {
            if (!_telegraphWasOn)
            {
                _activeTelegraphObject = Pick(preSlashMeshes, _pendingWindupCombo);
                if (_activeTelegraphObject != null)
                    _activeTelegraphObject.SetActive(true);
                _telegraphWasOn = true;
            }
        }
        else if (_telegraphWasOn)
        {
            if (_activeTelegraphObject != null)
            {
                _activeTelegraphObject.SetActive(false);
                _activeTelegraphObject = null;
            }
            _telegraphWasOn = false;
        }
    }

    // Меш тычка-замаха: тот же сигнал, что для остальных — IsTelegraphing,
    // но только когда форма хитбокса Capsule.
    void UpdateThrustMeshes()
    {
        if (_hitbox == null) return;
        bool windupOn = IsThrustShape() && _hitbox.IsTelegraphing;

        if (windupOn && !_thrustWindupWasOn)
        {
            _thrustWindupWasOn = true;
            if (thrustMesh != null)
                thrustMesh.SetActive(true);
        }
        else if (!windupOn && _thrustWindupWasOn)
        {
            _thrustWindupWasOn = false;
            if (thrustMesh != null)
                thrustMesh.SetActive(false);
        }

        if (_thrustAfterShowing)
        {
            _thrustAfterTimer -= Time.deltaTime;
            if (_thrustAfterTimer <= 0f)
            {
                _thrustAfterShowing = false;
                if (afterThrustMesh != null)
                    afterThrustMesh.SetActive(false);
            }
        }
    }

    void CancelThrustAfter()
    {
        if (_thrustAfterShowing)
        {
            _thrustAfterShowing = false;
            if (afterThrustMesh != null)
                afterThrustMesh.SetActive(false);
        }
    }

    // Вызывается при начале замаха (заряд + короткий замах перед ударом).
    public void ShowWindup(int comboIndex = 0)
    {
        _pendingWindupCombo = comboIndex;
        CancelThrustAfter();

        if (windupRenderer == null) return;
        windupRenderer.enabled = true;
        windupRenderer.transform.localScale = Vector3.one * arcRadius;
        windupRenderer.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    public void HideWindup()
    {
        if (windupRenderer != null)
            windupRenderer.enabled = false;
    }

    // Вызывается при самом ударе. comboIndex чередует сторону удара (0 = право, 1 = лево — зеркалим по X).
    public void ShowArc(Vector3 direction, Vector3 offset, float duration, float chargePercent = 0f, int comboIndex = 0)
    {
        HideWindup();
        CancelThrustAfter();

        if (arcRenderer == null) return;

        isShowingArc = true;
        arcDuration = duration;
        arcTimer = duration;
        _lastComboIndex = comboIndex;

        bool isLeft = comboIndex % 2 != 0;

        Vector3 origin = transform.parent != null
            ? transform.parent.position + offset
            : transform.position + offset;

        Vector3 sideDir = Vector3.Cross(Vector3.up, direction.normalized) * (isLeft ? -1f : 1f);
        arcRenderer.transform.position = origin + direction.normalized * (arcRadius * 0.8f) + sideDir * (arcRadius * 0.3f);
        arcRenderer.transform.position += Vector3.up * (arcHeight * 0.4f);

        float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        arcRenderer.transform.rotation = Quaternion.Euler(90f, angle, 0f);

        float scale = arcRadius * Mathf.Lerp(0.8f, 1.4f, chargePercent);
        arcRenderer.transform.localScale = new Vector3(isLeft ? -scale : scale, scale, 1f);

        arcRenderer.color = Color.Lerp(arcColor, Color.white, chargePercent * 0.5f);
        arcRenderer.enabled = true;

        _activeStrikeObject = Pick(slashMeshes, comboIndex);
        if (_activeStrikeObject != null)
            _activeStrikeObject.SetActive(true);

        if (_activeAfterObject != null)
        {
            _activeAfterObject.SetActive(false);
            _activeAfterObject = null;
        }
        isShowingAfter = false;

        flyDir = direction.normalized;
        arcStartPos = arcRenderer.transform.position;
    }

    public void HideArc()
    {
        isShowingArc = false;
        if (arcRenderer != null)
        {
            arcRenderer.enabled = false;
            arcRenderer.color = arcColor;
        }

        if (_activeStrikeObject != null)
        {
            _activeStrikeObject.SetActive(false);
            _activeStrikeObject = null;
        }

        // Для Capsule (тычок) ShowArc не вызывался — свой after-меш ведём отдельно.
        if (IsThrustShape())
        {
            if (afterThrustMesh != null)
            {
                afterThrustMesh.SetActive(true);
                _thrustAfterShowing = true;
                _thrustAfterTimer = afterObjectDuration;
            }
            return;
        }

        _activeAfterObject = Pick(afterSlashMeshes, _lastComboIndex);
        if (_activeAfterObject != null)
        {
            _activeAfterObject.SetActive(true);
            isShowingAfter = true;
            afterTimer = afterObjectDuration;
        }
    }
}