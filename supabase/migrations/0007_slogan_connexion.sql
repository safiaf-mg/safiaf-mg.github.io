-- Écran de connexion : le slogan (Paramètres › Configuration carte de membre)
-- s'affiche sous le nom de l'application, avant la connexion.
-- Seuls le nom, le logo et le slogan sont lisibles sans compte.
--
-- À exécuter une fois dans Supabase : SQL Editor > New query > coller > Run.
-- Le script peut être relancé sans danger.

create or replace function public.public_branding()
returns jsonb language sql stable security definer set search_path = public as $$
    select jsonb_build_object(
        'nom',  coalesce(a.value ->> 'appName', ''),
        'logo', coalesce(a.value ->> 'logo', ''),
        'slogan', coalesce(a.value ->> 'carteSlogan', ''))
    from public.app_data a
    where a.key = 'gu2:settings';
$$;

revoke execute on function public.public_branding() from public;
grant execute on function public.public_branding() to anon, authenticated;
