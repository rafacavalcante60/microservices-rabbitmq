-- Executado uma única vez, na primeira subida do contêiner (volume vazio).
--
-- D-9: uma instância PostgreSQL, dois bancos lógicos com usuários distintos.
-- O princípio III da constituição diz que nenhum serviço lê o banco de outro.
-- Aqui isso não é combinado, é imposto: cada usuário só consegue conectar no
-- próprio banco. Se o OrderService tentar ler `payments_db`, o Postgres recusa
-- a conexão — o erro aparece no primeiro teste, não seis meses depois.

CREATE USER orders_user   WITH PASSWORD 'orders_pw';
CREATE USER payments_user WITH PASSWORD 'payments_pw';

CREATE DATABASE orders_db   OWNER orders_user;
CREATE DATABASE payments_db OWNER payments_user;

-- Por padrão qualquer usuário do cluster pode conectar em qualquer banco.
-- Sem estas quatro linhas a separação acima seria decorativa.
REVOKE CONNECT ON DATABASE orders_db   FROM PUBLIC;
REVOKE CONNECT ON DATABASE payments_db FROM PUBLIC;
GRANT  CONNECT ON DATABASE orders_db   TO orders_user;
GRANT  CONNECT ON DATABASE payments_db TO payments_user;
