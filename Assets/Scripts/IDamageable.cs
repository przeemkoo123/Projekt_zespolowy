using UnityEngine;

/// <summary>
/// Interfejs dla wszystkich obiektów podatnych na obrażenia i odrzut (wrogowie, niszczalne obiekty otoczenia, gracz).
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Zadaje obrażenia obiektowi wraz z wektorem i siłą odrzutu (knockback).
    /// </summary>
    /// <param name="amount">Wartość zadanych obrażeń.</param>
    /// <param name="knockbackDirection">Znormalizowany kierunek odrzutu w przestrzeni świata 3D.</param>
    /// <param name="knockbackForce">Siła odrzutu.</param>
    void TakeDamage(float amount, Vector3 knockbackDirection, float knockbackForce);
}
