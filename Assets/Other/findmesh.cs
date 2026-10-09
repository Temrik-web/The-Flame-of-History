using UnityEngine;
using UnityEngine.AI;

public class FindNavMeshOwner : MonoBehaviour
{
    void Start()
    {
        // Находим все компоненты NavMeshAgent в сцене
        NavMeshAgent[] agents = FindObjectsOfType<NavMeshAgent>();

        if (agents.Length == 0)
        {
            Debug.LogWarning("В сцене не найдено ни одного NavMeshAgent.");
            return;
        }

        foreach (NavMeshAgent agent in agents)
        {
            // Получаем объект, которому принадлежит NavMesh, на котором стоит агент
            Object owner = agent.navMeshOwner;

            if (owner != null)
            {
                Debug.Log($"NavMesh для агента '{agent.gameObject.name}' принадлежит объекту: '{owner.name}'", owner);
            }
            else
            {
                Debug.Log($"NavMesh для агента '{agent.gameObject.name}' не имеет установленного владельца (owner).", agent);
            }
        }
    }
}