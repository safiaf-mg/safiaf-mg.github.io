-- Inscription sans adresse e-mail (étudiants, membres sans e-mail) et
-- connexion avec un compte Google.
--
-- Sans e-mail : le membre donne son prénom, son nom, son téléphone et un mot
-- de passe. Le serveur crée le compte avec une adresse technique
-- « m-<id>@sans-email.invalid » qui ne reçoit jamais rien (le domaine .invalid
-- n'existe pas). Le compte reste en attente comme les autres ; après
-- validation, le membre se connecte avec l'identifiant de sa fiche.
-- « Mot de passe oublié » accepte aussi l'identifiant.
--
-- Google : le prénom et le nom du compte Google remplissent le profil, et le
-- membre en attente peut corriger ses informations (complete_my_profile).
--
-- À exécuter une fois dans Supabase : SQL Editor > New query > coller > Run.
-- Le script peut être relancé sans danger.

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

-- « Mot de passe oublié » : l'e-mail du compte, ou l'identifiant de la fiche.
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

revoke execute on function public.request_password_help(text) from public;
grant execute on function public.request_password_help(text) to anon, authenticated;

-- Nouveau compte : prénom et nom saisis à l'inscription, sinon ceux du compte
-- Google (given_name / family_name, ou le nom complet coupé au premier espace).
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

-- Le membre en attente de validation complète ou corrige ses informations
-- (par exemple après une inscription avec Google, sans téléphone).
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
