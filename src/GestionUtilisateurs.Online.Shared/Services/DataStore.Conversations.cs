using GestionUtilisateurs.Shared.Models;

namespace GestionUtilisateurs.Shared.Services;

/// <summary>
/// Discussions de groupe nommées (créées dans la Messagerie ou depuis un groupe et
/// ministère) et suppression d'une conversation.
/// </summary>
public partial class DataStore
{
    /// <summary>Préfixe des discussions de groupe nommées ; pour un groupe et ministère, suivi de son Id.</summary>
    public const string PrefixeGroupe = "grp:";

    /// <summary>Conversation dont l'identifiant se recalcule à partir des participants (duo ou groupe sans nom).</summary>
    private static bool EstConversationParParticipants(string conversationId)
        => conversationId != ConversationAnnonces && !conversationId.StartsWith(PrefixeGroupe, StringComparison.Ordinal);

    public static bool EstDiscussionDeGroupe(string conversationId)
        => conversationId.StartsWith(PrefixeGroupe, StringComparison.Ordinal);

    /// <summary>Groupe et ministère lié à la discussion, s'il existe encore.</summary>
    public Groupe? GroupeDeConversation(string conversationId)
        => EstDiscussionDeGroupe(conversationId) ? FindGroupe(conversationId[PrefixeGroupe.Length..]) : null;

    private static bool MasquePourMoi(Message m, string me)
        => me.Length > 0 && m.MasquePour.Any(x => x.Equals(me, StringComparison.OrdinalIgnoreCase));

    private static bool VisiblePour(Message m, string me)
        => !MasquePourMoi(m, me)
           && (m.ConversationId == ConversationAnnonces
               || m.Expediteur.Equals(me, StringComparison.OrdinalIgnoreCase)
               || m.Destinataires.Any(d => d.Equals(me, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Nom d'une discussion de groupe : celui du groupe et ministère s'il existe, sinon le dernier nom donné.</summary>
    public string NomDiscussion(string conversationId)
    {
        if (GroupeDeConversation(conversationId) is { } g) return g.Nom;
        return _messages.Where(m => m.ConversationId == conversationId && m.NomGroupe.Length > 0)
            .OrderByDescending(m => m.Date, StringComparer.Ordinal)
            .Select(m => m.NomGroupe).FirstOrDefault() ?? "";
    }

    /// <summary>Participants de la conversation (sans distinction de casse), membre connecté compris.</summary>
    public IReadOnlyList<string> ParticipantsConversation(string conversationId)
    {
        if (GroupeDeConversation(conversationId) is { } g)
            return ParticipantsGroupe(g);
        return _messages.Where(m => m.ConversationId == conversationId)
            .SelectMany(m => m.Destinataires.Append(m.Expediteur))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Crée une discussion de groupe nommée avec son premier message.</summary>
    public OperationResult CreerDiscussionDeGroupe(string nom, IEnumerable<string> membres, string texte)
    {
        if (!Can(Permissions.MessagerieUse)) return OperationResult.Fail(L.T("Votre rôle ne permet pas d'utiliser la messagerie."));
        nom = (nom ?? "").Trim();
        if (nom.Length == 0) return OperationResult.Fail(L.T("Donnez un nom au groupe."));
        var me = MonIdentifiant;
        var autres = (membres ?? Enumerable.Empty<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0 && !MemeId(x, me))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (autres.Count < 2) return OperationResult.Fail(L.T("Choisissez au moins deux membres pour un groupe."));
        var existe = GetConversations(me).Any(c => CleDoublon(NomDiscussion(c.ConversationId)) == CleDoublon(nom));
        if (existe) return OperationResult.Fail(L.F("Une discussion « {0} » existe déjà : écrivez-y directement.", nom));
        return SendMessage(autres, texte, PrefixeGroupe + Guid.NewGuid().ToString("N"), nom);
    }

    /// <summary>
    /// Une conversation à plusieurs (au moins trois personnes) peut recevoir un nom ou
    /// changer de nom ; celle d'un groupe et ministère suit le nom du groupe.
    /// </summary>
    public bool PeutRenommerConversation(string conversationId)
        => conversationId != ConversationAnnonces
           && GroupeDeConversation(conversationId) == null
           && ParticipantsConversation(conversationId).Count >= 3
           && ParticipantsConversation(conversationId).Any(p => MemeId(p, MonIdentifiant));

    /// <summary>Renomme une conversation à plusieurs ; un nom vide la remet sans nom (sauf discussion de groupe).</summary>
    public OperationResult RenommerConversation(string conversationId, string nom)
    {
        if (GroupeDeConversation(conversationId) is { } g)
            return OperationResult.Fail(L.F("Cette discussion porte le nom du groupe « {0} » : renommez le groupe dans « Groupes et ministères ».", g.Nom));
        if (!PeutRenommerConversation(conversationId))
            return OperationResult.Fail(L.T("Seule une conversation à plusieurs peut recevoir un nom."));
        nom = (nom ?? "").Trim();
        if (nom.Length == 0 && EstDiscussionDeGroupe(conversationId)) return OperationResult.Fail(L.T("Donnez un nom au groupe."));
        if (nom.Length > 80) return OperationResult.Fail(L.T("Le nom ne doit pas dépasser 80 caractères."));
        var me = MonIdentifiant;
        if (nom.Length > 0 && GetConversations(me).Any(c => c.ConversationId != conversationId
                && CleDoublon(NomDiscussion(c.ConversationId)) == CleDoublon(nom)))
            return OperationResult.Fail(L.F("Une discussion « {0} » existe déjà : écrivez-y directement.", nom));
        var ancien = NomDiscussion(conversationId);
        foreach (var m in _messages.Where(m => m.ConversationId == conversationId)) m.NomGroupe = nom;
        SaveMessages();
        LogActivity(ActivityTypes.MessageUpdate, nom.Length == 0
            ? "Conversation « " + ancien + " » remise sans nom"
            : ancien.Length == 0 ? "Conversation nommée « " + nom + " »" : "Conversation « " + ancien + " » renommée « " + nom + " »");
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }

    /// <summary>Répond dans une conversation existante : mêmes participants, même nom de groupe.</summary>
    public OperationResult RepondreConversation(string conversationId, string texte)
    {
        if (conversationId == ConversationAnnonces) return SendMessage(new List<string>(), texte);
        var participants = ParticipantsConversation(conversationId).ToList();
        if (EstDiscussionDeGroupe(conversationId))
            return SendMessage(participants, texte, conversationId, NomDiscussion(conversationId));
        return SendMessage(participants, texte);
    }

    /// <summary>
    /// Supprime une conversation. Pour soi : elle disparaît de sa liste et revient
    /// seulement avec les messages suivants. Pour tout le monde (administrateur) :
    /// ses messages sont effacés.
    /// </summary>
    public OperationResult SupprimerConversation(string conversationId, bool pourTous = false)
    {
        var me = MonIdentifiant;
        var messages = _messages.Where(m => m.ConversationId == conversationId).ToList();
        if (messages.Count == 0 || !messages.Any(m => VisiblePour(m, me)))
            return OperationResult.Fail(L.T("Conversation introuvable."));
        var titre = conversationId == ConversationAnnonces ? "Annonces"
            : EstDiscussionDeGroupe(conversationId) ? NomDiscussion(conversationId)
            : string.Join(", ", ParticipantsConversation(conversationId));
        if (pourTous)
        {
            if (!IsAdmin) return OperationResult.Fail(L.T("Seul un administrateur peut supprimer une conversation pour tout le monde."));
            _messages.RemoveAll(m => m.ConversationId == conversationId);
            LogActivity(ActivityTypes.MessageDelete, "Conversation « " + titre + " » supprimée pour tout le monde (" + messages.Count + " message(s))");
        }
        else
        {
            foreach (var m in messages.Where(m => !MasquePourMoi(m, me))) m.MasquePour.Add(me);
            LogActivity(ActivityTypes.MessageDelete, "Conversation « " + titre + " » retirée de sa liste");
        }
        SaveMessages();
        DataChanged?.Invoke();
        return OperationResult.Ok();
    }
}
