using UnityEngine;

/// <summary>
/// Przykładowy komponent implementujący IDamageable dla przeciwników, manekinów treningowych lub niszczalnych obiektów.
/// Obsługuje redukcję punktów życia oraz fizyczny odrzut (knockback).
/// </summary>
[DisallowMultipleComponent]
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Zdrowie")]
    [SerializeField] private float maxHealth = 60f;
    private float currentHealth;

    [Header("Fizyka i Odrzut")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CharacterController characterController;

    private void Awake()
    {
        currentHealth = maxHealth;
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
    }

    public void TakeDamage(float amount, Vector3 knockbackDirection, float knockbackForce)
    {
        currentHealth -= amount;
        Debug.Log($"[EnemyHealth] {gameObject.name} otrzymał {amount:F1} obrażeń! Pozostałe HP: {Mathf.Max(0, currentHealth):F1}");

        // Aplikacja odrzutu przez Rigidbody (jeśli obiekt posiada fizykę 3D)
        if (rb != null && !rb.isKinematic)
        {
            rb.AddForce(knockbackDirection * knockbackForce, ForceMode.Impulse);
        }
        else if (characterController != null && characterController.enabled)
        {
            // Opcjonalne przesunięcie dla obiektów z CharacterController
            characterController.Move(knockbackDirection * (knockbackForce * 0.05f));
        }

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log($"[EnemyHealth] {gameObject.name} został zniszczony/pokonany!");
        Destroy(gameObject);
    }
}
