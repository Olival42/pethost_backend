# Roteiro exploratório — execução contra a API no ar

Gerado por `tools/PetHost.ExploratoryQa` (Entrega 2, seção 6.1: casos de borda, segurança, tempo real, carga leve e logs).

- **API:** http://localhost:5080/
- **Início:** 2026-10-25 14:57:19 (Brasília)
- **Duração:** 9 s
- **Logs conferidos:** arquivo `qa-api.log`
- **Resultado:** 60 passaram, 0 falharam, 0 não rodaram

## Preparação

| ID | Cenário | Esperado | Obtido | Resultado |
|---|---|---|---|---|
| P-01 | API no ar: Postgres e Redis respondem (`GET /health/ready`) | 200 | 200 (sem envelope), 84 ms | ✅ |
| P-02 | Login das 4 contas dos dados de teste (2 tutores, 2 anfitriões) | 200 em todas | 4 sessões abertas | ✅ |
| P-03 | Login do admin do seed | 200 | 200, 207 ms | ✅ |

## Casos de borda

| ID | Cenário | Esperado | Obtido | Resultado |
|---|---|---|---|---|
| B-01 | JSON quebrado no corpo | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 120 ms | ✅ |
| B-02 | Corpo sem o campo obrigatório (`{}`) | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 35 ms | ✅ |
| B-03 | Campo com tipo errado (texto no lugar de booleano) | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 67 ms | ✅ |
| B-04 | Content-Type que a API não aceita (`text/plain`) | 415 no envelope | 415 UNSUPPORTED_MEDIA_TYPE, 8 ms | ✅ |
| B-05 | Rota que não existe | 404 no envelope | 404 NOT_FOUND, 2 ms | ✅ |
| B-06 | Id fora do formato na rota (`/pets/nao-e-um-guid`) | 404 no envelope | 404 NOT_FOUND, 2 ms | ✅ |
| B-07 | Método que a rota não tem (`DELETE /listings`) | 405 no envelope | 405 METHOD_NOT_ALLOWED, 2 ms | ✅ |
| B-08 | `pageSize` acima do máximo (1000; máximo 100) | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 95 ms | ✅ |
| B-09 | `page=0` (começa em 1) | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 13 ms | ✅ |
| B-10 | Saída antes da chegada na busca por datas | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 44 ms | ✅ |
| B-11 | Datas no passado na busca | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 4 ms | ✅ |
| B-12 | SQL injection no filtro de cidade | 200 com zero resultados (o texto é só um valor) | 200, 240 ms, 0 resultados | ✅ |
| B-13 | Os cantinhos continuam lá depois da tentativa de injection | 200 com os cantinhos de Maringá | 200, 217 ms, 3 cantinhos | ✅ |
| B-14 | Emoji e HTML no filtro de bairro | 200 no envelope | 200, 20 ms | ✅ |
| B-15 | Corpo de 31 MB (acima do limite de 30 MB) | 400 ou 413 no envelope, sem 500 | 413 PAYLOAD_TOO_LARGE, 28 ms | ✅ |
| B-16 | Cadastro de tutor com vários campos inválidos (menor de idade, CPF 111.111.111-11, senha fraca...) | 400 VALIDATION_ERROR com todos os campos de uma vez | 400 VALIDATION_ERROR, 101 ms, 11 campos: cpf, email, password, phone, birthDate, address.zipCode, address.street, address.number, address.neighborhood, address.city, address.state | ✅ |

## Segurança

| ID | Cenário | Esperado | Obtido | Resultado |
|---|---|---|---|---|
| S-01 | Rota logada sem token | 401 no envelope | 401 UNAUTHORIZED, 6 ms | ✅ |
| S-02 | Token com o papel trocado para admin (assinatura não confere) | 401 no envelope | 401 UNAUTHORIZED, 8 ms | ✅ |
| S-03 | Token sem assinatura (`alg: none`) | 401 no envelope | 401 UNAUTHORIZED, 2 ms | ✅ |
| S-04 | Token que não é JWT | 401 no envelope | 401 UNAUTHORIZED, 1 ms | ✅ |
| S-05 | Tutor numa rota de admin (`GET /owners`) | 403 no envelope | 403 FORBIDDEN, 3 ms | ✅ |
| S-06 | Anfitrião numa rota de tutor (`POST /conversations`) | 403 no envelope | 403 FORBIDDEN, 9 ms | ✅ |
| S-07 | Tutor lê o pet de outro tutor | 404 PET_NOT_FOUND no envelope | 404 PET_NOT_FOUND, 132 ms | ✅ |
| S-08 | Tutor altera o pet de outro tutor | 404 PET_NOT_FOUND no envelope | 404 PET_NOT_FOUND, 61 ms | ✅ |
| S-09 | O pet continua com o nome de antes | "Mel" | "Mel" | ✅ |
| S-10 | Login: senha errada e e-mail inexistente respondem igual | mesmo status, código e mensagem (401 AUTH_INVALID_CREDENTIALS) | senha errada: 401 AUTH_INVALID_CREDENTIALS, 208 ms; e-mail inexistente: 401 AUTH_INVALID_CREDENTIALS, 204 ms | ✅ |
| S-11 | Login com a senha certa e o papel de outra conta (tutora como anfitriã) | 401 AUTH_INVALID_CREDENTIALS no envelope | 401 AUTH_INVALID_CREDENTIALS, 213 ms | ✅ |
| S-12 | SQL injection no e-mail e na senha do login | 401 AUTH_INVALID_CREDENTIALS no envelope | 401 AUTH_INVALID_CREDENTIALS, 2 ms | ✅ |
| S-13 | Esqueci a senha: conta existente e inexistente respondem igual | 200 nas duas, mesmo `data` | existente: 200, 99 ms; inexistente: 200, 8 ms | ✅ |
| S-14 | Refresh: troca o refresh token por um par novo | 200 no envelope | 200, 48 ms | ✅ |
| S-15 | Refresh com o token antigo, já trocado | 401 AUTH_REFRESH_TOKEN_INVALID no envelope | 401 AUTH_REFRESH_TOKEN_INVALID, 3 ms | ✅ |
| S-16 | Logout invalida o refresh token | logout 200; refresh depois 401 AUTH_REFRESH_TOKEN_INVALID | logout: 200, 18 ms; refresh: 401 AUTH_REFRESH_TOKEN_INVALID, 2 ms | ✅ |
| S-17 | CORS: preflight vindo do front (http://localhost:3000) | `Access-Control-Allow-Origin: http://localhost:3000` | 204, Allow-Origin: http://localhost:3000 | ✅ |
| S-18 | CORS: preflight vindo de outra origem | sem `Access-Control-Allow-Origin` | 204, Allow-Origin: ausente | ✅ |
| S-19 | Força bruta no login: tentativas seguidas com senha errada | 429 TOO_MANY_REQUESTS no envelope, com `Retry-After`, até a 11ª tentativa da janela | 429 TOO_MANY_REQUESTS, 7 ms na tentativa 2 da rajada (os logins anteriores do roteiro contam na mesma janela), Retry-After: 300 s | ✅ |
| S-20 | Nenhuma resposta da execução expõe stack trace, SQL ou nome de exceção | 0 de 258 respostas | 0 respostas | ✅ |
| S-21 | Nenhum 500 e toda resposta da API no envelope `ApiResponse` | 0 respostas 5xx; 0 fora do envelope | 0 respostas 5xx; 0 fora do envelope | ✅ |

## Chat e tempo real

| ID | Cenário | Esperado | Obtido | Resultado |
|---|---|---|---|---|
| R-01 | Tutora puxa conversa pelo cantinho da anfitriã | 201 na primeira vez, 200 com a mesma conversa depois | 200, 181 ms | ✅ |
| R-02 | Tutora envia mensagem de texto | 201 no envelope | 201, 305 ms | ✅ |
| R-03 | A anfitriã conectada no hub (WebSocket) recebe `messageCreated` | evento em até 10 s | recebido em 313 ms após o envio | ✅ |
| R-04 | Acentos, travessão e emoji chegam iguais pelo hub | texto idêntico ao enviado | idêntico | ✅ |
| R-05 | A anfitriã recebe `unreadCountChanged` com o contador novo | evento com `total` ≥ 1 | total = 3 | ✅ |
| R-06 | A mensagem fica no histórico da conversa | presente em `GET .../messages` | 200, 40 ms, presente | ✅ |
| R-07 | Mensagem só com espaços | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 3 ms | ✅ |
| R-08 | Mensagem com 2.001 caracteres (máximo 2.000) | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 3 ms | ✅ |
| R-09 | Mensagem com caractere nulo (`\u0000`) | 400 VALIDATION_ERROR no envelope | 400 VALIDATION_ERROR, 4 ms | ✅ |
| R-10 | Outro tutor lê as mensagens da conversa | 404 CONVERSATION_NOT_FOUND no envelope | 404 CONVERSATION_NOT_FOUND, 8 ms | ✅ |
| R-11 | Outro tutor envia mensagem na conversa | 404 CONVERSATION_NOT_FOUND no envelope | 404 CONVERSATION_NOT_FOUND, 9 ms | ✅ |
| R-12 | Hub sem token | 401 | 401 UNAUTHORIZED, 1 ms | ✅ |
| R-13 | Hub com token adulterado | 401 | 401 UNAUTHORIZED, 1 ms | ✅ |
| R-14 | Hub com token de admin (o chat é só de tutor e anfitrião) | 403 | 403 FORBIDDEN, 2 ms | ✅ |

## Carga leve

| ID | Cenário | Esperado | Obtido | Resultado |
|---|---|---|---|---|
| C-01 | 100 buscas de cantinhos, 10 ao mesmo tempo, com login | todas 200; p95 abaixo de 1 s | 100/100 com 200; p50 127 ms, p95 244 ms, máx 291 ms | ✅ |
| C-02 | Rajada sem login até passar do limite geral (120 por minuto por IP) | 429 TOO_MANY_REQUESTS no envelope, com `Retry-After` | 429 TOO_MANY_REQUESTS, 5 ms depois de 100 requisições da rajada, Retry-After: 60 s | ✅ |

## Logs

| ID | Cenário | Esperado | Obtido | Resultado |
|---|---|---|---|---|
| L-01 | Cada requisição vira uma linha com método, rota, status e tempo | linha do `POST /api/v1/auth/sessions/login` com status 401 | 17189 eventos lidos; linha encontrada | ✅ |
| L-02 | Rate limit estourado é registrado como Warning | pelo menos 1 `Rate limit exceeded on {Path}` | 6 eventos | ✅ |
| L-03 | Nenhum erro (Error/Fatal) no log durante o roteiro | 0 eventos | 0 eventos | ✅ |
| L-04 | Nenhum dado sensível no log (senhas, tokens, CPF, texto do chat) | nenhum dos 18 valores usados no roteiro | nenhum encontrado | ✅ |

## Observações

- Carga leve (C-01): 100 requisições em 1.4 s (69.2 req/s), mín 59 ms, p50 127 ms, p95 244 ms, máx 291 ms.
- Logs conferidos: 17189 linhas, 17189 eventos JSON, 13836 Debug, 3345 Information, 8 Warning.

