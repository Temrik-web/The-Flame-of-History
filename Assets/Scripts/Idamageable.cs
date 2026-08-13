using UnityEngine;

/// <summary>
/// Общий интерфейс для всего, что может получать урон (враг, игрок, разрушаемые объекты).
/// EnemyAI стреляет рейкастом и ищет этот интерфейс на том, во что попал.
/// Enemy.cs его уже реализует. Для игрока реализуй его в своём health-скрипте
/// (см. пример PlayerHealth.cs).
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage, Vector3 attackerPosition);
}