namespace GestionUtilisateurs.Shared.Models;

/// <summary>
/// Comptes créés sans adresse e-mail (script SQL 0005) : le serveur leur donne
/// une adresse technique « m-…@sans-email.invalid » qui ne reçoit rien. Elle ne
/// doit jamais être affichée ni recopiée dans la fiche du membre.
/// </summary>
public static class SansEmail
{
    public const string Domaine = "sans-email.invalid";

    public static bool EstTechnique(string? email)
        => (email ?? "").Trim().EndsWith("@" + Domaine, StringComparison.OrdinalIgnoreCase);

    /// <summary>L'e-mail du membre, ou une chaîne vide pour une adresse technique.</summary>
    public static string Reelle(string? email) => EstTechnique(email) ? "" : (email ?? "").Trim();
}
