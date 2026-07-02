-- Seed da modalidade "Ciências Exatas" e do curso "Educação Financeira"
-- Supabase/PostgreSQL

begin;

insert into public.modalidade (course_name)
select 'Ciências Exatas'
where not exists (
  select 1
  from public.modalidade
  where lower(course_name) = lower('Ciências Exatas')
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
  'Educação Financeira',
  m.id,
  current_date,
  current_date + interval '90 days',
  true,
  current_date,
  current_date + interval '30 days',
  null,
  'Curso introdutório de educação financeira com foco em organização do orçamento, consumo consciente, crédito, investimentos básicos e planejamento de metas. A proposta é desenvolver autonomia para tomar decisões financeiras mais claras no cotidiano.',
  0
from public.modalidade m
where lower(m.course_name) = lower('Ciências Exatas')
  and not exists (
    select 1
    from public.turma t
    where lower(t.nome_turma) = lower('Educação Financeira')
      and t.modalidade_id = m.id
  );

with curso as (
  select t.id
  from public.turma t
  join public.modalidade m on m.id = t.modalidade_id
  where lower(t.nome_turma) = lower('Educação Financeira')
    and lower(m.course_name) = lower('Ciências Exatas')
  order by t.id
  limit 1
),
modulos(titulo, descricao, ordem) as (
  values
    ('Fundamentos da vida financeira', 'Apresenta os conceitos essenciais para entender dinheiro, renda, despesas e escolhas financeiras. O módulo ajuda o estudante a perceber como hábitos cotidianos influenciam o equilíbrio financeiro.', 1),
    ('Orçamento pessoal e familiar', 'Explora métodos de controle financeiro, organização de gastos e construção de um orçamento realista. O objetivo é transformar registros simples em decisões mais conscientes.', 2),
    ('Consumo, crédito e endividamento', 'Discute consumo planejado, uso de crédito, juros e riscos do endividamento. O módulo mostra como comparar opções e evitar compromissos financeiros acima da capacidade de pagamento.', 3),
    ('Investimentos e formação de patrimônio', 'Introduz reserva de emergência, renda fixa, renda variável e relação entre risco e retorno. O foco é criar uma base segura para começar a investir com objetivos definidos.', 4),
    ('Planejamento financeiro e projeto de vida', 'Conecta metas pessoais, carreira, família e futuro financeiro. O módulo orienta a criação de planos de curto, médio e longo prazo com acompanhamento periódico.', 5)
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
  where lower(t.nome_turma) = lower('Educação Financeira')
    and lower(m.course_name) = lower('Ciências Exatas')
  order by t.id
  limit 1
),
aulas(modulo_ordem, aula_ordem, titulo, descricao, duracao_minutos) as (
  values
    (1, 1, 'O papel do dinheiro na vida cotidiana', 'O dinheiro funciona como meio de troca, unidade de conta e reserva de valor, mas seu uso depende das escolhas feitas no dia a dia. Entender essa função ajuda a perceber que cada compra envolve prioridades, limites e consequências para o orçamento.', 12),
    (1, 2, 'Renda, despesas e saldo financeiro', 'A renda representa os recursos que entram, enquanto as despesas registram tudo o que sai para manter necessidades e desejos. O saldo financeiro mostra se a pessoa está gastando menos, igual ou mais do que recebe, sendo o primeiro indicador de equilíbrio.', 14),
    (1, 3, 'Necessidades, desejos e prioridades', 'Necessidades são gastos ligados à sobrevivência e segurança, enquanto desejos expressam preferências e conforto. A educação financeira não elimina desejos, mas ensina a organizá-los para que não prejudiquem compromissos importantes.', 13),
    (1, 4, 'Hábitos financeiros e comportamento', 'Pequenas decisões repetidas criam padrões de consumo, economia ou endividamento. Observar hábitos permite identificar compras impulsivas, desperdícios e oportunidades de melhorar o uso do dinheiro sem mudanças bruscas.', 15),
    (1, 5, 'Custo de oportunidade', 'Ao escolher uma alternativa, abre-se mão de outras possibilidades, e esse valor é chamado custo de oportunidade. Esse conceito ajuda a comparar decisões, como gastar imediatamente ou guardar dinheiro para uma meta futura.', 12),
    (1, 6, 'Inflação e poder de compra', 'A inflação reduz o poder de compra quando os preços aumentam ao longo do tempo. Mesmo que a renda permaneça igual, uma pessoa pode comprar menos produtos e serviços, por isso é importante acompanhar preços e ajustar o planejamento.', 14),
    (1, 7, 'Organização de documentos financeiros', 'Comprovantes, contratos, faturas e extratos ajudam a entender compromissos assumidos e evitar atrasos. Manter esses documentos organizados facilita o controle, a contestação de cobranças e a tomada de decisões.', 11),
    (1, 8, 'Educação financeira como aprendizagem contínua', 'Cuidar do dinheiro exige atualização constante, pois renda, objetivos e contexto econômico mudam com o tempo. A aprendizagem financeira é um processo prático, baseado em observar, planejar, agir e revisar resultados.', 13),
    (1, 9, 'Indicadores simples de saúde financeira', 'Alguns sinais ajudam a avaliar a situação financeira, como pagar contas em dia, ter reserva, controlar dívidas e guardar parte da renda. Esses indicadores não dependem apenas de ganhar muito, mas de administrar bem o que se recebe.', 14),
    (1, 10, 'Primeiro diagnóstico financeiro', 'O diagnóstico financeiro reúne renda, despesas, dívidas, bens e objetivos. Ele cria uma fotografia da situação atual e permite escolher prioridades para iniciar mudanças com clareza e realismo.', 16),

    (2, 1, 'Por que fazer um orçamento', 'O orçamento é uma ferramenta para planejar o uso da renda antes que o dinheiro seja gasto. Ele ajuda a reduzir improvisos, prever compromissos e separar recursos para metas importantes.', 12),
    (2, 2, 'Categorias de gastos', 'Separar gastos em moradia, alimentação, transporte, educação, lazer e dívidas facilita a análise do orçamento. Essa classificação mostra quais áreas consomem mais recursos e onde pode haver ajustes.', 13),
    (2, 3, 'Gastos fixos e variáveis', 'Gastos fixos costumam ter valor previsível, como aluguel ou mensalidade, enquanto gastos variáveis mudam conforme o consumo. Entender essa diferença ajuda a planejar o essencial e controlar aquilo que depende mais de escolhas.', 12),
    (2, 4, 'Registro diário de despesas', 'Registrar despesas diariamente evita esquecimentos e mostra detalhes que não aparecem em uma análise superficial. Pequenos gastos frequentes podem representar uma parcela relevante do orçamento mensal.', 15),
    (2, 5, 'Método 50-30-20', 'O método 50-30-20 sugere dividir a renda entre necessidades, desejos e objetivos financeiros. Ele serve como referência inicial, mas pode ser adaptado conforme renda, família, dívidas e fase de vida.', 14),
    (2, 6, 'Planejamento de contas do mês', 'Planejar contas do mês envolve listar vencimentos, valores e prioridades de pagamento. Essa prática reduz atrasos, juros e sensação de descontrole, além de permitir uma visão antecipada das despesas.', 13),
    (2, 7, 'Controle familiar compartilhado', 'Em uma família, o orçamento funciona melhor quando as pessoas conhecem limites e combinam responsabilidades. Conversas claras sobre dinheiro reduzem conflitos e alinham decisões com objetivos comuns.', 14),
    (2, 8, 'Cortes inteligentes de gastos', 'Cortar gastos não significa retirar tudo o que traz bem-estar, mas identificar desperdícios e substituir escolhas caras por alternativas viáveis. Pequenas mudanças consistentes geram economia sem comprometer necessidades importantes.', 13),
    (2, 9, 'Acompanhamento semanal do orçamento', 'Revisar o orçamento semanalmente permite corrigir excessos antes do fim do mês. Esse acompanhamento transforma o planejamento em uma ferramenta viva, mais útil do que uma lista feita apenas uma vez.', 12),
    (2, 10, 'Revisão mensal e ajustes', 'Ao fim do mês, comparar o planejado com o realizado mostra acertos, falhas e tendências. A revisão mensal ajuda a ajustar metas, prever meses mais caros e melhorar decisões futuras.', 15),

    (3, 1, 'Consumo consciente', 'Consumir conscientemente significa avaliar necessidade, preço, qualidade e impacto antes da compra. Essa postura reduz compras por impulso e favorece escolhas alinhadas ao orçamento e aos valores pessoais.', 13),
    (3, 2, 'Compra por impulso', 'A compra por impulso ocorre quando a decisão é tomada rapidamente, muitas vezes por emoção, promoção ou pressão social. Criar um intervalo antes de comprar ajuda a distinguir desejo passageiro de necessidade real.', 12),
    (3, 3, 'Como comparar preços', 'Comparar preços envolve observar valor total, frete, garantia, qualidade e condições de pagamento. Nem sempre o menor preço é a melhor escolha, especialmente quando há custos ocultos ou baixa durabilidade.', 14),
    (3, 4, 'Funcionamento do crédito', 'O crédito permite usar recursos hoje e pagar no futuro, mas envolve responsabilidade e custo. Antes de contratar, é necessário verificar parcelas, juros, prazo e impacto no orçamento mensal.', 15),
    (3, 5, 'Juros simples e compostos', 'Juros simples crescem sobre o valor inicial, enquanto juros compostos crescem sobre o valor acumulado. No crédito, os juros compostos podem ampliar dívidas rapidamente; nos investimentos, podem favorecer o crescimento do patrimônio.', 16),
    (3, 6, 'Cartão de crédito com responsabilidade', 'O cartão de crédito pode organizar pagamentos e oferecer benefícios, mas exige controle do limite e pagamento integral da fatura. Usá-lo como extensão da renda aumenta o risco de endividamento.', 14),
    (3, 7, 'Empréstimos e financiamentos', 'Empréstimos e financiamentos devem ser avaliados pelo custo efetivo total, não apenas pelo valor da parcela. Prazos longos podem reduzir a parcela mensal, mas aumentar bastante o valor pago ao final.', 15),
    (3, 8, 'Sinais de endividamento perigoso', 'Atrasar contas, usar crédito para pagar despesas básicas e comprometer grande parte da renda são sinais de alerta. Identificar esses sinais cedo permite renegociar e reorganizar prioridades antes que a situação piore.', 13),
    (3, 9, 'Renegociação de dívidas', 'Renegociar dívidas exige conhecer o saldo, os juros, a capacidade de pagamento e as condições oferecidas. Um bom acordo deve caber no orçamento para evitar novo atraso logo após a renegociação.', 14),
    (3, 10, 'Plano para sair das dívidas', 'Sair das dívidas envolve listar débitos, priorizar os mais caros, cortar gastos temporários e evitar novos compromissos. A disciplina do plano é tão importante quanto a negociação com credores.', 15),

    (4, 1, 'Reserva de emergência', 'A reserva de emergência protege contra imprevistos como perda de renda, problemas de saúde ou reparos urgentes. Ela deve ficar em aplicações seguras e acessíveis, pois seu objetivo principal é liquidez e estabilidade.', 14),
    (4, 2, 'Perfil de investidor', 'O perfil de investidor considera objetivos, prazo, tolerância a risco e experiência. Conhecer esse perfil ajuda a escolher aplicações compatíveis com a realidade financeira e emocional da pessoa.', 13),
    (4, 3, 'Risco, retorno e liquidez', 'Todo investimento combina risco, retorno e liquidez em diferentes proporções. Buscar retorno maior geralmente envolve aceitar mais risco ou abrir mão de acesso imediato ao dinheiro.', 15),
    (4, 4, 'Introdução à renda fixa', 'Renda fixa reúne investimentos com regras de remuneração mais previsíveis, como títulos públicos, CDBs e outros produtos. Ela costuma ser usada para reserva, objetivos de curto prazo e construção inicial de patrimônio.', 14),
    (4, 5, 'Tesouro Direto em linguagem simples', 'O Tesouro Direto permite investir em títulos públicos federais, emprestando dinheiro ao governo em troca de remuneração. Existem títulos com características diferentes, ligados à taxa Selic, à inflação ou a juros prefixados.', 16),
    (4, 6, 'CDB, LCI e LCA', 'CDB, LCI e LCA são produtos emitidos por instituições financeiras para captar recursos. Eles podem ter cobertura do FGC dentro de limites definidos, mas devem ser comparados por rentabilidade, prazo e liquidez.', 15),
    (4, 7, 'Introdução à renda variável', 'Renda variável inclui investimentos cujo retorno não é garantido, como ações e fundos imobiliários. Ela pode participar da formação de patrimônio, mas exige diversificação, horizonte maior e controle emocional.', 15),
    (4, 8, 'Diversificação', 'Diversificar significa distribuir recursos entre diferentes tipos de investimento para reduzir dependência de um único resultado. A diversificação não elimina riscos, mas pode suavizar perdas e melhorar a consistência do plano.', 14),
    (4, 9, 'Custos e impostos nos investimentos', 'Taxas, impostos e custos operacionais reduzem a rentabilidade líquida. Antes de investir, é importante entender cobranças, prazos de tributação e regras de cada produto financeiro.', 13),
    (4, 10, 'Primeira carteira de investimentos', 'Uma primeira carteira pode começar pela reserva de emergência e evoluir para objetivos com prazos diferentes. A composição deve respeitar renda, metas, conhecimento e capacidade de lidar com oscilações.', 16),

    (5, 1, 'Metas financeiras claras', 'Metas financeiras precisam ter valor, prazo e motivo definidos para orientar decisões. Quando a meta é concreta, fica mais fácil separar recursos e medir o avanço ao longo do tempo.', 12),
    (5, 2, 'Objetivos de curto, médio e longo prazo', 'Objetivos de curto prazo exigem segurança e liquidez, enquanto objetivos longos podem aceitar estratégias diferentes. Separar prazos evita usar dinheiro de uma meta importante para resolver gastos imediatos sem planejamento.', 14),
    (5, 3, 'Planejamento de carreira e renda', 'A renda pode crescer com qualificação, experiência, mudança de função ou novas fontes de trabalho. Planejar carreira também é parte da educação financeira, pois amplia possibilidades além do corte de gastos.', 15),
    (5, 4, 'Proteção financeira da família', 'Proteção financeira envolve reserva, seguros adequados, organização de documentos e prevenção de riscos. O objetivo é reduzir impactos de eventos inesperados sobre a estabilidade da família.', 14),
    (5, 5, 'Grandes compras e decisões importantes', 'Comprar um imóvel, veículo ou equipamento caro exige comparação, entrada, parcelas, custos adicionais e impacto de longo prazo. Decisões grandes devem ser planejadas com calma e simulações realistas.', 15),
    (5, 6, 'Educação financeira para jovens', 'Jovens podem aprender sobre dinheiro ao administrar mesada, pequenas rendas ou objetivos simples. Começar cedo fortalece hábitos de poupança, comparação de preços e responsabilidade com escolhas.', 12),
    (5, 7, 'Finanças e qualidade de vida', 'Organização financeira não serve apenas para acumular dinheiro, mas para reduzir ansiedade e apoiar escolhas de vida. Equilíbrio financeiro inclui lazer, descanso e segurança dentro de limites sustentáveis.', 13),
    (5, 8, 'Acompanhamento de metas', 'Acompanhar metas permite verificar se os depósitos estão ocorrendo e se o prazo continua viável. Quando a realidade muda, a meta pode ser ajustada sem abandonar o planejamento.', 12),
    (5, 9, 'Revisão anual do plano financeiro', 'Uma revisão anual considera mudanças de renda, família, inflação, dívidas, investimentos e objetivos. Esse momento ajuda a corrigir rumos e definir prioridades para o novo ciclo.', 14),
    (5, 10, 'Projeto financeiro pessoal', 'O projeto financeiro pessoal reúne diagnóstico, orçamento, reserva, metas e plano de investimentos. Ele transforma conhecimentos isolados em um caminho prático para tomar decisões melhores ao longo da vida.', 16)
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
  a.descricao,
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
