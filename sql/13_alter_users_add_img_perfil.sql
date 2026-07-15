-- Adiciona o campo de imagem de perfil na tabela public.users
-- Supabase/PostgreSQL

alter table if exists public.users
  add column if not exists img_perfil text null;

comment on column public.users.img_perfil is
  'Diretorio e nome do arquivo da imagem de perfil salva para o usuario.';
