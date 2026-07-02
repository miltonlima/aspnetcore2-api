-- Adiciona campos complementares de curso na tabela public.turma
-- Supabase/PostgreSQL

alter table if exists public.turma
  add column if not exists inicio_inscricao date null,
  add column if not exists fim_inscricao date null,
  add column if not exists img_curso text null,
  add column if not exists descricao text null,
  add column if not exists classificacao text null,
  add column if not exists preco numeric(10,2) null;

update public.turma
set preco = 0
where preco is null;

alter table if exists public.turma
  alter column preco set default 0,
  alter column preco set not null;

do $$
begin
  if not exists (
    select 1
    from pg_constraint
    where conname = 'ck_turma_inscricao_datas'
      and conrelid = 'public.turma'::regclass
  ) then
    alter table public.turma
      add constraint ck_turma_inscricao_datas
      check (
        fim_inscricao is null
        or inicio_inscricao is null
        or fim_inscricao >= inicio_inscricao
      );
  end if;
end $$;

do $$
begin
  if not exists (
    select 1
    from pg_constraint
    where conname = 'ck_turma_preco_nao_negativo'
      and conrelid = 'public.turma'::regclass
  ) then
    alter table public.turma
      add constraint ck_turma_preco_nao_negativo
      check (preco >= 0);
  end if;
end $$;

create index if not exists idx_turma_inicio_inscricao
  on public.turma(inicio_inscricao);

create index if not exists idx_turma_fim_inscricao
  on public.turma(fim_inscricao);

create index if not exists idx_turma_classificacao
  on public.turma(classificacao);
