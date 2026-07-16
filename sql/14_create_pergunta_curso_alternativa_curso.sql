-- Banco de questoes por curso para Supabase/PostgreSQL

create table if not exists public.pergunta_curso (
  id bigint generated always as identity primary key,
  id_curso bigint not null references public.turma(id) on delete cascade,
  enunciado text not null,
  dificuldade text not null default 'Facil',
  status text not null default 'Ativa',
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),

  constraint pergunta_curso_dificuldade_check
    check (dificuldade in ('Facil', 'Media', 'Dificil')),

  constraint pergunta_curso_status_check
    check (status in ('Ativa', 'Rascunho', 'Inativa'))
);

create table if not exists public.alternativa_curso (
  id bigint generated always as identity primary key,
  pergunta_id bigint not null references public.pergunta_curso(id) on delete cascade,
  texto text not null,
  correta boolean not null default false,
  ordem integer not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),

  constraint alternativa_curso_ordem_check
    check (ordem > 0)
);

create index if not exists pergunta_curso_id_curso_idx
  on public.pergunta_curso (id_curso);

create index if not exists pergunta_curso_status_idx
  on public.pergunta_curso (status);

create index if not exists pergunta_curso_dificuldade_idx
  on public.pergunta_curso (dificuldade);

create index if not exists alternativa_curso_pergunta_id_idx
  on public.alternativa_curso (pergunta_id);

create unique index if not exists alternativa_curso_pergunta_ordem_idx
  on public.alternativa_curso (pergunta_id, ordem);

create unique index if not exists alternativa_curso_pergunta_correta_unica_idx
  on public.alternativa_curso (pergunta_id)
  where correta is true;

create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists set_pergunta_curso_updated_at on public.pergunta_curso;
create trigger set_pergunta_curso_updated_at
before update on public.pergunta_curso
for each row
execute function public.set_updated_at();

drop trigger if exists set_alternativa_curso_updated_at on public.alternativa_curso;
create trigger set_alternativa_curso_updated_at
before update on public.alternativa_curso
for each row
execute function public.set_updated_at();
