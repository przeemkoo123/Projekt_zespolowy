using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Komponent zarządzający punktami życia gracza, otrzymywaniem obrażeń,
/// odrzutem, animacjami zranienia i śmierci oraz komunikacją z UI (pasek HP i ekran Game Over).
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Zdrowie")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;
    private bool isDead = false;

    [Header("Referencje")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerCombat playerCombat;

    [Header("Efekt Zranienia (Flash)")]
    [SerializeField] private Color damageFlashColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private float damageFlashDuration = 0.15f;
    private Color originalSpriteColor = Color.white;
    private Coroutine flashRoutine;

    // Hashe Animatora
    private static readonly int AnimParamHurt = Animator.StringToHash("Hurt");
    private static readonly int AnimParamDeath = Animator.StringToHash("Death");
    private static readonly int AnimParamIsDead = Animator.StringToHash("IsDead");

    // Zdarzenia
    public event Action<float, float> OnHealthChanged; // (current, max)
    public event Action OnDied;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsDead => isDead;

    private void Awake()
    {
        currentHealth = maxHealth;
        InitializeReferences();
    }

    private void Start()
    {
        InitializeReferences();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void InitializeReferences()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            originalSpriteColor = spriteRenderer.color;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        if (playerCombat == null)
            playerCombat = GetComponent<PlayerCombat>();
    }

    /// <summary>
    /// Implementacja IDamageable. Zadaje obrażenia graczowi.
    /// </summary>
    public void TakeDamage(float amount, Vector3 knockbackDirection, float knockbackForce)
    {
        if (isDead) return;

        // Jeśli gracz jest w trakcie uniku (Dash) z i-frames, ignorujemy obrażenia
        if (playerMovement != null && playerMovement.IsInvulnerable)
        {
            return;
        }

        currentHealth -= amount;
        currentHealth = Mathf.Max(0f, currentHealth);

        // Powiadomienie UI o zmianie zdrowia
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        // Efekt zranienia (błysk na czerwono)
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        if (gameObject.activeInHierarchy)
        {
            flashRoutine = StartCoroutine(DamageFlashRoutine());
        }

        // Animacja zranienia
        if (animator != null && currentHealth > 0f)
        {
            animator.SetTrigger(AnimParamHurt);
        }

        // Odrzut gracza
        if (playerMovement != null && knockbackForce > 0f)
        {
            playerMovement.ApplyKnockback(knockbackDirection, knockbackForce);
        }

        // Sprawdzenie śmierci
        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private IEnumerator DamageFlashRoutine()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = damageFlashColor;
            yield return new WaitForSeconds(damageFlashDuration);
            spriteRenderer.color = originalSpriteColor;
        }
        flashRoutine = null;
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        // Blokada sterowania i ataku
        if (playerMovement != null) playerMovement.enabled = false;
        if (playerCombat != null) playerCombat.enabled = false;

        // Animacja śmierci
        if (animator != null)
        {
            animator.SetBool(AnimParamIsDead, true);
            animator.SetTrigger(AnimParamDeath);
        }

        // Wywołanie zdarzenia śmierci
        OnDied?.Invoke();

        // Powiadomienie menedżera gry / UI o końcu gry
        if (GameUIManager.Instance != null)
        {
            GameUIManager.Instance.ShowGameOver();
        }
    }

    /// <summary>
    /// Resetuje stan zdrowia gracza (np. przy restarcie gry).
    /// </summary>
    public void ResetHealth()
    {
        currentHealth = maxHealth;
        isDead = false;

        if (spriteRenderer != null)
            spriteRenderer.color = originalSpriteColor;

        if (animator != null)
        {
            animator.SetBool(AnimParamIsDead, false);
            animator.Play("Soldier_Idle", 0, 0f);
        }

        if (playerMovement != null) playerMovement.enabled = true;
        if (playerCombat != null) playerCombat.enabled = true;

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void Heal(float amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }
}
