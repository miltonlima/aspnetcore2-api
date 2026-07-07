-- Seed da modalidade "Desenvolvimento Web" e do curso "HTML 5"
-- Supabase/PostgreSQL
-- Cria 3 modulos e 30 aulas. Cada aula recebe conteudo teorico com mais de 20 linhas.

begin;

insert into public.modalidade (course_name)
select 'Desenvolvimento Web'
where not exists (
  select 1
  from public.modalidade
  where lower(course_name) = lower('Desenvolvimento Web')
);

insert into public.turma (
  nome_turma,
  modalidade_id,
  data_inicio,
  data_fim,
  active,
  inicio_inscricao,
  fim_inscricao,
  img_curso,
  descricao,
  preco
)
select
  'HTML 5',
  m.id,
  current_date,
  current_date + interval '90 days',
  true,
  current_date,
  current_date + interval '30 days',
  null,
  'Curso introdutorio e pratico de HTML 5 para criacao de paginas web bem estruturadas, semanticas, acessiveis e prontas para evoluir com CSS, JavaScript e boas praticas de publicacao.',
  0
from public.modalidade m
where lower(m.course_name) = lower('Desenvolvimento Web')
  and not exists (
    select 1
    from public.turma t
    where lower(t.nome_turma) = lower('HTML 5')
      and t.modalidade_id = m.id
  );

with curso as (
  select t.id
  from public.turma t
  join public.modalidade m on m.id = t.modalidade_id
  where lower(t.nome_turma) = lower('HTML 5')
    and lower(m.course_name) = lower('Desenvolvimento Web')
  order by t.id
  limit 1
),
modulos(titulo, descricao, ordem) as (
  values
    (
      'Fundamentos e estrutura do HTML 5',
      'Apresenta o papel do HTML 5 na web, a anatomia de um documento, tags essenciais, organizacao de conteudo e boas praticas para iniciar paginas claras e consistentes.',
      1
    ),
    (
      'Semantica, midia e formularios',
      'Explora elementos semanticos, conteudo multimidia, tabelas, listas, formularios e validacoes nativas para criar interfaces mais ricas e compreensiveis.',
      2
    ),
    (
      'Acessibilidade, SEO e publicacao',
      'Conecta HTML 5 com acessibilidade, metadados, organizacao para buscadores, desempenho inicial, validacao e preparacao do projeto para publicacao.',
      3
    )
)
insert into public.turma_modulo (turma_id, titulo, descricao, ordem, active)
select c.id, m.titulo, m.descricao, m.ordem, true
from curso c
cross join modulos m
where not exists (
  select 1
  from public.turma_modulo tm
  where tm.turma_id = c.id
    and tm.ordem = m.ordem
);

with curso as (
  select t.id
  from public.turma t
  join public.modalidade m on m.id = t.modalidade_id
  where lower(t.nome_turma) = lower('HTML 5')
    and lower(m.course_name) = lower('Desenvolvimento Web')
  order by t.id
  limit 1
),
aulas(modulo_ordem, aula_ordem, titulo, foco, pratica, duracao_minutos) as (
  values
    (1, 1, 'O que e HTML 5 e qual seu papel na web', 'a funcao do HTML como linguagem de marcacao e base estrutural das paginas', 'identificar HTML, CSS e JavaScript em uma pagina simples', 14),
    (1, 2, 'Anatomia de um documento HTML', 'a estrutura formada por doctype, html, head e body', 'montar um arquivo inicial com organizacao correta', 16),
    (1, 3, 'Tags, elementos e atributos', 'a diferenca entre tag, elemento, atributo e valor', 'criar elementos com atributos comuns e nomes legiveis', 15),
    (1, 4, 'Textos, titulos e paragrafos', 'a hierarquia de conteudo textual e a importancia dos titulos', 'organizar uma pagina com h1, h2, h3 e paragrafos', 14),
    (1, 5, 'Links e navegacao basica', 'a criacao de links internos, externos e ancoras', 'montar um menu simples de navegacao entre secoes', 15),
    (1, 6, 'Imagens e caminhos de arquivos', 'o uso de img, src, alt e caminhos relativos', 'inserir imagens com texto alternativo adequado', 15),
    (1, 7, 'Listas ordenadas e nao ordenadas', 'a representacao de sequencias, grupos e estruturas repetidas', 'criar listas de conteudo, etapas e recursos', 12),
    (1, 8, 'Organizacao com div e span', 'o uso de elementos genericos quando nao existe semantica especifica', 'separar blocos e trechos inline com clareza', 13),
    (1, 9, 'Comentarios e legibilidade do codigo', 'a importancia da leitura do codigo e da organizacao visual', 'adicionar comentarios uteis sem poluir o documento', 11),
    (1, 10, 'Primeira pagina completa em HTML 5', 'a integracao dos elementos fundamentais em um documento unico', 'criar uma pagina simples com cabecalho, conteudo e links', 18),

    (2, 1, 'Elementos semanticos principais', 'o uso de header, nav, main, section, article, aside e footer', 'reestruturar uma pagina usando tags semanticas', 16),
    (2, 2, 'Semantica para leitura humana e maquinas', 'como a semantica ajuda usuarios, navegadores, leitores de tela e buscadores', 'trocar divs genericas por elementos mais significativos', 15),
    (2, 3, 'Tabelas com estrutura correta', 'a criacao de tabelas com caption, thead, tbody, tr, th e td', 'montar uma tabela simples e compreensivel', 16),
    (2, 4, 'Audio e video no HTML 5', 'os elementos nativos de midia e seus atributos principais', 'inserir audio ou video com controles e fallback', 15),
    (2, 5, 'Iframes e conteudo incorporado', 'a incorporacao de conteudos externos com responsabilidade', 'adicionar um iframe com titulo e dimensoes adequadas', 14),
    (2, 6, 'Introducao a formularios', 'a estrutura de form, label, input e button', 'criar um formulario de contato basico', 16),
    (2, 7, 'Tipos de input e usabilidade', 'os tipos text, email, number, date, checkbox, radio e file', 'selecionar o input correto para cada dado', 17),
    (2, 8, 'Select, textarea e fieldset', 'campos de selecao, textos longos e agrupamento de controles', 'organizar um formulario com grupos logicos', 15),
    (2, 9, 'Validacao nativa de formularios', 'required, minlength, maxlength, pattern e mensagens do navegador', 'aplicar validacoes simples sem JavaScript', 16),
    (2, 10, 'Formulario completo e semantico', 'a combinacao de campos, rotulos, grupos e validacao basica', 'montar um formulario de cadastro organizado', 20),

    (3, 1, 'Acessibilidade no HTML 5', 'a relacao entre estrutura semantica e inclusao digital', 'revisar uma pagina pensando em leitores de tela', 16),
    (3, 2, 'Textos alternativos e nomes acessiveis', 'como alt, labels e textos de links influenciam a compreensao', 'melhorar imagens, botoes e links com textos claros', 15),
    (3, 3, 'Ordem de leitura e navegacao por teclado', 'a importancia da sequencia natural do documento', 'testar a navegacao usando apenas teclado', 14),
    (3, 4, 'Metadados essenciais no head', 'title, meta charset, viewport e description', 'configurar o head de uma pagina real', 15),
    (3, 5, 'HTML 5 e SEO basico', 'como titulos, semantica e descricao ajudam buscadores', 'ajustar uma pagina para melhor interpretacao', 16),
    (3, 6, 'Boas praticas de nomes e pastas', 'organizacao de arquivos, imagens e paginas do projeto', 'criar uma estrutura de pastas simples e previsivel', 13),
    (3, 7, 'Validacao do HTML', 'a verificacao de erros de sintaxe e estrutura', 'usar um validador para revisar o documento', 14),
    (3, 8, 'Preparacao para CSS e JavaScript', 'como classes, ids e estrutura limpa facilitam evolucao', 'preparar marcacao para receber estilos e interacao', 15),
    (3, 9, 'Publicacao de paginas estaticas', 'conceitos de hospedagem, arquivo inicial e caminho publico', 'organizar uma pagina para publicacao simples', 15),
    (3, 10, 'Projeto final em HTML 5', 'a construcao de uma pagina completa com semantica, acessibilidade e organizacao', 'entregar uma pagina final revisada e validada', 22)
)
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
  c.id,
  tm.id,
  a.titulo,
  array_to_string(array[
    format('Aula: %s.', a.titulo),
    format('Tema central: %s.', a.foco),
    'O HTML 5 deve ser entendido como a camada de estrutura do documento web.',
    'Ele descreve o significado das partes da pagina antes de qualquer estilo visual.',
    'Uma boa marcacao melhora manutencao, acessibilidade, leitura e evolucao do projeto.',
    'Nesta aula, o foco e compreender conceitos antes de memorizar tags isoladas.',
    'Cada elemento deve ser escolhido conforme a funcao que desempenha no conteudo.',
    'Quando a estrutura e clara, CSS e JavaScript conseguem trabalhar com mais previsibilidade.',
    'Tambem fica mais simples para outra pessoa entender a intencao do codigo.',
    'O navegador interpreta a marcacao e transforma o documento em uma arvore de elementos.',
    'Por isso, erros de abertura, fechamento ou hierarquia podem causar comportamentos inesperados.',
    'A semantica correta ajuda tecnologias assistivas a apresentar o conteudo com mais qualidade.',
    'Ela tambem ajuda mecanismos de busca a identificar titulos, secoes, links e conteudo principal.',
    'Ao escrever HTML, e importante pensar em ordem, clareza e relacao entre as informacoes.',
    'Evite usar elementos apenas pela aparencia visual que eles parecem produzir.',
    'A aparencia deve ser responsabilidade do CSS, enquanto o HTML representa estrutura e sentido.',
    'A pratica recomendada e criar pequenas paginas e revisar o codigo em ciclos curtos.',
    'Leia o documento de cima para baixo e verifique se a sequencia faz sentido para o usuario.',
    'Compare o resultado no navegador com a intencao original do conteudo.',
    'Use nomes, textos e atributos que comuniquem proposito de forma direta.',
    format('Atividade sugerida: %s.', a.pratica),
    'Ao final, registre duvidas e melhorias para aplicar na proxima revisao do projeto.'
  ], E'\n'),
  a.duracao_minutos,
  a.aula_ordem,
  null,
  true
from curso c
join public.turma_modulo tm on tm.turma_id = c.id
join aulas a on a.modulo_ordem = tm.ordem
where not exists (
  select 1
  from public.turma_aula ta
  where ta.turma_id = c.id
    and ta.modulo_id = tm.id
    and ta.ordem = a.aula_ordem
);

commit;
