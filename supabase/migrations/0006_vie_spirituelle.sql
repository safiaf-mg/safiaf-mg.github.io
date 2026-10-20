-- Vie spirituelle : groupes et ministères, planning des services et
-- intentions de prière.
--
-- Ajoute les règles de lecture et d'écriture des nouvelles collections
-- (groupes, services, intentions partagées, intentions privées) et deux
-- fonctions pour que chaque membre confie une intention privée et retrouve
-- les siennes, sans jamais lire celles des autres.
--
-- À exécuter une fois dans Supabase : SQL Editor > New query > coller > Run.
-- Le script peut être relancé sans danger.

create or replace function public.gu_key_rule(p_key text, out read_perm text, out write_perm text)
language sql immutable as $$
    select r.read_perm, r.write_perm from (values
        ('gu2:users',         'tous',              'users.manage'),
        ('gu2:fields',        'tous',              'fields.manage'),
        ('gu2:activity',      'activity.view',     'tous'),
        ('gu2:settings',      'tous',              'gestion'),
        ('gu2:archive',       'archive.view',      'users.manage|archive.view'),
        ('gu2:events',        'tous',              'calendar.use'),
        ('gu2:documents',     'documents.use',     'documents.use'),
        ('gu2:folders',       'documents.use',     'documents.use'),
        ('gu2:community',     'tous',              'community.use'),
        ('gu2:permissions',   'tous',              'roles.manage'),
        ('gu2:messages',      'messagerie.use',    'messagerie.use'),
        ('gu2:taches',        'taches.use',        'taches.use'),
        ('gu2:cotisations',   'finances.manage',   'finances.manage'),
        ('gu2:tresorerie',    'finances.manage',   'finances.manage'),
        ('gu2:materiels',     'tous',              'materiel.manage'),
        ('gu2:prets',         'tous',              'materiel.manage'),
        ('gu2:reunions',      'tous',              'presence.manage'),
        ('gu2:situations',    'situations.manage', 'situations.manage'),
        ('gu2:votes',         'tous',              'tous'),
        ('gu2:journal_media', 'journal_media.use', 'journal_media.use'),
        ('gu2:groupes',       'tous',              'groupes.manage'),
        ('gu2:services',      'tous',              'tous'),
        ('gu2:prieres',       'prieres.use|prieres.manage', 'prieres.use|prieres.manage'),
        ('gu2:prieres_privees', 'prieres.manage',  'prieres.manage')
    ) as r(key, read_perm, write_perm)
    where r.key = p_key;
$$;

-- ---------------------------------------------------------------------
-- Intentions de prière privées (0006) : le membre les confie sans pouvoir
-- lire celles des autres ; seuls les responsables (« prieres.manage ») les lisent.
-- ---------------------------------------------------------------------

create or replace function public.deposer_intention_privee(p_texte text, p_anonyme boolean)
returns void language plpgsql security definer set search_path = public as $$
declare
    v_texte  text := trim(coalesce(p_texte, ''));
    v_nom    text;
    v_liste  jsonb;
    v_recent int;
begin
    if not public.gu_can('prieres.use|prieres.manage') then
        raise exception 'Votre rôle ne permet pas de confier une intention de prière.';
    end if;
    if v_texte = '' then
        raise exception 'Écrivez votre intention de prière.';
    end if;
    if length(v_texte) > 2000 then
        raise exception 'L''intention ne doit pas dépasser 2 000 caractères.';
    end if;
    select trim(coalesce(prenom, '') || ' ' || coalesce(nom, '')) into v_nom from public.profiles where id = auth.uid();

    select value into v_liste from public.app_data where key = 'gu2:prieres_privees' for update;
    v_liste := coalesce(v_liste, '[]'::jsonb);
    -- Garde-fou : 10 intentions privées par heure et par membre.
    select count(*) into v_recent from jsonb_array_elements(v_liste) e
     where e ->> 'auteurUid' = auth.uid()::text
       and (e ->> 'date')::timestamptz > now() - interval '1 hour';
    if v_recent >= 10 then
        raise exception 'trop_de_demandes';
    end if;

    v_liste := v_liste || jsonb_build_array(jsonb_build_object(
        'id', replace(gen_random_uuid()::text, '-', ''),
        'texte', v_texte,
        'auteur', '',
        'auteurNom', coalesce(v_nom, ''),
        'auteurUid', auth.uid()::text,
        'anonyme', coalesce(p_anonyme, false),
        'date', to_char(now() at time zone 'utc', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
        'prieurs', '[]'::jsonb,
        'exaucee', false,
        'dateExaucee', '',
        'temoignage', '',
        'suivi', ''));
    if pg_column_size(v_liste) > 5 * 1024 * 1024 then
        raise exception 'Données trop volumineuses (plus de 5 Mo).';
    end if;

    insert into public.app_data as a (key, value, version, updated_at, updated_by)
    values ('gu2:prieres_privees', v_liste, 1, now(), auth.uid())
    on conflict (key) do update
       set value = excluded.value, version = a.version + 1, updated_at = now(), updated_by = auth.uid();
end;
$$;

revoke execute on function public.deposer_intention_privee(text, boolean) from public;
grant execute on function public.deposer_intention_privee(text, boolean) to authenticated;

-- Les intentions privées du membre connecté (sans la note de suivi des responsables).
create or replace function public.mes_intentions_privees()
returns jsonb language sql stable security definer set search_path = public as $$
    select coalesce(jsonb_agg(e - 'suivi' - 'auteurUid' order by e ->> 'date' desc), '[]'::jsonb)
      from public.app_data d, jsonb_array_elements(d.value) e
     where d.key = 'gu2:prieres_privees'
       and public.gu_role() is not null
       and e ->> 'auteurUid' = auth.uid()::text;
$$;

revoke execute on function public.mes_intentions_privees() from public;
grant execute on function public.mes_intentions_privees() to authenticated;
