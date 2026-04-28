-- Sample data for School LMS schema (Supabase)
-- Execute after 01_create_school_lms_tables.sql

begin;

-- Cria modalidade base se não existir
insert into public.modalidade (course_name)
select 'Informática Básica'
where not exists (
    select 1 from public.modalidade where lower(course_name) = lower('Informática Básica')
);

-- Cria turma base se não existir
insert into public.turma (nome_turma, modalidade_id, data_inicio, data_fim, active)
select 'Lógica de Programação - Turma A', m.id, current_date, current_date + interval '90 days', true
from public.modalidade m
where m.course_name = 'Informática Básica'
  and not exists (
      select 1
      from public.turma t
      where lower(t.nome_turma) = lower('Lógica de Programação - Turma A')
  );

-- Cria módulos
insert into public.turma_modulo (turma_id, titulo, descricao, ordem, active)
select t.id, x.titulo, x.descricao, x.ordem, true
from public.turma t
cross join (
    values
        ('Módulo 1', 'Fundamentos de algoritmo e pseudocódigo.', 1),
        ('Módulo 2', 'Estruturas de decisão e repetição.', 2),
        ('Módulo 3', 'Projeto prático de resolução de problemas.', 3)
) as x(titulo, descricao, ordem)
where t.nome_turma = 'Lógica de Programação - Turma A'
  and not exists (
      select 1
      from public.turma_modulo md
      where md.turma_id = t.id
        and md.ordem = x.ordem
  );

-- Cria aulas por módulo
insert into public.turma_aula (turma_id, modulo_id, titulo, descricao, duracao_minutos, ordem, video_url, active)
select
    t.id,
    md.id,
    a.titulo,
    a.descricao,
    a.duracao_minutos,
    a.ordem,
    a.video_url,
    true
from public.turma t
join public.turma_modulo md on md.turma_id = t.id
join lateral (
    select * from (
        values
            ('Módulo 1', 'Boas-vindas e visão geral', 'Como estudar na plataforma e organizar sua trilha.', 12, 1, 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'),
            ('Módulo 1', 'Variáveis e tipos', 'Conceitos iniciais para começar a programar.', 25, 2, null),
            ('Módulo 2', 'Estrutura SE e SENAO', 'Tomada de decisão com exemplos reais.', 30, 3, null),
            ('Módulo 2', 'Enquanto e Para', 'Laços de repetição aplicados a desafios escolares.', 28, 4, null),
            ('Módulo 3', 'Projeto final', 'Aplicando todo o conteúdo em um mini-projeto.', 45, 5, null)
    ) as aula(modulo_titulo, titulo, descricao, duracao_minutos, ordem, video_url)
    where aula.modulo_titulo = md.titulo
) as a on true
where t.nome_turma = 'Lógica de Programação - Turma A'
  and not exists (
      select 1
      from public.turma_aula aa
      where aa.turma_id = t.id
        and aa.ordem = a.ordem
  );

commit;
