-- Seed de 10 aulas de Inglês no Módulo 1
-- Idempotente: pode ser executado mais de uma vez sem duplicar dados.
-- Pré-requisito: script 01_create_school_lms_tables.sql aplicado.

begin;

-- 1) Garante modalidade de Inglês.
insert into public.modalidade (course_name)
select 'Inglês Básico'
where not exists (
    select 1
    from public.modalidade
    where lower(course_name) = lower('Inglês Básico')
);

-- 2) Garante turma vinculada à modalidade.
insert into public.turma (nome_turma, modalidade_id, data_inicio, data_fim, active)
select
    'Inglês Básico - Turma A',
    m.id,
    current_date,
    current_date + interval '120 days',
    true
from public.modalidade m
where lower(m.course_name) = lower('Inglês Básico')
  and not exists (
      select 1
      from public.turma t
      where lower(t.nome_turma) = lower('Inglês Básico - Turma A')
  );

-- 3) Garante Módulo 1 na turma.
insert into public.turma_modulo (turma_id, titulo, descricao, ordem, active)
select
    t.id,
    'Módulo 1',
    'Fundamentos de inglês para comunicação básica: saudações, apresentações, vocabulário inicial e estruturas essenciais.',
    1,
    true
from public.turma t
where lower(t.nome_turma) = lower('Inglês Básico - Turma A')
  and not exists (
      select 1
      from public.turma_modulo md
      where md.turma_id = t.id
        and md.ordem = 1
  );

-- 4) Insere 10 aulas no Módulo 1 com títulos, descrições detalhadas e URLs do YouTube.
insert into public.turma_aula (
    turma_id,
    modulo_id,
    titulo,
    descricao,
    duracao_minutos,
    ordem,
    video_url,
    active
)
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
join public.turma_modulo md
  on md.turma_id = t.id
 and md.ordem = 1
join lateral (
    select * from (
        values
            (
                'Aula 1 - Greetings and Introductions',
                'Introdução às saudações formais e informais em inglês. O aluno aprende a cumprimentar, perguntar como a pessoa está e se apresentar com frases simples para situações do dia a dia.',
                20,
                1,
                'https://www.youtube.com/watch?v=Y8x7f8u7Q2E'
            ),
            (
                'Aula 2 - Alphabet and Pronunciation',
                'Estudo do alfabeto em inglês com foco na pronúncia correta das letras. A aula trabalha soletração de nomes, e-mails e palavras básicas, reforçando compreensão auditiva e fala.',
                24,
                2,
                'https://www.youtube.com/watch?v=75p-N9YKqNo'
            ),
            (
                'Aula 3 - Numbers and Basic Information',
                'Aprendizado de números, idade, telefone e dados pessoais. O conteúdo inclui perguntas e respostas comuns para preencher formulários e interações iniciais em ambientes acadêmicos e profissionais.',
                22,
                3,
                'https://www.youtube.com/watch?v=DR-cfDsHCGA'
            ),
            (
                'Aula 4 - Days, Months and Dates',
                'Vocabulário e uso de dias da semana, meses e datas em inglês. A aula ensina como dizer aniversários, compromissos e rotina semanal, além de práticas de escuta com exemplos contextualizados.',
                23,
                4,
                'https://www.youtube.com/watch?v=V5M2WZiAy6k'
            ),
            (
                'Aula 5 - Verb To Be in Practice',
                'Aplicação prática do verbo to be em frases afirmativas, negativas e interrogativas. O aluno aprende a falar sobre identidade, nacionalidade, profissão e estado emocional.',
                28,
                5,
                'https://www.youtube.com/watch?v=4M6aLZV1C7M'
            ),
            (
                'Aula 6 - Family and Personal Relationships',
                'Vocabulário de família e relações pessoais com construção de frases para descrever parentes e vínculos. Atividades guiadas ajudam na produção oral e escrita de pequenos textos.',
                25,
                6,
                'https://www.youtube.com/watch?v=R4B6n8s1Y2w'
            ),
            (
                'Aula 7 - Daily Routine Vocabulary',
                'Termos e expressões para falar da rotina diária: horários, atividades e hábitos. A aula trabalha present simple em contexto, com foco em comunicação funcional para estudo e trabalho.',
                27,
                7,
                'https://www.youtube.com/watch?v=5Qn5fH1JkZU'
            ),
            (
                'Aula 8 - Classroom Expressions',
                'Expressões úteis para sala de aula e ambientes de aprendizagem, incluindo pedidos de repetição, esclarecimento e participação. Excelente para aumentar confiança em aulas de inglês.',
                21,
                8,
                'https://www.youtube.com/watch?v=8f9YV0sA2mI'
            ),
            (
                'Aula 9 - Listening and Short Dialogues',
                'Treino de escuta com diálogos curtos e situações reais. O aluno identifica palavras-chave, intenção comunicativa e pratica respostas rápidas para interações cotidianas em inglês.',
                26,
                9,
                'https://www.youtube.com/watch?v=3k5R7jP2wXQ'
            ),
            (
                'Aula 10 - Review and Guided Conversation',
                'Revisão geral do Módulo 1 com conversa guiada para consolidar conteúdos. A aula final integra vocabulário, gramática básica e pronúncia em uma atividade comunicativa completa.',
                30,
                10,
                'https://www.youtube.com/watch?v=YJg8qj8Zc7Y'
            )
    ) as lesson(titulo, descricao, duracao_minutos, ordem, video_url)
) as a on true
where lower(t.nome_turma) = lower('Inglês Básico - Turma A')
  and not exists (
      select 1
      from public.turma_aula aa
      where aa.turma_id = t.id
        and aa.modulo_id = md.id
        and aa.ordem = a.ordem
  );

commit;
