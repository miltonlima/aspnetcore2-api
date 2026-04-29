-- Fix for legacy databases where updated_at columns may be missing
-- and fn_set_updated_at trigger function causes runtime errors on UPDATE.

begin;

-- Ensure updated_at exists on tables using the generic trigger
alter table if exists public.turma
    add column if not exists updated_at timestamptz not null default now();

alter table if exists public.inscricao
    add column if not exists updated_at timestamptz not null default now();

alter table if exists public.turma_modulo
    add column if not exists updated_at timestamptz not null default now();

alter table if exists public.turma_aula
    add column if not exists updated_at timestamptz not null default now();

alter table if exists public.aluno_aula_progresso
    add column if not exists updated_at timestamptz not null default now();

-- Safe trigger function: if table has updated_at, it is filled;
-- if it does not, no runtime failure occurs.
create or replace function public.fn_set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new := json_populate_record(new, json_build_object('updated_at', now()));
    return new;
end;
$$;

-- Recreate triggers to guarantee they point to current function
-- (idempotent operations)
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
