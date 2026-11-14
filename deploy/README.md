# Implantar em um servidor Linux

Os arquivos estão preparados para uma única instância da API com SQLite.
Você precisa de um servidor Linux com Docker Engine e Docker Compose v2,
um domínio e acesso SSH. Não precisa instalar .NET ou Rust no servidor.

## 1. Preparar o acesso

Aponte o registro DNS A de `api.seudominio.com` para o IPv4 público do
servidor. Se criar um registro AAAA, ele também precisa apontar para o
servidor correto. Libere as portas TCP 80 e 443 no firewall do servidor e
no painel da hospedagem. Preserve seu acesso SSH.

Instale Docker seguindo a [documentação oficial](https://docs.docker.com/engine/install/).
Configure o serviço Docker para iniciar com o sistema operacional.

## 2. Copiar e configurar o projeto

Copie o projeto para o servidor, sem `.dotnet`, `bin`, `obj`, `target`,
arquivos `.env` ou bancos locais. Execute os comandos abaixo na raiz do
projeto no servidor:

```bash
cp .env.example .env
chmod 600 .env
openssl rand -base64 48
```

Edite `.env`: preencha `DOMAIN` com seu domínio real, sem `https://`, e
`JWT_KEY` com a chave gerada. Não compartilhe a chave nem a envie ao Git.
Guarde a mesma chave entre reinicializações.

## 3. Iniciar

```bash
docker compose config --quiet
docker compose up -d --build
docker compose ps
docker compose logs --tail=100 api caddy
curl --fail https://api.seudominio.com/health
```

A resposta esperada é `{"status":"ok"}`. O Caddy emite e renova o
certificado HTTPS automaticamente quando DNS e portas estão configurados.
A raiz `/` continua retornando 404: a API não tem página inicial.

Somente o Caddy publica portas no servidor. A API escuta na rede interna
do Docker. O IP fixo do Caddy é confiável para encaminhar o IP do cliente
e preservar o limite de autenticação por IP. A rede `172.30.50.0/24` deve
estar livre no servidor; se precisar trocá-la, altere a sub-rede, os dois
IPs e `Proxy__KnownIp` juntos em `compose.yaml`.

Os volumes `api_data`, `caddy_data` e `caddy_config` persistem entre
recriações dos contêineres. Não use `docker compose down -v`, pois isso
apaga os volumes, inclusive os usuários cadastrados.

## 4. Testar e acompanhar

```bash
# Cria um usuário de teste persistente; respeite o limite por minuto.
API_BASE_URL=https://api.seudominio.com python3 tests/smoke.py
docker compose logs -f --tail=100
```

`restart: unless-stopped` reinicia os processos se caírem e após reiniciar
o servidor, desde que o Docker esteja ativo. Uma resposta HTTP com erro
não reinicia automaticamente o contêiner; acompanhe `/health` com um
monitor externo.

## 5. Atualizar

Copie a nova versão do código preservando `.env`, e execute:

```bash
docker compose up -d --build
curl --fail https://api.seudominio.com/health
```

## 6. Backup do banco

Para um backup consistente, pare a API brevemente. Na raiz do projeto:

```bash
mkdir -p backups
chmod 700 backups
docker compose stop api
docker compose cp api:/data/security.db "backups/security-$(date +%Y%m%d-%H%M%S).db"
docker compose start api
chmod 600 backups/*.db
```

Mesmo se a cópia falhar, execute `docker compose start api`. Guarde cópias
fora do servidor, com acesso restrito, e teste a restauração. Para
restaurar, pare a API, copie o backup para `api:/data/security.db`, ajuste
o dono do arquivo para o usuário `app` do contêiner se necessário e inicie
a API. Não sobrescreva o banco com a API rodando.

Referências: [HTTPS do Caddy](https://caddyserver.com/docs/automatic-https)
e [imagens oficiais .NET 10](https://github.com/dotnet/dotnet-docker/discussions/6801).
