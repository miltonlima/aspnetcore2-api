-- Seed da modalidade "Desenvolvimento Web" e do curso "HTML 5"
-- Supabase/PostgreSQL
-- Cria 3 modulos e 30 aulas. Cada aula recebe conteudo teorico objetivo com 5 linhas.

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
aulas(modulo_ordem, aula_ordem, titulo, foco, pratica, exemplo, cuidado, resultado, duracao_minutos) as (
  values
    (1, 1, 'O que e HTML 5 e qual seu papel na web', 'a funcao do HTML como linguagem de marcacao e base estrutural das paginas', 'identificar HTML, CSS e JavaScript em uma pagina simples', 'uma noticia online em que o HTML separa titulo, corpo, links e imagens', 'confundir HTML com linguagem de programacao ou com ferramenta de design visual', 'explicar por que o HTML organiza o conteudo antes da etapa de estilo', 14),
    (1, 2, 'Anatomia de um documento HTML', 'a estrutura formada por doctype, html, head e body', 'montar um arquivo inicial com organizacao correta', 'um arquivo index.html com metadados no head e conteudo visivel no body', 'colocar conteudo visual dentro do head ou esquecer a declaracao doctype', 'reconhecer onde cada parte do documento deve ser escrita', 16),
    (1, 3, 'Tags, elementos e atributos', 'a diferenca entre tag, elemento, atributo e valor', 'criar elementos com atributos comuns e nomes legiveis', 'um link com href e target, uma imagem com src e alt e um input com name', 'usar atributos sem valor valido ou repetir ids em varios elementos', 'distinguir estrutura do elemento e informacoes adicionais dos atributos', 15),
    (1, 4, 'Textos, titulos e paragrafos', 'a hierarquia de conteudo textual e a importancia dos titulos', 'organizar uma pagina com h1, h2, h3 e paragrafos', 'uma pagina institucional com titulo principal, subtitulos e blocos de texto', 'escolher h1, h2 e h3 pelo tamanho visual em vez da hierarquia', 'montar textos com sequencia logica para leitura humana e tecnica', 14),
    (1, 5, 'Links e navegacao basica', 'a criacao de links internos, externos e ancoras', 'montar um menu simples de navegacao entre secoes', 'um sumario que leva para secoes da propria pagina e para paginas externas', 'usar textos genericos como clique aqui sem indicar o destino do link', 'criar navegacao compreensivel e previsivel entre conteudos', 15),
    (1, 6, 'Imagens e caminhos de arquivos', 'o uso de img, src, alt e caminhos relativos', 'inserir imagens com texto alternativo adequado', 'uma galeria simples com imagens armazenadas em uma pasta assets', 'deixar alt vazio em imagens informativas ou usar caminho quebrado', 'exibir imagens corretamente e descrever sua funcao no conteudo', 15),
    (1, 7, 'Listas ordenadas e nao ordenadas', 'a representacao de sequencias, grupos e estruturas repetidas', 'criar listas de conteudo, etapas e recursos', 'uma receita com etapas numeradas e uma lista de ingredientes sem ordem fixa', 'usar varios paragrafos quando uma lista comunica melhor a relacao dos itens', 'selecionar ol ou ul conforme a necessidade de ordem', 12),
    (1, 8, 'Organizacao com div e span', 'o uso de elementos genericos quando nao existe semantica especifica', 'separar blocos e trechos inline com clareza', 'um card de produto usando div para bloco e span para pequenos trechos', 'usar div para tudo mesmo quando existe header, nav, main ou section', 'usar elementos genericos apenas quando eles fizerem sentido', 13),
    (1, 9, 'Comentarios e legibilidade do codigo', 'a importancia da leitura do codigo e da organizacao visual', 'adicionar comentarios uteis sem poluir o documento', 'comentarios curtos separando cabecalho, conteudo principal e rodape', 'explicar obviedades ou esconder codigo antigo em comentarios permanentes', 'manter um HTML facil de revisar por voce e por outras pessoas', 11),
    (1, 10, 'Primeira pagina completa em HTML 5', 'a integracao dos elementos fundamentais em um documento unico', 'criar uma pagina simples com cabecalho, conteudo e links', 'uma pagina de apresentacao pessoal com foto, biografia, links e listas', 'misturar assuntos sem secao clara ou deixar a pagina sem estrutura principal', 'entregar um documento completo, legivel e navegavel', 18),

    (2, 1, 'Elementos semanticos principais', 'o uso de header, nav, main, section, article, aside e footer', 'reestruturar uma pagina usando tags semanticas', 'um blog com cabecalho, menu, artigo, conteudo lateral e rodape', 'substituir todo bloco por section mesmo quando article ou nav seriam melhores', 'escolher elementos conforme o papel do conteudo', 16),
    (2, 2, 'Semantica para leitura humana e maquinas', 'como a semantica ajuda usuarios, navegadores, leitores de tela e buscadores', 'trocar divs genericas por elementos mais significativos', 'uma pagina de curso em que aulas, modulos e acoes possuem estrutura clara', 'acreditar que semantica so importa para SEO e ignorar acessibilidade', 'perceber como significado melhora interpretacao e manutencao', 15),
    (2, 3, 'Tabelas com estrutura correta', 'a criacao de tabelas com caption, thead, tbody, tr, th e td', 'montar uma tabela simples e compreensivel', 'uma tabela de horarios com legenda, cabecalho de colunas e linhas organizadas', 'usar tabela para montar layout visual em vez de dados tabulares', 'criar tabelas legiveis para pessoas e tecnologias assistivas', 16),
    (2, 4, 'Audio e video no HTML 5', 'os elementos nativos de midia e seus atributos principais', 'inserir audio ou video com controles e fallback', 'um video de aula com controls e texto alternativo de apoio', 'ativar autoplay sem necessidade ou esquecer formatos compativeis', 'publicar midias com controle de usuario e fallback minimo', 15),
    (2, 5, 'Iframes e conteudo incorporado', 'a incorporacao de conteudos externos com responsabilidade', 'adicionar um iframe com titulo e dimensoes adequadas', 'um mapa incorporado com title descritivo e largura responsiva', 'incorporar conteudo sem avaliar seguranca, origem e experiencia mobile', 'usar iframe quando ele agrega valor e comunica sua finalidade', 14),
    (2, 6, 'Introducao a formularios', 'a estrutura de form, label, input e button', 'criar um formulario de contato basico', 'um formulario com nome, email, mensagem e botao de envio', 'separar label do campo de forma que o usuario nao saiba o que preencher', 'montar formularios compreensiveis e preparados para envio', 16),
    (2, 7, 'Tipos de input e usabilidade', 'os tipos text, email, number, date, checkbox, radio e file', 'selecionar o input correto para cada dado', 'um cadastro com email validado, data de nascimento e escolhas por radio', 'usar text para todos os campos e perder recursos nativos do navegador', 'melhorar usabilidade escolhendo controles adequados', 17),
    (2, 8, 'Select, textarea e fieldset', 'campos de selecao, textos longos e agrupamento de controles', 'organizar um formulario com grupos logicos', 'um questionario com select de assunto, textarea de mensagem e fieldset de preferencias', 'agrupar campos sem legenda ou usar textarea para respostas muito curtas', 'organizar formularios longos de maneira clara e escaneavel', 15),
    (2, 9, 'Validacao nativa de formularios', 'required, minlength, maxlength, pattern e mensagens do navegador', 'aplicar validacoes simples sem JavaScript', 'um campo de senha com tamanho minimo e um email obrigatorio', 'confiar apenas na validacao do navegador para seguranca do sistema', 'usar validacoes nativas como primeira camada de orientacao', 16),
    (2, 10, 'Formulario completo e semantico', 'a combinacao de campos, rotulos, grupos e validacao basica', 'montar um formulario de cadastro organizado', 'um cadastro de aluno com dados pessoais, contato e aceite de termos', 'criar formulario grande sem agrupamento, labels ou ordem de preenchimento', 'entregar um formulario funcional, acessivel e facil de preencher', 20),

    (3, 1, 'Acessibilidade no HTML 5', 'a relacao entre estrutura semantica e inclusao digital', 'revisar uma pagina pensando em leitores de tela', 'uma pagina de aula navegavel por regioes e titulos bem definidos', 'tratar acessibilidade como etapa opcional feita apenas no final', 'identificar melhorias estruturais que incluem mais usuarios', 16),
    (3, 2, 'Textos alternativos e nomes acessiveis', 'como alt, labels e textos de links influenciam a compreensao', 'melhorar imagens, botoes e links com textos claros', 'um botao de download com texto objetivo e uma imagem com alt informativo', 'repetir no alt exatamente o texto ao lado ou escrever descricoes vagas', 'criar nomes acessiveis que expliquem funcao e contexto', 15),
    (3, 3, 'Ordem de leitura e navegacao por teclado', 'a importancia da sequencia natural do documento', 'testar a navegacao usando apenas teclado', 'um formulario que segue ordem logica do primeiro ao ultimo campo', 'alterar a ordem visual com CSS e deixar o HTML confuso para teclado', 'garantir que a sequencia de interacao faca sentido', 14),
    (3, 4, 'Metadados essenciais no head', 'title, meta charset, viewport e description', 'configurar o head de uma pagina real', 'um head com codificacao, responsividade, titulo e descricao da pagina', 'duplicar titles ou esquecer viewport em paginas que serao abertas no celular', 'preparar a pagina para exibicao correta e compartilhamento basico', 15),
    (3, 5, 'HTML 5 e SEO basico', 'como titulos, semantica e descricao ajudam buscadores', 'ajustar uma pagina para melhor interpretacao', 'uma pagina de curso com titulo unico, descricao clara e secoes organizadas', 'repetir palavras-chave artificialmente e prejudicar a leitura natural', 'estruturar conteudo que seja util para usuarios e compreensivel para busca', 16),
    (3, 6, 'Boas praticas de nomes e pastas', 'organizacao de arquivos, imagens e paginas do projeto', 'criar uma estrutura de pastas simples e previsivel', 'um projeto com index.html, pasta assets e nomes de arquivos em minusculas', 'usar nomes com espacos, acentos ou versoes finais confusas', 'manter um projeto facil de publicar, mover e revisar', 13),
    (3, 7, 'Validacao do HTML', 'a verificacao de erros de sintaxe e estrutura', 'usar um validador para revisar o documento', 'uma pagina corrigida apos identificar tag sem fechamento e atributo invalido', 'ignorar avisos do validador por achar que o navegador ja exibiu a pagina', 'corrigir problemas antes que eles afetem manutencao e acessibilidade', 14),
    (3, 8, 'Preparacao para CSS e JavaScript', 'como classes, ids e estrutura limpa facilitam evolucao', 'preparar marcacao para receber estilos e interacao', 'um botao com classe clara e uma secao pronta para comportamento dinamico', 'usar ids para tudo ou nomes de classes que descrevem apenas cor e tamanho', 'escrever HTML que possa evoluir sem reestruturacao completa', 15),
    (3, 9, 'Publicacao de paginas estaticas', 'conceitos de hospedagem, arquivo inicial e caminho publico', 'organizar uma pagina para publicacao simples', 'um projeto pronto para hospedagem com index.html na raiz', 'usar caminhos locais do computador que quebram quando o site e publicado', 'preparar arquivos para funcionar fora do ambiente de desenvolvimento', 15),
    (3, 10, 'Projeto final em HTML 5', 'a construcao de uma pagina completa com semantica, acessibilidade e organizacao', 'entregar uma pagina final revisada e validada', 'uma pagina de portifolio ou curso com cabecalho, conteudo, formulario e rodape', 'entregar apenas trechos soltos sem fluxo de leitura e sem validacao', 'concluir uma pagina HTML 5 consistente, organizada e pronta para evoluir', 22)
),
aulas_payload as (
  select
    c.id as turma_id,
    tm.id as modulo_id,
    a.titulo,
    a.aula_ordem,
    a.duracao_minutos,
  array_to_string(array[
    format('Objetivo: compreender %s.', a.foco),
    format('Aplicacao em HTML 5: %s.', a.exemplo),
    format('Cuidado importante: %s.', a.cuidado),
    format('Pratica da aula: %s.', a.pratica),
    format('Ao concluir, voce deve conseguir %s.', a.resultado)
  ], E'\n') as descricao
  from curso c
  join public.turma_modulo tm on tm.turma_id = c.id
  join aulas a on a.modulo_ordem = tm.ordem
),
aulas_atualizadas as (
  update public.turma_aula ta
  set
    titulo = ap.titulo,
    descricao = ap.descricao,
    duracao_minutos = ap.duracao_minutos,
    video_url = null,
    active = true
  from aulas_payload ap
  where ta.turma_id = ap.turma_id
    and ta.modulo_id = ap.modulo_id
    and ta.ordem = ap.aula_ordem
  returning ta.id
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
  ap.turma_id,
  ap.modulo_id,
  ap.titulo,
  ap.descricao,
  ap.duracao_minutos,
  ap.aula_ordem,
  null,
  true
from aulas_payload ap
where not exists (
  select 1
  from public.turma_aula ta
  where ta.turma_id = ap.turma_id
    and ta.modulo_id = ap.modulo_id
    and ta.ordem = ap.aula_ordem
);

commit;
