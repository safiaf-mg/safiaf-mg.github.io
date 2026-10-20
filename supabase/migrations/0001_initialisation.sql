-- =====================================================================
-- Gestion communautaire — version en ligne
-- Schéma Supabase : comptes personnels, rôles et droits appliqués côté
-- serveur, données partagées, fichiers, surveillance de la capacité.
--
-- À exécuter une seule fois dans Supabase : SQL Editor > New query >
-- coller ce fichier > Run. Le script peut être relancé sans danger.
-- =====================================================================

create extension if not exists pgcrypto with schema extensions;

-- ---------------------------------------------------------------------
-- Tables
-- ---------------------------------------------------------------------

-- Un profil par compte Supabase. Le statut « en_attente » bloque l'accès
-- tant qu'un administrateur n'a pas validé le compte.
create table if not exists public.profiles (
    id            uuid primary key references auth.users (id) on delete cascade,
    email         text not null default '',
    prenom        text not null default '',
    nom           text not null default '',
    telephone     text not null default '',
    role          text not null default 'Utilisateur'
                  check (role in ('Administrateur de la base', 'Administrateur', 'Gestionnaire', 'Utilisateur')),
    statut        text not null default 'en_attente'
                  check (statut in ('en_attente', 'actif', 'refuse', 'desactive')),
    created_at    timestamptz not null default now(),
    validated_at  timestamptz,
    validated_by  uuid,
    last_seen_at  timestamptz,
    mdp_demande_at timestamptz,                          -- mot de passe oublié : demande en attente
    mdp_provisoire boolean not null default false        -- mot de passe donné par un administrateur
);

-- Données de l'application, une ligne par collection (membres, calendrier,
-- documents…). Les fichiers sont remplacés par des références « gufile: »
-- vers le stockage Supabase pour garder la base légère.
create table if not exists public.app_data (
    key         text primary key,
    value       jsonb not null,
    version     bigint not null default 1,
    updated_at  timestamptz not null default now(),
    updated_by  uuid
);

-- Réglages de l'alerte de saturation (une seule ligne).
create table if not exists public.capacity_settings (
    id                   int primary key default 1 check (id = 1),
    warning_pct          int not null default 70,
    critical_pct         int not null default 90,
    db_limit_bytes       bigint not null default 524288000,    -- 500 Mo (offre gratuite)
    storage_limit_bytes  bigint not null default 1073741824,   -- 1 Go (offre gratuite)
    updated_at           timestamptz not null default now(),
    updated_by           uuid
);
insert into public.capacity_settings (id) values (1) on conflict (id) do nothing;

-- Mesures quotidiennes de l'espace utilisé (historique et projection).
create table if not exists public.capacity_snapshots (
    id               bigserial primary key,
    taken_at         timestamptz not null default now(),
    db_bytes         bigint not null,
    storage_bytes    bigint not null,
    storage_files    int not null,
    members_active   int not null,
    members_pending  int not null,
    source           text not null default 'app'
);
create index if not exists capacity_snapshots_taken_at on public.capacity_snapshots (taken_at desc);

-- Alertes adressées aux administrateurs.
create table if not exists public.admin_alerts (
    id               bigserial primary key,
    created_at       timestamptz not null default now(),
    level            text not null check (level in ('attention', 'critique')),
    metric           text not null check (metric in ('base', 'fichiers')),
    percent          numeric(5, 1) not null,
    message          text not null,
    acknowledged_at  timestamptz,
    acknowledged_by  uuid
);

-- Accès uniquement par les fonctions ci-dessous (aucun accès direct).
alter table public.profiles           enable row level security;
alter table public.app_data           enable row level security;
alter table public.capacity_settings  enable row level security;
alter table public.capacity_snapshots enable row level security;
alter table public.admin_alerts       enable row level security;

revoke all on public.profiles, public.app_data, public.capacity_settings,
              public.capacity_snapshots, public.admin_alerts from anon, authenticated;

drop policy if exists profiles_self_read on public.profiles;
create policy profiles_self_read on public.profiles for select to authenticated using (id = auth.uid());
grant select on public.profiles to authenticated;

-- ---------------------------------------------------------------------
-- Rôles et droits
-- ---------------------------------------------------------------------

-- Rôle du compte connecté, uniquement s'il est actif (sinon null).
create or replace function public.gu_role()
returns text language sql stable security definer set search_path = public as $$
    select role from public.profiles where id = auth.uid() and statut = 'actif';
$$;

create or replace function public.gu_is_admin()
returns boolean language sql stable security definer set search_path = public as $$
    select coalesce(public.gu_role() in ('Administrateur', 'Administrateur de la base'), false);
$$;

-- Appel fait avec la clé « service_role » (tâches GitHub planifiées).
create or replace function public.gu_is_service()
returns boolean language sql stable as $$
    select coalesce(current_setting('request.jwt.claims', true)::jsonb ->> 'role', '') = 'service_role';
$$;

-- Le compte connecté a-t-il l'un des droits demandés ?
-- « tous » = tout membre actif ; « admins » = administrateurs ;
-- « gestion » = administrateurs et gestionnaires ; sinon une clé de la
-- matrice des permissions (modifiable dans l'application) ; plusieurs
-- droits séparés par « | ».
create or replace function public.gu_can(p_perms text)
returns boolean language plpgsql stable security definer set search_path = public as $$
declare
    v_role   text := public.gu_role();
    v_perm   text;
    v_matrix jsonb;
    v_set    jsonb;
begin
    if v_role is null or p_perms is null then return false; end if;
    if v_role = 'Administrateur de la base' then return true; end if;
    select value into v_matrix from public.app_data where key = 'gu2:permissions';
    foreach v_perm in array string_to_array(p_perms, '|') loop
        v_perm := trim(v_perm);
        if v_perm = 'tous' then return true; end if;
        if v_perm = 'admins' and v_role = 'Administrateur' then return true; end if;
        if v_perm = 'gestion' and v_role in ('Administrateur', 'Gestionnaire') then return true; end if;
        if v_perm in ('tous', 'admins', 'gestion') then continue; end if;
        if v_matrix is null then
            -- Matrice pas encore enregistrée : seuls les administrateurs ont les droits.
            if v_role = 'Administrateur' then return true; end if;
            continue;
        end if;
        select elem into v_set from jsonb_array_elements(v_matrix) elem where elem ->> 'role' = v_role limit 1;
        if v_set is not null and coalesce((v_set -> 'allowed' ->> v_perm)::boolean, false) then
            return true;
        end if;
    end loop;
    return false;
end;
$$;

-- Droits de lecture et d'écriture de chaque collection.
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
-- Inscription : le premier compte devient administrateur, les suivants
-- attendent la validation d'un administrateur.
-- ---------------------------------------------------------------------

create or replace function public.gu_handle_new_user()
returns trigger language plpgsql security definer set search_path = public as $$
declare
    v_first   boolean;
    v_meta    jsonb := coalesce(new.raw_user_meta_data, '{}'::jsonb);
    v_complet text := trim(coalesce(nullif(v_meta ->> 'full_name', ''), v_meta ->> 'name', ''));
    v_prenom  text := trim(coalesce(nullif(v_meta ->> 'prenom', ''), nullif(v_meta ->> 'given_name', ''), ''));
    v_nom     text := trim(coalesce(nullif(v_meta ->> 'nom', ''), nullif(v_meta ->> 'family_name', ''), ''));
begin
    if v_prenom = '' and v_nom = '' and v_complet <> '' then
        v_prenom := split_part(v_complet, ' ', 1);
        v_nom := trim(substr(v_complet, length(v_prenom) + 1));
    end if;
    select not exists (select 1 from public.profiles where statut = 'actif') into v_first;
    insert into public.profiles (id, email, prenom, nom, telephone, role, statut, validated_at)
    values (
        new.id,
        coalesce(new.email, ''),
        left(v_prenom, 80),
        left(v_nom, 80),
        left(coalesce(v_meta ->> 'telephone', ''), 40),
        case when v_first then 'Administrateur' else 'Utilisateur' end,
        case when v_first then 'actif' else 'en_attente' end,
        case when v_first then now() else null end
    )
    on conflict (id) do nothing;
    return new;
end;
$$;

drop trigger if exists gu_on_auth_user_created on auth.users;
create trigger gu_on_auth_user_created
    after insert on auth.users
    for each row execute function public.gu_handle_new_user();

-- ---------------------------------------------------------------------
-- Comptes
-- ---------------------------------------------------------------------

create or replace function public.my_profile()
returns jsonb language plpgsql security definer set search_path = public as $$
declare
    v jsonb;
begin
    update public.profiles set last_seen_at = now() where id = auth.uid();
    select to_jsonb(p) into v from public.profiles p where p.id = auth.uid();
    return v;
end;
$$;

-- Comptes en ligne (demandes en attente et comptes existants).
create or replace function public.list_accounts()
returns jsonb language plpgsql stable security definer set search_path = public as $$
begin
    if not public.gu_can('users.manage') then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    return coalesce((
        select jsonb_agg(to_jsonb(p) order by (p.statut = 'en_attente') desc, p.created_at desc)
        from public.profiles p
    ), '[]'::jsonb);
end;
$$;

create or replace function public.gu_check_role_change(p_target uuid, p_role text)
returns void language plpgsql stable security definer set search_path = public as $$
declare
    v_caller text := public.gu_role();
    v_current text;
begin
    select role into v_current from public.profiles where id = p_target;
    if p_role not in ('Administrateur de la base', 'Administrateur', 'Gestionnaire', 'Utilisateur') then
        raise exception 'Rôle inconnu : %', p_role;
    end if;
    if (p_role = 'Administrateur de la base' or v_current = 'Administrateur de la base')
       and v_caller <> 'Administrateur de la base' then
        raise exception 'Seul l''administrateur de la base peut attribuer ou modifier ce rôle.' using errcode = '42501';
    end if;
    if p_role in ('Administrateur', 'Administrateur de la base') and not public.gu_is_admin() then
        raise exception 'Seul un administrateur peut nommer un administrateur.' using errcode = '42501';
    end if;
end;
$$;

create or replace function public.approve_account(p_id uuid, p_role text)
returns jsonb language plpgsql security definer set search_path = public as $$
declare
    v jsonb;
begin
    if not public.gu_can('users.manage') then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    perform public.gu_check_role_change(p_id, p_role);
    update public.profiles
       set statut = 'actif', role = p_role, validated_at = now(), validated_by = auth.uid()
     where id = p_id
    returning to_jsonb(profiles.*) into v;
    if v is null then raise exception 'Compte introuvable.'; end if;
    return v;
end;
$$;

create or replace function public.reject_account(p_id uuid)
returns void language plpgsql security definer set search_path = public as $$
begin
    if not public.gu_can('users.manage') then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    if p_id = auth.uid() then raise exception 'Impossible de refuser votre propre compte.'; end if;
    update public.profiles set statut = 'refuse', validated_at = now(), validated_by = auth.uid()
     where id = p_id and statut in ('en_attente', 'refuse');
end;
$$;

-- Supprime définitivement une demande refusée ou en attente.
create or replace function public.delete_account_request(p_id uuid)
returns void language plpgsql security definer set search_path = public, auth as $$
begin
    if not public.gu_can('users.manage') then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    if not exists (select 1 from public.profiles where id = p_id and statut in ('en_attente', 'refuse')) then
        raise exception 'Seule une demande en attente ou refusée peut être supprimée.';
    end if;
    delete from auth.users where id = p_id;
end;
$$;

-- Sans service d'e-mail, c'est l'administrateur qui définit un nouveau mot
-- de passe (mot de passe oublié) et le transmet au membre.
create or replace function public.admin_set_password(p_id uuid, p_password text)
returns void language plpgsql security definer set search_path = public, auth, extensions as $$
begin
    if not public.gu_is_admin() then
        raise exception 'Seul un administrateur peut réinitialiser un mot de passe.' using errcode = '42501';
    end if;
    if length(coalesce(p_password, '')) < 8 then
        raise exception 'Le mot de passe doit contenir au moins 8 caractères.';
    end if;
    if exists (select 1 from public.profiles where id = p_id and role = 'Administrateur de la base')
       and public.gu_role() <> 'Administrateur de la base' then
        raise exception 'Seul l''administrateur de la base peut modifier ce compte.' using errcode = '42501';
    end if;
    update auth.users
       set encrypted_password = extensions.crypt(p_password, extensions.gen_salt('bf')),
           updated_at = now()
     where id = p_id;
    if not found then raise exception 'Compte introuvable.'; end if;
    update public.profiles set mdp_provisoire = true, mdp_demande_at = null where id = p_id;
end;
$$;

-- Applique aux comptes en ligne les rôles et l'état actif/inactif des fiches
-- membres (gu2:users), pour que les droits côté serveur suivent l'application.
create or replace function public.gu_sync_accounts(p_users jsonb)
returns void language plpgsql security definer set search_path = public as $$
declare
    v_elem     jsonb;
    v_id       uuid;
    v_role     text;
    v_actif    boolean;
    v_profile  public.profiles;
    v_seen     uuid[] := '{}';
    v_can_role boolean := public.gu_is_admin() or public.gu_can('roles.manage');
begin
    if jsonb_typeof(p_users) <> 'array' then return; end if;
    for v_elem in select * from jsonb_array_elements(p_users) loop
        begin
            v_id := (v_elem ->> 'id')::uuid;
        exception when others then
            continue; -- fiche sans compte en ligne
        end;
        select * into v_profile from public.profiles where id = v_id;
        if not found or v_profile.statut not in ('actif', 'desactive') then continue; end if;
        v_seen := v_seen || v_id;
        if v_id = auth.uid() then continue; end if; -- jamais sur son propre compte
        v_role := coalesce(v_elem ->> 'role', v_profile.role);
        v_actif := coalesce((v_elem ->> 'actif')::boolean, true);
        if v_role <> v_profile.role then
            if v_can_role then
                begin
                    perform public.gu_check_role_change(v_id, v_role);
                    update public.profiles set role = v_role where id = v_id;
                exception when others then
                    null; -- changement non autorisé : ignoré
                end;
            end if;
        end if;
        -- Seul un administrateur peut activer ou désactiver un compte administrateur.
        if (v_profile.role = 'Administrateur de la base' and public.gu_role() = 'Administrateur de la base')
           or (v_profile.role = 'Administrateur' and public.gu_is_admin())
           or v_profile.role not in ('Administrateur', 'Administrateur de la base') then
            update public.profiles
               set statut = case when v_actif then 'actif' else 'desactive' end
             where id = v_id;
        end if;
    end loop;
    -- Fiche supprimée : le compte en ligne est désactivé.
    if jsonb_array_length(p_users) > 0 then
        update public.profiles
           set statut = 'desactive'
         where statut = 'actif'
           and id <> auth.uid()
           and role <> 'Administrateur de la base'
           and (role <> 'Administrateur' or public.gu_is_admin())
           and not (id = any (v_seen));
    end if;
end;
$$;

-- ---------------------------------------------------------------------
-- Données partagées
-- ---------------------------------------------------------------------

-- Renvoie les collections lisibles par le compte connecté dont la version
-- diffère de celle déjà en cache sur l'appareil (p_known = {clé: version}).
create or replace function public.load_app_data(p_known jsonb default '{}'::jsonb)
returns jsonb language plpgsql stable security definer set search_path = public as $$
declare
    v_rows     jsonb := '[]'::jsonb;
    v_readable jsonb := '[]'::jsonb;
    r          record;
begin
    if public.gu_role() is null then
        raise exception 'Compte non validé' using errcode = '42501';
    end if;
    for r in select a.key, a.value, a.version from public.app_data a loop
        if public.gu_can((public.gu_key_rule(r.key)).read_perm) then
            v_readable := v_readable || to_jsonb(r.key);
            if coalesce((p_known ->> r.key)::bigint, -1) <> r.version then
                v_rows := v_rows || jsonb_build_object('key', r.key, 'value', r.value, 'version', r.version);
            end if;
        end if;
    end loop;
    return jsonb_build_object('rows', v_rows, 'readable', v_readable);
end;
$$;

-- Enregistre une collection si le compte en a le droit et si personne ne
-- l'a modifiée entre-temps (sinon « conflict » avec la version du serveur).
create or replace function public.save_app_data(p_key text, p_value jsonb, p_expected_version bigint)
returns jsonb language plpgsql security definer set search_path = public as $$
declare
    v_rule     record;
    v_version  bigint;
    v_current  jsonb;
    v_admin    jsonb;
begin
    select * into v_rule from public.gu_key_rule(p_key);
    if v_rule.write_perm is null then
        return jsonb_build_object('status', 'denied', 'message', 'Collection inconnue.');
    end if;
    if not public.gu_can(v_rule.write_perm) then
        return jsonb_build_object('status', 'denied', 'message', 'Vous n''avez pas le droit de modifier ces données.');
    end if;
    if pg_column_size(p_value) > 5 * 1024 * 1024 then
        return jsonb_build_object('status', 'too_large', 'message', 'Données trop volumineuses (plus de 5 Mo).');
    end if;
    if p_key = 'gu2:permissions' then
        -- Verrou anti-blocage : les administrateurs gardent la gestion des rôles.
        select elem into v_admin from jsonb_array_elements(p_value) elem where elem ->> 'role' = 'Administrateur' limit 1;
        if v_admin is null or not coalesce((v_admin -> 'allowed' ->> 'roles.manage')::boolean, false) then
            return jsonb_build_object('status', 'denied', 'message', 'Les administrateurs doivent garder la gestion des rôles.');
        end if;
    end if;

    select version, value into v_version, v_current from public.app_data where key = p_key for update;
    if found and v_version <> coalesce(p_expected_version, 0) then
        if public.gu_can(v_rule.read_perm) then
            return jsonb_build_object('status', 'conflict', 'version', v_version, 'value', v_current);
        end if;
        return jsonb_build_object('status', 'conflict', 'version', v_version);
    end if;

    insert into public.app_data as a (key, value, version, updated_at, updated_by)
    values (p_key, p_value, 1, now(), auth.uid())
    on conflict (key) do update
       set value = excluded.value, version = a.version + 1, updated_at = now(), updated_by = auth.uid()
    returning a.version into v_version;

    if p_key = 'gu2:users' then
        perform public.gu_sync_accounts(p_value);
    end if;
    return jsonb_build_object('status', 'ok', 'version', v_version);
end;
$$;

-- ---------------------------------------------------------------------
-- Fichiers (photos, documents, journal multimédia)
-- ---------------------------------------------------------------------

insert into storage.buckets (id, name, public, file_size_limit)
values ('fichiers', 'fichiers', false, 20971520)
on conflict (id) do nothing;

drop policy if exists gu_fichiers_lecture on storage.objects;
create policy gu_fichiers_lecture on storage.objects for select to authenticated
    using (bucket_id = 'fichiers' and public.gu_role() is not null);

drop policy if exists gu_fichiers_envoi on storage.objects;
create policy gu_fichiers_envoi on storage.objects for insert to authenticated
    with check (bucket_id = 'fichiers' and public.gu_role() is not null);

drop policy if exists gu_fichiers_suppression on storage.objects;
create policy gu_fichiers_suppression on storage.objects for delete to authenticated
    using (bucket_id = 'fichiers' and public.gu_is_admin());

-- Fichiers qui ne sont plus utilisés par aucune donnée (envoyés il y a plus
-- d'un jour), que l'administrateur peut supprimer pour libérer de l'espace.
create or replace function public.unused_files()
returns jsonb language plpgsql stable security definer set search_path = public, storage as $$
declare
    v_all text;
begin
    if not public.gu_is_admin() then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    select string_agg(value::text, ' ') into v_all from public.app_data;
    v_all := coalesce(v_all, '');
    return coalesce((
        select jsonb_agg(jsonb_build_object('name', o.name, 'size', coalesce((o.metadata ->> 'size')::bigint, 0)))
          from storage.objects o
         where o.bucket_id = 'fichiers'
           and o.created_at < now() - interval '1 day'
           and position(('gufile:' || split_part(o.name, '/', 2)) in v_all) = 0
    ), '[]'::jsonb);
end;
$$;

-- ---------------------------------------------------------------------
-- Capacité et alerte de saturation
-- ---------------------------------------------------------------------

create or replace function public.gu_measure()
returns jsonb language sql stable security definer set search_path = public, storage as $$
    select jsonb_build_object(
        'db_bytes', pg_database_size(current_database()),
        'storage_bytes', coalesce((select sum(coalesce((metadata ->> 'size')::bigint, 0)) from storage.objects), 0),
        'storage_files', (select count(*) from storage.objects),
        'members_active', (select count(*) from public.profiles where statut = 'actif'),
        'members_pending', (select count(*) from public.profiles where statut = 'en_attente')
    );
$$;

-- Prend une mesure, la garde dans l'historique et crée une alerte quand un
-- seuil est franchi. Appelée chaque jour par GitHub Actions (clé service)
-- et à l'ouverture de la page Capacité par un administrateur.
create or replace function public.record_capacity_snapshot(p_source text default 'app')
returns jsonb language plpgsql security definer set search_path = public as $$
declare
    m        jsonb := public.gu_measure();
    s        public.capacity_settings;
    v_metric text;
    v_used   bigint;
    v_limit  bigint;
    v_pct    numeric;
    v_level  text;
    v_label  text;
    v_alerts jsonb := '[]'::jsonb;
    v_new    jsonb;
    v_worst  text := 'ok';
begin
    if not (public.gu_is_admin() or public.gu_is_service()) then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    select * into s from public.capacity_settings where id = 1;

    insert into public.capacity_snapshots (db_bytes, storage_bytes, storage_files, members_active, members_pending, source)
    values ((m ->> 'db_bytes')::bigint, (m ->> 'storage_bytes')::bigint, (m ->> 'storage_files')::int,
            (m ->> 'members_active')::int, (m ->> 'members_pending')::int, left(coalesce(p_source, 'app'), 20));
    delete from public.capacity_snapshots where taken_at < now() - interval '400 days';

    foreach v_metric in array array['base', 'fichiers'] loop
        if v_metric = 'base' then
            v_used := (m ->> 'db_bytes')::bigint; v_limit := s.db_limit_bytes; v_label := 'Base de données';
        else
            v_used := (m ->> 'storage_bytes')::bigint; v_limit := s.storage_limit_bytes; v_label := 'Stockage des fichiers';
        end if;
        v_pct := round(100.0 * v_used / greatest(v_limit, 1), 1);
        v_level := case when v_pct >= s.critical_pct then 'critique'
                        when v_pct >= s.warning_pct then 'attention' end;
        if v_level = 'critique' then v_worst := 'critique';
        elsif v_level = 'attention' and v_worst = 'ok' then v_worst := 'attention'; end if;
        -- Une seule alerte non lue par mesure et par niveau.
        if v_level is not null and not exists (
            select 1 from public.admin_alerts
             where metric = v_metric and acknowledged_at is null
               and (level = v_level or level = 'critique')
        ) then
            insert into public.admin_alerts (level, metric, percent, message)
            values (v_level, v_metric, v_pct,
                    v_label || ' : ' || replace(v_pct::text, '.', ',') || ' % de la limite gratuite utilisés.')
            returning jsonb_build_object('level', level, 'metric', metric, 'percent', percent, 'message', message)
            into v_new;
            v_alerts := v_alerts || v_new;
        end if;
    end loop;

    return m || jsonb_build_object('level', v_worst, 'new_alerts', v_alerts,
        'db_pct', round(100.0 * (m ->> 'db_bytes')::bigint / greatest(s.db_limit_bytes, 1), 1),
        'storage_pct', round(100.0 * (m ->> 'storage_bytes')::bigint / greatest(s.storage_limit_bytes, 1), 1));
end;
$$;

-- Rapport complet pour la page Capacité de l'administrateur.
create or replace function public.get_capacity()
returns jsonb language plpgsql stable security definer set search_path = public as $$
begin
    if not public.gu_is_admin() then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    return public.gu_measure() || jsonb_build_object(
        'settings', (select to_jsonb(s) from public.capacity_settings s where id = 1),
        'collections', coalesce((
            select jsonb_agg(jsonb_build_object('key', key, 'bytes', pg_column_size(value), 'version', version, 'updated_at', updated_at) order by pg_column_size(value) desc)
              from public.app_data), '[]'::jsonb),
        'history', coalesce((
            select jsonb_agg(jsonb_build_object('day', day, 'db_bytes', db_bytes, 'storage_bytes', storage_bytes) order by day)
              from (
                select distinct on (taken_at::date) taken_at::date as day, db_bytes, storage_bytes
                  from public.capacity_snapshots
                 where taken_at > now() - interval '90 days'
                 order by taken_at::date, taken_at desc
              ) h), '[]'::jsonb),
        'alerts', coalesce((
            select jsonb_agg(to_jsonb(a) order by a.created_at desc)
              from (select * from public.admin_alerts
                     where acknowledged_at is null or created_at > now() - interval '30 days'
                     order by created_at desc limit 20) a), '[]'::jsonb)
    );
end;
$$;

-- Nombre d'alertes non lues et niveau le plus grave (bandeau de l'administrateur).
create or replace function public.capacity_status()
returns jsonb language plpgsql stable security definer set search_path = public as $$
begin
    if not public.gu_is_admin() then
        return jsonb_build_object('open_alerts', 0, 'level', 'ok', 'pending_accounts', 0, 'password_requests', 0);
    end if;
    return jsonb_build_object(
        'open_alerts', (select count(*) from public.admin_alerts where acknowledged_at is null),
        'level', coalesce((select case when bool_or(level = 'critique') then 'critique' else 'attention' end
                             from public.admin_alerts where acknowledged_at is null having count(*) > 0), 'ok'),
        'pending_accounts', (select count(*) from public.profiles where statut = 'en_attente'),
        'password_requests', (select count(*) from public.profiles where statut = 'actif' and mdp_demande_at is not null));
end;
$$;

create or replace function public.acknowledge_alert(p_id bigint)
returns void language plpgsql security definer set search_path = public as $$
begin
    if not public.gu_is_admin() then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    update public.admin_alerts set acknowledged_at = now(), acknowledged_by = auth.uid()
     where id = p_id and acknowledged_at is null;
end;
$$;

create or replace function public.set_capacity_settings(p_warning int, p_critical int)
returns void language plpgsql security definer set search_path = public as $$
begin
    if not public.gu_is_admin() then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    if p_warning < 10 or p_critical > 100 or p_warning >= p_critical then
        raise exception 'Seuils invalides : 10 ≤ attention < critique ≤ 100.';
    end if;
    update public.capacity_settings
       set warning_pct = p_warning, critical_pct = p_critical, updated_at = now(), updated_by = auth.uid()
     where id = 1;
end;
$$;

-- ---------------------------------------------------------------------
-- Droits d'exécution
-- ---------------------------------------------------------------------

-- Les fonctions internes (gu_sync_accounts, gu_measure…) ne sont jamais
-- appelables directement depuis l'application.
revoke execute on all functions in schema public from public, anon, authenticated;

grant execute on function
    public.gu_role(), public.gu_is_admin(), public.gu_can(text), public.gu_key_rule(text),
    public.my_profile(), public.list_accounts(), public.approve_account(uuid, text),
    public.reject_account(uuid), public.delete_account_request(uuid),
    public.admin_set_password(uuid, text), public.load_app_data(jsonb),
    public.save_app_data(text, jsonb, bigint), public.unused_files(),
    public.record_capacity_snapshot(text), public.get_capacity(), public.capacity_status(),
    public.acknowledge_alert(bigint), public.set_capacity_settings(int, int)
to authenticated;

grant execute on function public.record_capacity_snapshot(text), public.gu_measure() to service_role;

-- Écran de connexion : nom et logo de la communauté visibles avant la connexion
-- (seuls ces deux réglages et le fichier du logo sont lisibles sans compte).

create or replace function public.public_branding()
returns jsonb language sql stable security definer set search_path = public as $$
    select jsonb_build_object(
        'nom',  coalesce(a.value ->> 'appName', ''),
        'logo', coalesce(a.value ->> 'logo', ''))
    from public.app_data a
    where a.key = 'gu2:settings';
$$;

-- Le logo est rangé dans les fichiers : seul ce fichier-là est lisible sans compte.
create or replace function public.gu_logo_object()
returns text language sql stable security definer set search_path = public as $$
    select 'f/' || split_part(a.value ->> 'logo', ':', 2)
    from public.app_data a
    where a.key = 'gu2:settings' and a.value ->> 'logo' like 'gufile:%';
$$;

revoke execute on function public.public_branding(), public.gu_logo_object() from public;
grant execute on function public.public_branding(), public.gu_logo_object() to anon, authenticated;

drop policy if exists gu_fichiers_logo_public on storage.objects;
create policy gu_fichiers_logo_public on storage.objects for select to anon
    using (bucket_id = 'fichiers' and name = public.gu_logo_object());

-- Mot de passe oublié (voir 0003_mot_de_passe_oublie.sql).

alter table public.profiles add column if not exists mdp_demande_at timestamptz;
alter table public.profiles add column if not exists mdp_provisoire boolean not null default false;

-- Demande envoyée depuis l'écran de connexion (sans compte connecté).
-- La réponse est toujours la même, que l'e-mail existe ou non, pour ne pas
-- révéler qui est inscrit. Une demande déjà en attente garde sa date.
create or replace function public.request_password_help(p_email text)
returns void language plpgsql security definer set search_path = public as $$
declare
    v_saisie text := lower(trim(coalesce(p_email, '')));
begin
    if length(v_saisie) not between 2 and 254 then
        return;
    end if;
    if position('@' in v_saisie) > 0 then
        update public.profiles
           set mdp_demande_at = now()
         where lower(email) = v_saisie
           and statut = 'actif'
           and mdp_demande_at is null;
    else
        update public.profiles p
           set mdp_demande_at = now()
          from public.app_data d
          cross join lateral jsonb_array_elements(d.value) as u(fiche)
         where d.key = 'gu2:users'
           and lower(u.fiche->>'identifiant') = v_saisie
           and p.id::text = u.fiche->>'id'
           and p.statut = 'actif'
           and p.mdp_demande_at is null;
    end if;
end;
$$;

-- L'administrateur écarte une demande (par exemple déjà réglée de vive voix).
create or replace function public.dismiss_password_request(p_id uuid)
returns void language plpgsql security definer set search_path = public as $$
begin
    if not public.gu_is_admin() then
        raise exception 'Accès refusé' using errcode = '42501';
    end if;
    update public.profiles set mdp_demande_at = null where id = p_id;
end;
$$;

-- Le membre a choisi son propre mot de passe : il n'est plus provisoire.
create or replace function public.password_changed()
returns void language sql security definer set search_path = public as $$
    update public.profiles set mdp_provisoire = false, mdp_demande_at = null where id = auth.uid();
$$;

revoke execute on function public.request_password_help(text), public.dismiss_password_request(uuid),
    public.password_changed() from public;
grant execute on function public.request_password_help(text) to anon, authenticated;
grant execute on function public.dismiss_password_request(uuid), public.password_changed() to authenticated;

-- Connexion avec l'identifiant (voir 0004_connexion_identifiant.sql).
create table if not exists public.login_attempts (
    identifiant text not null,
    at          timestamptz not null default now()
);
create index if not exists login_attempts_identifiant_at on public.login_attempts (identifiant, at);
alter table public.login_attempts enable row level security;
-- Aucune politique : la table n'est lisible que par les fonctions ci-dessous.

create or replace function public.login_email(p_identifiant text, p_password text)
returns text language plpgsql security definer set search_path = public, auth, extensions as $$
declare
    v_ident text := lower(trim(coalesce(p_identifiant, '')));
    v_id    uuid;
    v_email text;
begin
    if v_ident = '' or length(v_ident) > 120 or coalesce(p_password, '') = '' then
        return null;
    end if;
    delete from public.login_attempts where at < now() - interval '1 day';
    if (select count(*) from public.login_attempts
         where identifiant = v_ident and at > now() - interval '15 minutes') >= 5 then
        raise exception 'trop_de_tentatives';
    end if;

    -- Fiche membre (données partagées) reliée à un compte en ligne actif.
    select p.id into v_id
      from public.app_data d
      cross join lateral jsonb_array_elements(d.value) as u(fiche)
      join public.profiles p on p.id::text = u.fiche->>'id'
     where d.key = 'gu2:users'
       and lower(u.fiche->>'identifiant') = v_ident
       and p.statut = 'actif'
     limit 1;

    if v_id is not null then
        select a.email into v_email
          from auth.users a
         where a.id = v_id
           and a.encrypted_password = extensions.crypt(p_password, a.encrypted_password);
    end if;

    if v_email is null then
        insert into public.login_attempts (identifiant) values (v_ident);
        return null;
    end if;
    delete from public.login_attempts where identifiant = v_ident;
    return v_email;
end;
$$;

revoke execute on function public.login_email(text, text) from public;
grant execute on function public.login_email(text, text) to anon, authenticated;

-- Inscription sans e-mail et profil en attente (voir 0005_inscription_sans_email_et_google.sql).
create or replace function public.inscription_sans_email(
    p_prenom text, p_nom text, p_telephone text, p_password text)
returns text language plpgsql security definer set search_path = public, auth, extensions as $$
declare
    v_id     uuid := gen_random_uuid();
    v_email  text;
    v_prenom text := left(trim(coalesce(p_prenom, '')), 80);
    v_nom    text := left(trim(coalesce(p_nom, '')), 80);
    v_tel    text := left(trim(coalesce(p_telephone, '')), 40);
begin
    if v_prenom = '' or v_nom = '' then
        raise exception 'Le prénom et le nom sont obligatoires.';
    end if;
    if length(regexp_replace(v_tel, '[^0-9]', '', 'g')) < 6 then
        raise exception 'Sans e-mail, le numéro de téléphone est obligatoire.';
    end if;
    if length(coalesce(p_password, '')) < 8 then
        raise exception 'Le mot de passe doit contenir au moins 8 caractères.';
    end if;
    -- Garde-fou contre les inscriptions en masse.
    if (select count(*) from public.profiles
         where email like '%@sans-email.invalid' and created_at > now() - interval '1 hour') >= 20 then
        raise exception 'trop_de_demandes';
    end if;

    v_email := 'm-' || v_id || '@sans-email.invalid';
    insert into auth.users (
        instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
        raw_app_meta_data, raw_user_meta_data, created_at, updated_at,
        confirmation_token, recovery_token, email_change, email_change_token_new)
    values (
        '00000000-0000-0000-0000-000000000000', v_id, 'authenticated', 'authenticated', v_email,
        extensions.crypt(p_password, extensions.gen_salt('bf')), now(),
        '{"provider":"email","providers":["email"]}'::jsonb,
        jsonb_build_object('prenom', v_prenom, 'nom', v_nom, 'telephone', v_tel),
        now(), now(), '', '', '', '');
    insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
    values (
        gen_random_uuid(), v_id, v_id::text,
        jsonb_build_object('sub', v_id::text, 'email', v_email, 'email_verified', true),
        'email', now(), now(), now());
    -- Le profil « en attente » est créé par le déclencheur gu_on_auth_user_created.
    return v_email;
end;
$$;

revoke execute on function public.inscription_sans_email(text, text, text, text) from public;
grant execute on function public.inscription_sans_email(text, text, text, text) to anon, authenticated;

create or replace function public.complete_my_profile(p_prenom text, p_nom text, p_telephone text)
returns void language plpgsql security definer set search_path = public as $$
declare
    v_prenom text := left(trim(coalesce(p_prenom, '')), 80);
    v_nom    text := left(trim(coalesce(p_nom, '')), 80);
begin
    if v_prenom = '' or v_nom = '' then
        raise exception 'Le prénom et le nom sont obligatoires.';
    end if;
    update public.profiles
       set prenom = v_prenom,
           nom = v_nom,
           telephone = left(trim(coalesce(p_telephone, '')), 40)
     where id = auth.uid()
       and statut = 'en_attente';
end;
$$;

revoke execute on function public.complete_my_profile(text, text, text) from public;
grant execute on function public.complete_my_profile(text, text, text) to authenticated;

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
