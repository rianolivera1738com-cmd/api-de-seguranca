# API de segurança — C# + Rust

API ASP.NET Core (.NET 10) com SQLite, JWT de 15 minutos e biblioteca Rust chamada por P/Invoke para gerar/verificar hashes Argon2id com salt aleatório.

## Executar (Linux)

Instale SDK .NET 10, Rust/Cargo e um linker C. Na raiz:

Se necessário, instale o SDK localmente (Linux, requer acesso à internet):

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$PWD/.dotnet"
export DOTNET_ROOT="$PWD/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

O script de execução reconhece automaticamente esse SDK local. Para iniciar:

```bash
export Jwt__Key="$(openssl rand -base64 48)"
bash scripts/run.sh
```

Servidor: `http://127.0.0.1:5080`. Mantenha a chave entre reinicializações para preservar tokens. Nunca a versione. `Database__Path` configura o banco (padrão `security.db` no diretório de execução do servidor); `ASPNETCORE_URLS` configura a escuta.

| Método | Rota | Resultado |
|---|---|---|
| GET | `/health` | Estado do processo |
| POST | `/auth/register` | Cadastro: 201, entrada inválida: 400, duplicidade: 409 |
| POST | `/auth/login` | JWT ou 401 |
| GET | `/me` | ID e e-mail; exige Bearer token |

Cadastro e login recebem JSON:

```json
{"email":"usuario@example.com","password":"Minha-senha-segura-123!"}
```

Exemplo:

```bash
curl http://127.0.0.1:5080/auth/register -H 'Content-Type: application/json' -d '{"email":"usuario@example.com","password":"Minha-senha-segura-123!"}'
curl http://127.0.0.1:5080/auth/login -H 'Content-Type: application/json' -d '{"email":"usuario@example.com","password":"Minha-senha-segura-123!"}'
curl http://127.0.0.1:5080/me -H 'Authorization: Bearer COLE_O_ACCESS_TOKEN'
```

Senhas: 12 a 128 bytes UTF-8, sem caractere nulo. E-mails são normalizados para minúsculas. Consultas parametrizadas, validação de assinatura/algoritmo/emissor/público/expiração JWT e hash fictício para login de usuário inexistente. Limites: 10 chamadas de autenticação por IP/minuto, 8 requisições simultâneas no processo e corpo de 4 KiB.

## Verificar

```bash
cargo test --locked --manifest-path native/security-core/Cargo.toml
dotnet build api/SecurityApi.csproj
# Com servidor rodando, preferencialmente usando banco de testes:
python3 tests/smoke.py
```

O teste HTTP cria um usuário persistente. Aguarde um minuto se o limite de autenticação já tiver sido consumido.

## Implantação e limites

Veja o [passo a passo de implantação com Docker e HTTPS](deploy/README.md).
Inclui compilação de C# e Rust, reinício automático, banco persistente e backup.

Use HTTPS no proxy de entrada, proteja a chave e o SQLite e faça backups. `Proxy__KnownIp` permite confiar em um único proxy para obter o IP do cliente; o Compose configura o IP interno do Caddy. Sem essa configuração, o limite usa o IP da conexão. Os limites são locais ao processo e precisam de coordenação externa para múltiplas réplicas.

Esta base não inclui MFA, recuperação de senha, confirmação de e-mail, papéis ou revogação de tokens antes da expiração. Cadastro com resposta 409 permite descobrir a existência de um e-mail.

Referências: [JWT no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0), [Argon2 Rust](https://docs.rs/argon2/0.5.3/argon2/).
