using UnityEngine;

/// <summary>
/// Шаговая система. Добавляет небольшой импульс-толчок поверх базовой скорости.
/// Вызывай TryStart() когда есть input, Tick() каждый кадр.
/// </summary>
public class StepController
{
    private bool _active;
    private float _timer;
    private float _cooldownTimer;

    private float _impulse;   // пиковая добавка скорости от толчка
    private float _duration;
    private float _cooldown;

    public bool IsActive => _active;

    /// <summary>
    /// Пытается начать новый шаг. Параметры интерполируются между двумя режимами.
    /// </summary>
    public void TryStart(float currentSpeed, GaitConfig a, GaitConfig b)
    {
        if (_active || _cooldownTimer > 0f) return;

        float t = Mathf.InverseLerp(a.speed, b.speed, currentSpeed);
        _impulse = Mathf.Lerp(a.stepDistance, b.stepDistance, t);
        _duration = Mathf.Lerp(a.stepDuration, b.stepDuration, t);
        _cooldown = Mathf.Lerp(1f / a.stepFrequency, 1f / b.stepFrequency, t);

        _timer = 0f;
        _active = true;
    }

    /// <summary>
    /// Возвращает добавочную скорость шага в этот кадр (поверх базовой).
    /// </summary>
    public float Tick(float deltaTime)
    {
        if (_cooldownTimer > 0f)
            _cooldownTimer -= deltaTime;

        if (!_active) return 0f;

        _timer += deltaTime;
        float progress = _timer / _duration;

        if (progress >= 1f)
        {
            _active = false;
            _cooldownTimer = _cooldown;
            return 0f;
        }

        // Синусоида: нарастает и спадает за время шага
        float curve = Mathf.Sin(progress * Mathf.PI);
        return _impulse * curve;
    }

    /// <summary>
    /// Принудительно прерывает шаг (при манёвре).
    /// </summary>
    public void Cancel()
    {
        _active = false;
        _cooldownTimer = 0f;
    }
}
