-- School LMS schema for Supabase (PostgreSQL)
-- Compatível com os endpoints atuais de modalidades/turmas/inscrições
-- e com os novos endpoints de dashboard/aulas/progresso.

begin;

create table if not exists public.modalidade (
    id bigint generated always as identity primary key,
    course_name text not null,
    created_at timestamptz not null default now()
);

create table if not exists public.turma (
    id bigint generated always as identity primary key,
    nome_turma text not null,
    modalidade_id bigint not null references public.modalidade(id) on delete restrict,
    data_inicio date null,
    data_fim date null,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_turma_datas check (data_fim is null or data_inicio is null or data_fim >= data_inicio)
);

create index if not exists idx_turma_modalidade_id on public.turma(modalidade_id);
create index if not exists idx_turma_active on public.turma(active);

create table if not exists public.inscricao (
    id bigint generated always as identity primary key,
    aluno_id bigint not null,
    turma_id bigint not null references public.turma(id) on delete cascade,
    status text not null default 'ATIVA',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_inscricao_aluno_turma unique (aluno_id, turma_id)
);

create index if not exists idx_inscricao_aluno_id on public.inscricao(aluno_id);
create index if not exists idx_inscricao_turma_id on public.inscricao(turma_id);
create index if not exists idx_inscricao_status on public.inscricao(status);

create table if not exists public.turma_modulo (
    id bigint generated always as identity primary key,
    turma_id bigint not null references public.turma(id) on delete cascade,
    titulo text not null,
    descricao text null,
    ordem int not null default 1,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_turma_modulo_ordem check (ordem > 0)
);

create index if not exists idx_turma_modulo_turma_id on public.turma_modulo(turma_id);
create index if not exists idx_turma_modulo_ordem on public.turma_modulo(turma_id, ordem);

create table if not exists public.turma_aula (
    id bigint generated always as identity primary key,
    turma_id bigint not null references public.turma(id) on delete cascade,
    modulo_id bigint null references public.turma_modulo(id) on delete set null,
    titulo text not null,
    descricao text null,
    duracao_minutos int not null default 10,
    ordem int not null default 1,
    video_url text null,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_turma_aula_ordem check (ordem > 0),
    constraint ck_turma_aula_duracao check (duracao_minutos >= 0)
);

create index if not exists idx_turma_aula_turma_id on public.turma_aula(turma_id);
create index if not exists idx_turma_aula_modulo_id on public.turma_aula(modulo_id);
create index if not exists idx_turma_aula_ordem on public.turma_aula(turma_id, ordem);

create table if not exists public.aluno_aula_progresso (
    id bigint generated always as identity primary key,
    aluno_id bigint not null,
    turma_id bigint not null references public.turma(id) on delete cascade,
    aula_id bigint not null references public.turma_aula(id) on delete cascade,
    percentual double precision not null default 0,
    concluida boolean not null default false,
    ultimo_acesso_em timestamptz not null default now(),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_aluno_aula unique (aluno_id, aula_id),
    constraint ck_aluno_aula_percentual check (percentual >= 0 and percentual <= 100)
);

create index if not exists idx_aluno_aula_progresso_aluno_id on public.aluno_aula_progresso(aluno_id);
create index if not exists idx_aluno_aula_progresso_turma_id on public.aluno_aula_progresso(turma_id);
create index if not exists idx_aluno_aula_progresso_aula_id on public.aluno_aula_progresso(aula_id);

create or replace function public.fn_set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

drop trigger if exists trg_turma_set_updated_at on public.turma;
create trigger trg_turma_set_updated_at
before update on public.turma
for each row execute function public.fn_set_updated_at();

drop trigger if exists trg_inscricao_set_updated_at on public.inscricao;
create trigger trg_inscricao_set_updated_at
before update on public.inscricao
for each row execute function public.fn_set_updated_at();

drop trigger if exists trg_turma_modulo_set_updated_at on public.turma_modulo;
create trigger trg_turma_modulo_set_updated_at
before update on public.turma_modulo
for each row execute function public.fn_set_updated_at();

drop trigger if exists trg_turma_aula_set_updated_at on public.turma_aula;
create trigger trg_turma_aula_set_updated_at
before update on public.turma_aula
for each row execute function public.fn_set_updated_at();

drop trigger if exists trg_aluno_aula_progresso_set_updated_at on public.aluno_aula_progresso;
create trigger trg_aluno_aula_progresso_set_updated_at
before update on public.aluno_aula_progresso
for each row execute function public.fn_set_updated_at();

commit;
