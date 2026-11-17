using UnityEngine;

/// <summary>
/// Przechowuje i udostępnia statystyki gracza (np. bazowe obrażenia miecza, zdrowie, mnożniki).
/// PlayerCombat pobiera stąd wartość bazowych obrażeń przed nałożeniem mnożnika combo.
/// </summary>
[DisallowMultipleComponent]
public class PlayerStats : MonoBehaviour
{
    [Header("Atak i Obrażenia")]
    [Tooltip("Bazowa wartość obrażeń zadawanych mieczem przy pierwszym ataku combo.")]
    [SerializeField] private float baseDamage = 25f;

    [Tooltip("Globalny mnożnik obrażeń gracza (np. z mikstur, buffów lub ekwipunku).")]
    [SerializeField] private float damageMultiplier = 1.0f;

    [Header("Życie (Opcjonalne)")]
    [Tooltip("Maksymalne punkty zdrowia gracza.")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    public float BaseDamage
    {
        get => baseDamage;
        set => baseDamage = Mathf.Max(0f, value);
    }

    public float DamageMultiplier
    {
        get => damageMultiplier;
        set => damageMultiplier = Mathf.Max(0f, value);
    }

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    /// <summary>
    /// Zwraca ostateczną wartość bazowych obrażeń z uwzględnieniem mnożnika.
    /// </summary>
    public float GetCalculatedDamage()
    {
        return baseDamage * damageMultiplier;
    }
}
