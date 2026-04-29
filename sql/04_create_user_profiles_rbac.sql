-- RBAC for Supabase/PostgreSQL
-- Adds user profiles: Administrador, Gerente, Coordenador, Professor, Aluno
-- Compatible with existing public.users table used by the API.

begin;

-- Reuse trigger function if it already exists in the database.
create or replace function public.fn_set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new := json_populate_record(new, json_build_object('updated_at', now()));
    return new;
end;
$$;

create table if not exists public.perfil_acesso (
    id smallint generated always as identity primary key,
    codigo text not null unique,
    nome text not null,
    descricao text null,
    nivel smallint not null default 50,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_perfil_acesso_codigo check (codigo = upper(codigo)),
    constraint ck_perfil_acesso_nivel check (nivel between 1 and 100)
);

create table if not exists public.usuario_perfil_acesso (
    id bigint generated always as identity primary key,
    user_id bigint not null references public.users(id) on delete cascade,
    perfil_id smallint not null references public.perfil_acesso(id) on delete restrict,
    principal boolean not null default false,
    active boolean not null default true,
    data_inicio date not null default current_date,
    data_fim date null,
    observacao text null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_usuario_perfil unique (user_id, perfil_id),
    constraint ck_usuario_perfil_datas check (data_fim is null or data_fim >= data_inicio)
);

create index if not exists idx_usuario_perfil_user_id
    on public.usuario_perfil_acesso(user_id);

create index if not exists idx_usuario_perfil_perfil_id
    on public.usuario_perfil_acesso(perfil_id);

create unique index if not exists ux_usuario_perfil_principal_ativo
    on public.usuario_perfil_acesso(user_id)
    where principal = true and active = true and data_fim is null;

-- Optional scope table to support role assignments by modality/turma when needed.
create table if not exists public.usuario_perfil_escopo (
    id bigint generated always as identity primary key,
    usuario_perfil_id bigint not null references public.usuario_perfil_acesso(id) on delete cascade,
    modalidade_id bigint null references public.modalidade(id) on delete cascade,
    turma_id bigint null references public.turma(id) on delete cascade,
    created_at timestamptz not null default now(),
    constraint uq_usuario_perfil_escopo unique (usuario_perfil_id, modalidade_id, turma_id),
    constraint ck_usuario_perfil_escopo_alvo check (modalidade_id is not null or turma_id is not null)
);

create index if not exists idx_usuario_perfil_escopo_usuario_perfil
    on public.usuario_perfil_escopo(usuario_perfil_id);

create index if not exists idx_usuario_perfil_escopo_modalidade
    on public.usuario_perfil_escopo(modalidade_id);

create index if not exists idx_usuario_perfil_escopo_turma
    on public.usuario_perfil_escopo(turma_id);

-- Seed system profiles (idempotent).
insert into public.perfil_acesso (codigo, nome, descricao, nivel, active)
values
    ('ADMINISTRADOR', 'Administrador', 'Acesso total ao sistema.', 100, true),
    ('GERENTE', 'Gerente', 'Gestao administrativa e operacional.', 80, true),
    ('COORDENADOR', 'Coordenador', 'Coordenacao academica e acompanhamento de turmas.', 70, true),
    ('PROFESSOR', 'Professor', 'Gestao de conteudo, modulos e aulas.', 60, true),
    ('ALUNO', 'Aluno', 'Acesso a inscricoes, cursos e progresso.', 20, true)
on conflict (codigo) do update
set
    nome = excluded.nome,
    descricao = excluded.descricao,
    nivel = excluded.nivel,
    active = excluded.active,
    updated_at = now();

-- Backfill: users without a profile get ALUNO as principal profile.
insert into public.usuario_perfil_acesso (user_id, perfil_id, principal, active)
select u.id, p.id, true, true
from public.users u
join public.perfil_acesso p on p.codigo = 'ALUNO'
where not exists (
    select 1
    from public.usuario_perfil_acesso up
    where up.user_id = u.id
      and up.active = true
      and up.data_fim is null
);

-- Trigger for updated_at maintenance.
drop trigger if exists trg_perfil_acesso_set_updated_at on public.perfil_acesso;
create trigger trg_perfil_acesso_set_updated_at
before update on public.perfil_acesso
for each row execute function public.fn_set_updated_at();

drop trigger if exists trg_usuario_perfil_acesso_set_updated_at on public.usuario_perfil_acesso;
create trigger trg_usuario_perfil_acesso_set_updated_at
before update on public.usuario_perfil_acesso
for each row execute function public.fn_set_updated_at();

-- Helper view for API queries.
create or replace view public.vw_usuario_perfis as
select
    up.user_id,
    u.full_name,
    u.email,
    pa.codigo as perfil_codigo,
    pa.nome as perfil_nome,
    pa.nivel as perfil_nivel,
    up.principal,
    up.active,
    up.data_inicio,
    up.data_fim
from public.usuario_perfil_acesso up
join public.perfil_acesso pa on pa.id = up.perfil_id
join public.users u on u.id = up.user_id;

commit;
