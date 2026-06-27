-- Seed de perguntas faceis sobre curiosidades para Supabase/PostgreSQL
-- Execute depois do script 06_create_pergunta_alternativa.sql

begin;

with p1 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual planeta e conhecido como planeta vermelho?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p1
cross join (
  values
    ('Marte', true, 1),
    ('Venus', false, 2),
    ('Jupiter', false, 3),
    ('Mercurio', false, 4)
) as alternativas(texto, correta, ordem);

with p2 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Quantos continentes existem no modelo mais usado no Brasil?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p2
cross join (
  values
    ('Seis', true, 1),
    ('Quatro', false, 2),
    ('Cinco', false, 3),
    ('Oito', false, 4)
) as alternativas(texto, correta, ordem);

with p3 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual e o maior animal terrestre?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p3
cross join (
  values
    ('Elefante africano', true, 1),
    ('Rinoceronte branco', false, 2),
    ('Girafa', false, 3),
    ('Hipopotamo', false, 4)
) as alternativas(texto, correta, ordem);

with p4 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual instrumento e usado para medir a temperatura?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p4
cross join (
  values
    ('Termometro', true, 1),
    ('Barometro', false, 2),
    ('Velocimetro', false, 3),
    ('Bussola', false, 4)
) as alternativas(texto, correta, ordem);

with p5 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual e o oceano mais extenso do planeta?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p5
cross join (
  values
    ('Oceano Pacifico', true, 1),
    ('Oceano Atlantico', false, 2),
    ('Oceano Indico', false, 3),
    ('Oceano Artico', false, 4)
) as alternativas(texto, correta, ordem);

with p6 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual metal e conhecido pelo simbolo quimico Au?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p6
cross join (
  values
    ('Ouro', true, 1),
    ('Prata', false, 2),
    ('Aluminio', false, 3),
    ('Cobre', false, 4)
) as alternativas(texto, correta, ordem);

with p7 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual e a capital da Franca?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p7
cross join (
  values
    ('Paris', true, 1),
    ('Lyon', false, 2),
    ('Marselha', false, 3),
    ('Nice', false, 4)
) as alternativas(texto, correta, ordem);

with p8 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual sentido humano esta relacionado ao nariz?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p8
cross join (
  values
    ('Olfato', true, 1),
    ('Paladar', false, 2),
    ('Audicao', false, 3),
    ('Visao', false, 4)
) as alternativas(texto, correta, ordem);

with p9 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual e o nome do satelite natural da Terra?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p9
cross join (
  values
    ('Lua', true, 1),
    ('Sol', false, 2),
    ('Marte', false, 3),
    ('Estrela Polar', false, 4)
) as alternativas(texto, correta, ordem);

with p10 as (
  insert into public.pergunta (enunciado, dificuldade, status)
  values ('Qual cor se forma ao misturar azul e amarelo?', 'Facil', 'Ativa')
  returning id
)
insert into public.alternativa (pergunta_id, texto, correta, ordem)
select id, texto, correta, ordem
from p10
cross join (
  values
    ('Verde', true, 1),
    ('Roxo', false, 2),
    ('Laranja', false, 3),
    ('Vermelho', false, 4)
) as alternativas(texto, correta, ordem);

commit;
