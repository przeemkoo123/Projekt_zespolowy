using UnityEngine;

/// <summary>
/// Współdzielona klasa pomocnicza do uruchamiania animacji śmierci wrogów w Animatorze
/// (z bezpiecznym fallbackiem przechylenia) oraz płynnego wygaszania sprite'a przed usunięciem obiektu.
/// </summary>
public static class DeathAnimationHelper
{
    private static readonly int AnimParamSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimParamIsDead = Animator.StringToHash("IsDead");
    private static readonly int AnimParamDie = Animator.StringToHash("Die");
    private static readonly int AnimStateDeath = Animator.StringToHash("Death");

    /// <summary>
    /// Resetuje wskazane triggery, ustawia parametry śmierci (IsDead / Die) i odtwarza stan Death od klatki 0.
    /// Zwraca true, jeśli Animator posiada stan "Death" i prawidłowy kontroler.
    /// </summary>
    public static bool TryTriggerDeathState(Animator animator, params int[] triggersToReset)
    {
        if (animator == null) return false;

        if (!animator.enabled)
        {
            animator.enabled = true;
        }

        if (triggersToReset != null)
        {
            for (int i = 0; i < triggersToReset.Length; i++)
            {
                animator.ResetTrigger(triggersToReset[i]);
            }
        }

        animator.SetFloat(AnimParamSpeed, 0f);
        animator.SetBool(AnimParamIsDead, true);
        animator.SetTrigger(AnimParamDie);

        if (animator.runtimeAnimatorController != null &&
            animator.isActiveAndEnabled &&
            animator.HasState(0, AnimStateDeath))
        {
            animator.Play(AnimStateDeath, 0, 0f);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Wyłącza komponent Billboard, gdy używana jest procedura awaryjna bez klipu animacji śmierci.
    /// </summary>
    public static void PrepareFallbackVisual(SpriteRenderer spriteRenderer, Transform rootTransform, bool hasDeathClip)
    {
        if (hasDeathClip) return;

        Transform visual = spriteRenderer != null ? spriteRenderer.transform : rootTransform;
        if (visual == null) return;

        Billboard billboard = visual.GetComponent<Billboard>();
        if (billboard != null)
        {
            billboard.enabled = false;
        }
    }

    /// <summary>
    /// Aktualizuje wygląd sprite'a w trakcie sekwencji śmierci: opcjonalny przewrót (brak klipu),
    /// krótki błysk trafienia oraz płynne zanikanie kanału alpha pod koniec.
    /// </summary>
    public static void UpdateVisualDeathFrame(
        SpriteRenderer spriteRenderer,
        Transform rootTransform,
        Vector3 startVisualScale,
        Color baseColor,
        Color flashColor,
        bool hasDeathClip,
        float elapsed,
        float flashDuration,
        float fadeStartTime,
        float fadeDuration)
    {
        if (!hasDeathClip)
        {
            Transform visual = spriteRenderer != null ? spriteRenderer.transform : rootTransform;
            if (visual != null)
            {
                float fallT = Mathf.Clamp01(elapsed / 0.35f);
                visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -85f, fallT));
                visual.localScale = new Vector3(
                    startVisualScale.x,
                    Mathf.Lerp(startVisualScale.y, startVisualScale.y * 0.8f, fallT),
                    startVisualScale.z);
            }
        }

        if (spriteRenderer == null) return;

        if (elapsed >= fadeStartTime && fadeDuration > 0f)
        {
            float fadeT = Mathf.Clamp01((elapsed - fadeStartTime) / fadeDuration);
            Color c = baseColor;
            c.a = Mathf.Lerp(baseColor.a, 0f, fadeT);
            spriteRenderer.color = c;
        }
        else if (elapsed >= flashDuration)
        {
            spriteRenderer.color = baseColor;
        }
        else
        {
            spriteRenderer.color = flashColor;
        }
    }
}
