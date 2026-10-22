using System.Text;
using System.Text.RegularExpressions;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Pointage de la présence en scannant le QR code de la carte de membre.
/// Le QR reste une fiche contact (vCard) lisible par tout téléphone ; la ligne UID
/// porte l'Id du membre. Les cartes imprimées avant l'UID sont reconnues par
/// matricule, e-mail, téléphone puis nom complet (seulement s'il n'y a qu'un membre).
/// </summary>
public partial class DataStore
{
    private static readonly Regex NoteMatricule = new(@"^Membre\s+(.+?)\s+—", RegexOptions.Compiled);

    /// <summary>Contenu du QR code de la carte de membre (vCard 3.0).</summary>
    public string VCardMembre(UserAccount user)
    {
        var code = string.IsNullOrWhiteSpace(user.Matricule) ? null : user.Matricule.Trim();
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCARD");
        sb.AppendLine("VERSION:3.0");
        sb.AppendLine("N:" + Vcf(user.Nom) + ";" + Vcf(user.Prenom) + ";;;");
        sb.AppendLine("FN:" + Vcf(user.NomComplet));
        sb.AppendLine("ORG:" + Vcf(Settings.AppName));
        if (!string.IsNullOrWhiteSpace(user.Telephone)) sb.AppendLine("TEL;TYPE=CELL:" + Vcf(user.Telephone));
        if (!string.IsNullOrWhiteSpace(user.Email)) sb.AppendLine("EMAIL:" + Vcf(user.Email));
        sb.AppendLine(code == null
            ? "NOTE:" + Vcf(user.Role)
            : "NOTE:Membre " + Vcf(code) + " — " + Vcf(user.Role));
        if (!string.IsNullOrWhiteSpace(user.Id)) sb.AppendLine("UID:" + Vcf(user.Id));
        sb.AppendLine("END:VCARD");
        return sb.ToString();
    }

    private static string Vcf(string? s)
        => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n").Trim();

    private static string VcfLire(string s)
        => s.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    /// <summary>Retrouve le membre d'un QR code scanné (carte de membre, ou texte : identifiant, matricule).</summary>
    public UserAccount? TrouverMembreParQr(string? texte)
    {
        var t = (texte ?? "").Trim();
        if (t.Length == 0) return null;

        if (!t.Contains("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
        {
            return _users.FirstOrDefault(u => u.Id == t)
                ?? FindUserByIdentifier(t)
                ?? Seul(_users.Where(u => u.AnciensIdentifiants.Contains(t, StringComparer.OrdinalIgnoreCase)))
                ?? Seul(_users.Where(u => MemeTexte(u.Matricule, t)));
        }

        var champs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ligne in t.Replace("\r", "").Split('\n'))
        {
            var deuxPoints = ligne.IndexOf(':');
            if (deuxPoints <= 0) continue;
            var cle = ligne[..deuxPoints].Split(';')[0].Trim();
            champs.TryAdd(cle, VcfLire(ligne[(deuxPoints + 1)..]));
        }

        if (champs.TryGetValue("UID", out var uid) && uid.Length > 0
            && _users.FirstOrDefault(u => u.Id == uid) is { } parId)
            return parId;
        if (champs.TryGetValue("NOTE", out var note) && NoteMatricule.Match(note) is { Success: true } m
            && Seul(_users.Where(u => MemeTexte(u.Matricule, m.Groups[1].Value))) is { } parMatricule)
            return parMatricule;
        if (champs.TryGetValue("EMAIL", out var email) && email.Length > 0
            && Seul(_users.Where(u => MemeTexte(u.Email, email))) is { } parEmail)
            return parEmail;
        if (champs.TryGetValue("TEL", out var tel) && FinTelephone(tel) is { } finTel
            && Seul(_users.Where(u => FinTelephone(u.Telephone) == finTel)) is { } parTel)
            return parTel;
        if (champs.TryGetValue("FN", out var nom) && nom.Length > 0)
            return Seul(_users.Where(u => MemeTexte(u.NomComplet, nom)));
        return null;
    }

    private static UserAccount? Seul(IEnumerable<UserAccount> candidats)
    {
        var liste = candidats.Take(2).ToList();
        return liste.Count == 1 ? liste[0] : null;
    }

    private static bool MemeTexte(string? a, string b)
        => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Les 9 derniers chiffres : « +261 34 12 345 67 » et « 034 12 345 67 » se valent.</summary>
    private static string? FinTelephone(string? tel)
    {
        var chiffres = new string((tel ?? "").Where(char.IsDigit).ToArray());
        return chiffres.Length < 6 ? null : chiffres.Length > 9 ? chiffres[^9..] : chiffres;
    }

    /// <summary>Pointe « présent » le membre dont le QR code vient d'être scanné.</summary>
    public PointageQr PointerPresenceParQr(string reunionId, string? texte)
    {
        var reunion = FindReunion(reunionId);
        if (reunion == null) return new PointageQr(false, L.T("Réunion introuvable."), null, false);
        var membre = TrouverMembreParQr(texte);
        if (membre == null)
            return new PointageQr(false, L.T("QR code non reconnu : ce n'est pas une carte de membre."), null, false);
        var cle = reunion.Participants.FirstOrDefault(p => string.Equals(p, membre.Identifiant, StringComparison.OrdinalIgnoreCase))
            ?? membre.Identifiant;
        if (reunion.Presences.TryGetValue(cle, out var statut) && statut == PresenceStatuts.Present)
            return new PointageQr(true, L.F("{0} est déjà pointé(e) présent(e).", membre.NomComplet), membre, true);
        var res = MarkPresence(reunionId, cle, PresenceStatuts.Present);
        return res.Success
            ? new PointageQr(true, L.F("{0} : présent(e).", membre.NomComplet), membre, false)
            : new PointageQr(false, res.Error, membre, false);
    }
}
