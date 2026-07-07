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
    format('Objetivo da aula: compreender %s.', a.foco),
    case a.modulo_ordem
      when 1 then 'Neste modulo, a aula fortalece a base de estrutura: primeiro o documento, depois os elementos, depois a pagina completa.'
      when 2 then 'Neste modulo, a aula aproxima o HTML de telas reais, com semantica, midias, tabelas e formularios.'
      else 'Neste modulo, a aula trata da qualidade final do projeto: acessibilidade, SEO, validacao, organizacao e publicacao.'
    end,
    case a.modulo_ordem
      when 1 then 'A pergunta guia e: esta marcacao deixa claro o que cada parte do conteudo representa?'
      when 2 then 'A pergunta guia e: este elemento ajuda o usuario a entender, consumir ou preencher melhor a interface?'
      else 'A pergunta guia e: esta pagina seria compreensivel, acessivel e facil de publicar fora do meu computador?'
    end,
    format('Contexto pratico: %s.', a.exemplo),
    format('Ponto de atencao: %s.', a.cuidado),
    case a.aula_ordem
      when 1 then 'Comece observando o problema geral da aula e identifique onde ele aparece em sites que voce usa no dia a dia.'
      when 2 then 'Antes de codificar, desenhe mentalmente a estrutura que sera criada e separe configuracao, conteudo e interacao.'
      when 3 then 'Leia cada elemento com calma e perceba quais informacoes sao obrigatorias para que ele tenha sentido.'
      when 4 then 'Priorize a ordem de leitura, pois titulos e textos sao o caminho principal para entender a pagina.'
      when 5 then 'Pense no fluxo de navegacao: cada acao deve indicar para onde o usuario sera levado.'
      when 6 then 'Avalie se o recurso visual ou de arquivo esta realmente ajudando o conteudo a ficar mais claro.'
      when 7 then 'Organize informacoes repetidas em grupos, evitando misturar lista, paragrafo e tabela sem necessidade.'
      when 8 then 'Use agrupamentos para melhorar manutencao, mas sem transformar todo o documento em blocos genericos.'
      when 9 then 'Revise o codigo como se outra pessoa fosse continuar o projeto depois de voce.'
      else 'Una os conhecimentos anteriores e procure entregar uma pagina que funcione como um pequeno projeto completo.'
    end,
    case a.aula_ordem
      when 1 then 'O primeiro passo pratico e abrir um arquivo simples e reconhecer a responsabilidade de cada camada da web.'
      when 2 then 'O segundo passo e montar a estrutura minima e conferir se o navegador interpreta o documento corretamente.'
      when 3 then 'O terceiro passo e testar pequenas variacoes de atributos para entender o efeito de cada um.'
      when 4 then 'O quarto passo e transformar texto solto em uma hierarquia compreensivel.'
      when 5 then 'O quinto passo e criar caminhos entre secoes, paginas ou recursos externos.'
      when 6 then 'O sexto passo e validar se caminhos de arquivo, formatos e descricoes fazem sentido.'
      when 7 then 'O setimo passo e escolher a estrutura de lista ou agrupamento que melhor representa os dados.'
      when 8 then 'O oitavo passo e separar blocos apenas quando isso simplificar a leitura do codigo.'
      when 9 then 'O nono passo e limpar excesso de comentarios e melhorar a indentacao.'
      else 'O decimo passo e revisar o conjunto e corrigir incoerencias antes de considerar a aula concluida.'
    end,
    case a.modulo_ordem
      when 1 then 'Ao praticar, mantenha o foco em tags essenciais, hierarquia e caminhos de arquivo.'
      when 2 then 'Ao praticar, mantenha o foco em significado, controle do usuario e organizacao dos campos.'
      else 'Ao praticar, mantenha o foco em revisao, compatibilidade, clareza e preparo para publicacao.'
    end,
    case a.modulo_ordem
      when 1 then 'Uma pagina bem estruturada neste ponto ainda pode ser simples visualmente, mas ja deve ser coerente no codigo.'
      when 2 then 'Uma pagina bem construida neste ponto deve favorecer leitura, preenchimento e interpretacao por ferramentas.'
      else 'Uma pagina bem finalizada neste ponto deve ser validada, acessivel e preparada para evoluir com outras tecnologias.'
    end,
    case a.aula_ordem
      when 1 then 'Compare a pagina pronta com um exemplo real e identifique quais partes poderiam receber tags mais especificas.'
      when 2 then 'Confira se a divisao do arquivo evita confusao entre metadados e conteudo visivel.'
      when 3 then 'Verifique se os atributos usados possuem nomes, valores e finalidades compreensiveis.'
      when 4 then 'Confira se existe apenas um titulo principal e se os subtitulos seguem uma ordem natural.'
      when 5 then 'Teste todos os links e veja se o texto de cada um antecipa corretamente o destino.'
      when 6 then 'Abra a pagina sem uma imagem carregada e veja se o texto alternativo ainda comunica a informacao.'
      when 7 then 'Troque temporariamente a ordem dos itens e perceba se isso muda ou nao o sentido da lista.'
      when 8 then 'Substitua um bloco generico por uma tag semantica quando houver uma funcao clara.'
      when 9 then 'Remova comentarios que nao ajudam na decisao tecnica e mantenha apenas os que orientam a leitura.'
      else 'Revise o projeto completo usando o navegador e o editor lado a lado.'
    end,
    case a.modulo_ordem
      when 1 then 'Checklist do modulo: documento valido, conteudo bem dividido, nomes claros e relacao correta entre arquivos.'
      when 2 then 'Checklist do modulo: semantica adequada, controles compreensiveis, midias com fallback e formularios bem rotulados.'
      else 'Checklist do modulo: metadados revisados, navegacao por teclado testada, HTML validado e estrutura pronta para hospedagem.'
    end,
    case a.aula_ordem
      when 1 then 'Pergunta de revisao: qual problema esta aula resolve antes de qualquer preocupacao com aparencia?'
      when 2 then 'Pergunta de revisao: o que precisa ficar dentro da configuracao do documento e o que pertence ao conteudo?'
      when 3 then 'Pergunta de revisao: qual atributo muda o comportamento do elemento e qual apenas complementa informacao?'
      when 4 then 'Pergunta de revisao: a hierarquia de titulos permite entender a pagina apenas olhando o sumario?'
      when 5 then 'Pergunta de revisao: os links ajudam o usuario a prever o destino antes do clique?'
      when 6 then 'Pergunta de revisao: se o arquivo externo falhar, o conteudo principal continua compreensivel?'
      when 7 then 'Pergunta de revisao: a ordem dos itens altera o significado ou apenas organiza visualmente?'
      when 8 then 'Pergunta de revisao: este agrupamento melhora a estrutura ou apenas esconde falta de semantica?'
      when 9 then 'Pergunta de revisao: o codigo explica sua intencao pela propria organizacao?'
      else 'Pergunta de revisao: o conjunto final representa uma pagina completa e coerente?'
    end,
    case a.modulo_ordem
      when 1 then 'Erro comum do modulo: escrever HTML pensando apenas no resultado visual imediato.'
      when 2 then 'Erro comum do modulo: adicionar recursos de interface sem cuidar de rotulos, ordem e significado.'
      else 'Erro comum do modulo: publicar ou finalizar sem testar leitura, caminhos, metadados e validacao.'
    end,
    case a.aula_ordem
      when 1 then 'Dica pratica: explique o conceito com suas palavras antes de abrir o editor.'
      when 2 then 'Dica pratica: use um arquivo pequeno para testar a estrutura antes de expandir a pagina.'
      when 3 then 'Dica pratica: altere um atributo por vez e observe o impacto no navegador.'
      when 4 then 'Dica pratica: leia apenas os titulos e veja se eles contam a historia da pagina.'
      when 5 then 'Dica pratica: passe o mouse e use o teclado para testar se a navegacao e clara.'
      when 6 then 'Dica pratica: mantenha arquivos em pastas previsiveis e confira caminhos relativos.'
      when 7 then 'Dica pratica: escolha lista quando os itens pertencem ao mesmo grupo de informacao.'
      when 8 then 'Dica pratica: use elementos genericos como apoio, nao como substitutos de toda semantica.'
      when 9 then 'Dica pratica: prefira comentarios curtos que expliquem decisoes, nao linhas obvias.'
      else 'Dica pratica: revise como usuario e como desenvolvedor antes de concluir.'
    end,
    case a.modulo_ordem
      when 1 then 'Conexao com a proxima etapa: uma estrutura limpa facilita aplicar estilos sem reorganizar tudo.'
      when 2 then 'Conexao com a proxima etapa: interfaces bem marcadas ficam mais faceis de validar, estilizar e automatizar.'
      else 'Conexao com a proxima etapa: um projeto validado e organizado pode receber CSS, JavaScript e publicacao com menos retrabalho.'
    end,
    case a.aula_ordem
      when 1 then 'Mini-entrega: uma explicacao curta do papel do recurso estudado dentro de uma pagina.'
      when 2 then 'Mini-entrega: um arquivo inicial que possa servir de modelo para novas paginas.'
      when 3 then 'Mini-entrega: exemplos de elementos com atributos corretos e finalidade clara.'
      when 4 then 'Mini-entrega: um bloco textual com titulos e paragrafos bem encadeados.'
      when 5 then 'Mini-entrega: um pequeno conjunto de links internos ou externos funcionando.'
      when 6 then 'Mini-entrega: uma imagem ou arquivo externo referenciado corretamente.'
      when 7 then 'Mini-entrega: uma lista adequada ao tipo de informacao apresentada.'
      when 8 then 'Mini-entrega: uma estrutura de blocos simples sem excesso de elementos genericos.'
      when 9 then 'Mini-entrega: um trecho de codigo revisado e mais legivel que a primeira versao.'
      else 'Mini-entrega: uma pagina consolidada com os principais pontos da sequencia.'
    end,
    case a.modulo_ordem
      when 1 then 'Sinal de qualidade: mesmo sem CSS, a pagina deve revelar sua organizacao e seu objetivo.'
      when 2 then 'Sinal de qualidade: os elementos devem orientar leitura, preenchimento e consumo de midia sem ambiguidade.'
      else 'Sinal de qualidade: a pagina deve resistir a revisao tecnica sem depender de explicacoes externas.'
    end,
    format('Exercicio recomendado: %s.', a.pratica),
    format('Resultado esperado: %s.', a.resultado),
    case a.modulo_ordem
      when 1 then 'Criterio de conclusao: a estrutura deve abrir corretamente no navegador e demonstrar dominio dos elementos basicos.'
      when 2 then 'Criterio de conclusao: o recurso estudado deve funcionar e estar marcado de forma semantica e compreensivel.'
      else 'Criterio de conclusao: a pagina deve estar revisada, organizada e pronta para validacao ou publicacao.'
    end,
    'Ao finalizar, salve uma versao do arquivo e anote uma melhoria concreta para aplicar na proxima aula.'
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
