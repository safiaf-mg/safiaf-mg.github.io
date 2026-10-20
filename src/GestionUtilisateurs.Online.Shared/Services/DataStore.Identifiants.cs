using System.Text.Json.Nodes;
using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Identifiants des membres construits sur le prénom : « lucas », « jean.pierre »
/// pour deux prénoms, puis « lucas1 », « lucas2 »… pour les homonymes, dans l'ordre
/// d'inscription. Les identifiants existants peuvent être mis à la règle : toutes
/// les données qui les citent suivent.
/// </summary>
public partial class DataStore
{
    /// <summary>Premier identifiant libre : la base, puis base1, base2…</summary>
    public static string IdentifiantLibre(string baseId, Func<string, bool> pris)
    {
        if (!pris(baseId)) return baseId;
        for (var i = 1; ; i++)
            if (!pris(baseId + i)) return baseId + i;
    }

    private string IdentifiantLibre(string baseId)
        => IdentifiantLibre(baseId, id => _users.Any(u => u.Identifiant.Equals(id, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Identifiants qui ne suivent pas la règle du prénom, avec celui qu'ils recevraient.</summary>
    public IReadOnlyList<(UserAccount Membre, string Nouveau)> IdentifiantsARenommer()
    {
        var pris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Le compte technique de la base garde son identifiant (il n'a pas de prénom).
        foreach (var u in _users.Where(u => u.Role == Roles.BaseAdmin)) pris.Add(u.Identifiant);
        var plan = new List<(UserAccount, string)>();
        foreach (var u in _users.Where(u => u.Role != Roles.BaseAdmin)
                     .OrderBy(u => u.DateCreation ?? "", StringComparer.Ordinal)
                     .ThenBy(u => u.Id, StringComparer.Ordinal))
        {
            var nouveau = IdentifiantLibre(GenerateIdentifiant(u.Prenom, u.Nom), pris.Contains);
            pris.Add(nouveau);
            if (!nouveau.Equals(u.Identifiant, StringComparison.Ordinal)) plan.Add((u, nouveau));
        }
        return plan;
    }

    /// <summary>
    /// Met tous les identifiants à la règle du prénom. Les références (famille,
    /// cotisations, prêts, tâches, messages, réunions, votes, documents, événements,
    /// auteurs…) sont réécrites en une passe ; l'historique d'activité n'est pas modifié.
    /// </summary>
    public OperationResult AppliquerIdentifiantsParPrenom()
    {
        if (!IsAdmin) return OperationResult.Fail(L.T("Action réservée aux administrateurs."));
        var plan = IdentifiantsARenommer();
        if (plan.Count == 0) return OperationResult.Ok();

        var correspondance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (membre, nouveau) in plan)
        {
            var ancien = (membre.Identifiant ?? "").Trim();
            if (ancien.Length > 0) correspondance[ancien] = nouveau;
        }

        // Chaque ancien identifiant est remplacé une seule fois, tous en même temps :
        // un échange (lucas → lucas1 pendant que lucas.ran → lucas) reste correct.
        RemplacerListe(_users, correspondance);
        RemplacerListe(_events, correspondance);
        RemplacerListe(_documents, correspondance);
        RemplacerListe(_folders, correspondance);
        RemplacerListe(_messages, correspondance);
        RemplacerListe(_taches, correspondance);
        RemplacerListe(_cotisations, correspondance);
        RemplacerListe(_tresorerie, correspondance);
        RemplacerListe(_materiels, correspondance);
        RemplacerListe(_prets, correspondance);
        RemplacerListe(_reunions, correspondance);
        RemplacerListe(_situations, correspondance);
        RemplacerListe(_votes, correspondance);
        RemplacerListe(_journalMedia, correspondance);
        RemplacerListe(_groupes, correspondance);
        RemplacerListe(_services, correspondance);
        RemplacerListe(_prieres, correspondance);
        RemplacerListe(_prieresPrivees, correspondance);
        _community = Remplacer(_community, correspondance) ?? _community;
        _settings = Remplacer(_settings, correspondance) ?? _settings;

        foreach (var m in _messages.Where(m => m.ConversationId != ConversationAnnonces))
            m.ConversationId = ConversationIdFor(m.Destinataires.Append(m.Expediteur));

        // Les préférences de chaque membre (langue, thème) sont rangées dans son
        // navigateur sous son identifiant : on garde l'ancien pour les retrouver.
        foreach (var u in _users)
            foreach (var (ancien, nouveau) in correspondance)
                if (u.Identifiant.Equals(nouveau, StringComparison.OrdinalIgnoreCase)
                    && !u.AnciensIdentifiants.Contains(ancien, StringComparer.OrdinalIgnoreCase))
                    u.AnciensIdentifiants.Add(ancien);

        SaveUsers();
        SaveEvents();
        SaveDocuments();
        SaveFolders();
        SaveMessages();
        SaveTaches();
        SaveCotisations();
        SaveTresorerie();
        SaveMateriels();
        SavePrets();
        SaveReunions();
        SaveSituations();
        SaveVotes();
        SaveJournalMedia();
        SaveGroupes();
        SaveServices();
        SavePrieres();
        SavePrieresPrivees();
        SaveCommunity();
        SaveSettings();
        LogActivity(ActivityTypes.Update, "Identifiants mis à la règle du prénom (" + plan.Count + ")");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    private void RemplacerListe<T>(List<T> liste, IReadOnlyDictionary<string, string> correspondance)
    {
        var copie = Remplacer<List<T>>(liste, correspondance);
        if (copie == null) return;
        liste.Clear();
        liste.AddRange(copie);
    }

    private T? Remplacer<T>(T valeur, IReadOnlyDictionary<string, string> correspondance)
    {
        var noeud = JsonNode.Parse(Serialize(valeur));
        if (noeud == null) return default;
        noeud = RemplacerNoeud(noeud, correspondance);
        return Deserialize<T>(noeud?.ToJsonString());
    }

    /// <summary>Champs (en JSON) qui contiennent des identifiants de membres. Les autres
    /// textes ne sont jamais touchés : un prénom « Lucas » n'est pas l'identifiant « lucas ».</summary>
    private static readonly HashSet<string> ChampsIdentifiant = new(StringComparer.OrdinalIgnoreCase)
    {
        "identifiant", "membreIdentifiant", "votantIdentifiant", "assignes", "expediteur",
        "destinataires", "luPar", "supprimePar", "participants", "personneConcernee",
        "personnesConcernees", "createdBy", "modifiePar", "deletedBy", "responsable",
        "utilisateurId", "guideVu"
    };

    /// <summary>Dictionnaires dont les clés sont des identifiants de membres.</summary>
    private static readonly HashSet<string> DictionnairesParMembre = new(StringComparer.OrdinalIgnoreCase)
        { "presences", "themesUtilisateurs", "conseilsUtilisateurs" };

    private static JsonNode? RemplacerNoeud(JsonNode? noeud, IReadOnlyDictionary<string, string> correspondance, string? champ = null)
    {
        switch (noeud)
        {
            case JsonObject objet:
                foreach (var (cle, enfant) in objet.ToList())
                {
                    var nouvelEnfant = RemplacerNoeud(enfant, correspondance, cle);
                    if (!ReferenceEquals(nouvelEnfant, enfant)) objet[cle] = nouvelEnfant;
                }
                if (champ == null || !DictionnairesParMembre.Contains(champ)) return objet;
                foreach (var cle in objet.Select(p => p.Key).ToList())
                {
                    if (!correspondance.TryGetValue(cle.Trim(), out var nouvelleCle) || objet.ContainsKey(nouvelleCle)) continue;
                    var v = objet[cle];
                    objet.Remove(cle);
                    objet[nouvelleCle] = v;
                }
                return objet;
            case JsonArray tableau:
                for (var i = 0; i < tableau.Count; i++)
                {
                    var nouvelElement = RemplacerNoeud(tableau[i], correspondance, champ);
                    if (!ReferenceEquals(nouvelElement, tableau[i])) tableau[i] = nouvelElement;
                }
                return tableau;
            case JsonValue valeur when champ != null && ChampsIdentifiant.Contains(champ)
                                        && valeur.TryGetValue<string>(out var texte)
                                        && correspondance.TryGetValue(texte.Trim(), out var nouveau):
                return JsonValue.Create(nouveau);
            default:
                return noeud;
        }
    }
}
