using System.Collections;
using UnityEngine;

/// <summary>
/// Przykładowy komponent implementujący IDamageable dla przeciwników, manekinów treningowych lub niszczalnych obiektów.
/// Obsługuje redukcję punktów życia, fizyczny odrzut (knockback) oraz animację śmierci przed usunięciem obiektu.
/// </summary>
[DisallowMultipleComponent]
public class EnemyHealth : MonoBehaviour, IDamageable
{
    [Header("Zdrowie")]
    [SerializeField] private float maxHealth = 60f;
    private float currentHealth;
    private bool isDead = false;

    [Header("Fizyka i Odrzut")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CharacterController characterController;

    [Header("Animacja Śmierci")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float despawnDelay = 1.0f;

    private Color originalColor = Color.white;

    private void Awake()
    {
        currentHealth = maxHealth;
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null) originalColor = spriteRenderer.color;
    }

    public void TakeDamage(float amount, Vector3 knockbackDirection, float knockbackForce)
    {
        if (isDead) return;

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
        if (isDead) return;
        isDead = true;

        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        bool hasDeathClip = DeathAnimationHelper.TryTriggerDeathState(animator);

        Debug.Log($"[EnemyHealth] {gameObject.name} został zniszczony/pokonany!");
        StartCoroutine(DeathAnimationRoutine(hasDeathClip));
    }

    private IEnumerator DeathAnimationRoutine(bool hasDeathClip)
    {
        float totalDuration = Mathf.Max(despawnDelay, 0.75f);
        float fadeDuration = Mathf.Min(0.4f, totalDuration * 0.45f);
        float fadeStart = totalDuration - fadeDuration;

        Transform visual = spriteRenderer != null ? spriteRenderer.transform : transform;
        Vector3 startScale = visual.localScale;
        DeathAnimationHelper.PrepareFallbackVisual(spriteRenderer, transform, hasDeathClip);

        Color flashColor = new Color(1f, 0.25f, 0.25f, originalColor.a);
        float elapsed = 0f;
        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;
            DeathAnimationHelper.UpdateVisualDeathFrame(
                spriteRenderer, transform, startScale, originalColor, flashColor,
                hasDeathClip, elapsed, 0.08f, fadeStart, fadeDuration);
            yield return null;
        }

        Destroy(gameObject);
    }
}
