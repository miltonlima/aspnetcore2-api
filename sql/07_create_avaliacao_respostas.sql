-- Respostas de avaliacao para Supabase/PostgreSQL

create table if not exists public.avaliacao_resposta (
  id bigint generated always as identity primary key,
  aluno_id bigint null,
  aluno_nome text null,
  total_perguntas integer not null default 0,
  total_corretas integer not null default 0,
  percentual numeric(5,2) not null default 0,
  status text not null default 'Concluida',
  created_at timestamptz not null default now(),

  constraint avaliacao_resposta_totais_check
    check (total_perguntas >= 0 and total_corretas >= 0 and total_corretas <= total_perguntas),

  constraint avaliacao_resposta_percentual_check
    check (percentual >= 0 and percentual <= 100)
);

create table if not exists public.avaliacao_resposta_item (
  id bigint generated always as identity primary key,
  resposta_id bigint not null references public.avaliacao_resposta(id) on delete cascade,
  pergunta_id bigint not null references public.pergunta(id) on delete restrict,
  alternativa_id bigint not null references public.alternativa(id) on delete restrict,
  correta boolean not null default false,
  created_at timestamptz not null default now(),

  constraint avaliacao_resposta_item_unica_pergunta
    unique (resposta_id, pergunta_id)
);

create index if not exists avaliacao_resposta_aluno_id_idx
  on public.avaliacao_resposta (aluno_id);

create index if not exists avaliacao_resposta_created_at_idx
  on public.avaliacao_resposta (created_at desc);

create index if not exists avaliacao_resposta_item_resposta_id_idx
  on public.avaliacao_resposta_item (resposta_id);

create index if not exists avaliacao_resposta_item_pergunta_id_idx
  on public.avaliacao_resposta_item (pergunta_id);
