-- Logs de acesso ao sistema para Supabase/PostgreSQL

create table if not exists public.access_log (
  id bigint generated always as identity primary key,
  user_id bigint null,
  user_email text null,
  user_name text null,
  user_type text null,
  session_id text null,
  page_path text not null,
  page_title text null,
  action text not null,
  http_method text null,
  ip_address inet null,
  user_agent text null,
  referrer text null,
  status_code integer null,
  metadata jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now(),

  constraint access_log_action_check
    check (length(trim(action)) > 0),

  constraint access_log_page_path_check
    check (length(trim(page_path)) > 0),

  constraint access_log_status_code_check
    check (status_code is null or (status_code >= 100 and status_code <= 599))
);

create index if not exists access_log_user_id_idx
  on public.access_log (user_id);

create index if not exists access_log_user_email_idx
  on public.access_log (lower(user_email));

create index if not exists access_log_page_path_idx
  on public.access_log (page_path);

create index if not exists access_log_action_idx
  on public.access_log (action);

create index if not exists access_log_created_at_idx
  on public.access_log (created_at desc);

create index if not exists access_log_ip_address_idx
  on public.access_log (ip_address);

create index if not exists access_log_metadata_gin_idx
  on public.access_log using gin (metadata);
