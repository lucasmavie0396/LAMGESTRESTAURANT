# Plano de melhorias — estudo do sistema (20/11/2026)

Estudo feito sobre o repositório em `8c4f8ad`. O objectivo é encontrar fragilidades depois de vários meses de crescimento faseado e ordenar as melhorias por risco.

## 1. Retrato do sistema

| Parte | Dimensão | Observação |
|---|---|---|
| Backend (Domain, Application, Infrastructure, Api) | ≈ 20 000 linhas, 24 ficheiros de controllers, 54 tabelas, 30 migrations | Camadas respeitadas; serviços usam `IAppDbContext` |
| Testes do backend | ≈ 8 000 linhas, 301 testes | Cobrem regras de negócio, concorrência e idempotência |
| Frontend (React) | ≈ 20 700 linhas, 18 módulos | 16 ficheiros de testes unitários (60 testes) e 31 testes E2E |
| Agente de impressão, desenho de documentos, licenças | ≈ 2 300 linhas | — |
| Instalador e configurador | ≈ 5 100 linhas, 52 testes | — |
| App Android (PDA) | ≈ 700 linhas | Sem testes automáticos |

### Pontos fortes confirmados
- **Regras permanentes cumpridas.**
  - Não há `float`/`double` em valores de dinheiro; o único caso é a medida das etiquetas.
  - Não há `DateTime.Now` no servidor.
  - Há 71 transacções explícitas.
  - As 14 entidades editáveis por várias pessoas ao mesmo tempo usam `rowversion`.
  - A auditoria é feita só por inserção.
- **Segurança de base sólida.**
  - Por omissão, todos os endpoints exigem sessão, e cada um tem a sua permissão: ou no controller, ou no serviço, no caso dos documentos de relatório.
  - O cookie é `HttpOnly` e `SameSite=Strict`.
  - A conta bloqueia 15 min após 5 tentativas, também no PIN.
  - O servidor envia os cabeçalhos `nosniff`, `X-Frame-Options` e `Referrer-Policy`.
- **Pacotes NuGet:** não há vulnerabilidades conhecidas.
- **Cópias de segurança:** são verificadas com `RESTORE VERIFYONLY`, ficam guardadas 14 dias e podem ser copiadas para outro disco.

## 2. Fragilidades encontradas

Prioridade: **A** = risco real para o cliente ou para os dados; **B** = risco operacional ou de manutenção; **C** = qualidade e evolução.
Esforço: **P** = pequeno (≤ 1 dia), **M** = médio (2–4 dias), **G** = grande (≥ 1 semana).

| # | Prioridade | Área | Fragilidade | Proposta | Esforço |
|---|---|---|---|---|---|
| 1 | A | Legal | Ainda está por confirmar se a AT exige certificação do programa de facturação, e quais os textos legais das isenções. | Reunião com o contabilista antes de mais vendas e registo da decisão. | — |
| 2 | A | Dependências | `npm audit`: `react-router` e `zod` com vulnerabilidade **alta**, `fflate` moderada. | Actualizar, correr todos os testes e gerar um Setup novo. | P |
| 3 | A | Verificação | Não há verificação automática no GitHub para o backend e o frontend, só para a app Android. Os testes dependem de alguém se lembrar de os correr. | GitHub Actions: compilação, testes do backend (SQL LocalDB), do configurador e do instalador, vitest, lint e build em cada push; E2E todas as noites. | M |
| 4 | A | Cópias de segurança | Se a cópia diária falhar, ninguém é avisado: só fica no log. | O servidor lê o log e a data da última cópia; aviso ao administrador no painel e no início de sessão se tiver mais de 36 h ou se falhou. | M |
| 5 | A | Capacidade | O SQL Server Express tem um limite de **10 GB por base**. As imagens dos produtos e a auditoria (que nunca se apaga) estão dentro da base, e não há aviso de espaço. | Medir o tamanho da base e o disco livre; aviso a partir de 70 % (7 GB) e de pouco disco; mostrar em Configurações → Sistema. | P |
| 6 | A | Frontend | Não há protecção contra erros de ecrã (*error boundary*): um erro num componente deixa o POS em branco, e os erros do navegador não chegam ao servidor. | Uma protecção por módulo, com o botão «Recarregar»; enviar o erro para o log do servidor. | P |
| 7 | B | Actualizações | As permissões novas não chegam aos perfis existentes (Gerente, Caixa): o administrador tem de as dar à mão. Há três casos destes em `TAREFAS.md`. | Na actualização, o semeador dá as permissões novas aos perfis padrão; um aviso nos Perfis lista as permissões novas por atribuir. | M |
| 8 | B | Rede | O sistema funciona em HTTP na LAN: palavras-passe e PIN passam sem cifra na rede local (na VPN Tailscale vão cifrados). | HTTPS na LAN com certificado próprio criado e instalado pelo Setup (servidor e postos); o cookie passa a só HTTPS. | G |
| 9 | B | Segurança | Não há limite de pedidos por IP no início de sessão: o bloqueio é por conta, por isso é possível experimentar muitas contas e PINs. | Limite de pedidos (`RateLimiter` do ASP.NET) em `/api/auth/*`. | P |
| 10 | B | Distribuição | O `LAMGest-Setup.exe` não está assinado (aviso do SmartScreen) e o APK não tem chave própria (cada versão obriga a desinstalar a anterior). | Certificado de assinatura de código e chave do APK em segredos do GitHub. | P (+ compra do certificado) |
| 11 | B | Instalação | Não há desinstalação, e várias partes nunca foram executadas numa máquina limpa: modo Servidor completo, troca real de base, SQL 2016/2019. | Desinstalação no Setup; uma máquina virtual de teste para seguir `TESTE_INSTALACAO.md` antes de cada versão. | M |
| 12 | B | Hardware | Muitas funções foram verificadas só em simulação: impressoras térmicas, gaveta, balanças, leitores de código de barras e de cartões, visor, PDA Sunmi, monitor táctil. | Uma visita de verificação com a lista única do anexo e o registo do resultado em `TAREFAS.md`. | M |
| 13 | B | Manutenção da base | O Express não tem Agent: os índices e as estatísticas nunca são mantidos. | Tarefa semanal (junto da de cópias) com `UPDATE STATISTICS` e reorganização de índices. | P |
| 14 | B | Desempenho | Os relatórios de períodos longos carregam as linhas e agrupam em memória; nunca foram testados com um ano de dados. | Gerar volume de teste (1 ano, «DEMO»), medir e passar as agregações pesadas para SQL. | M |
| 15 | C | Frontend | Todo o frontend vai num só ficheiro de 1,5 MB (a carga inicial em postos fracos e no PDA é lenta). | Dividir por módulos (`React.lazy` por rota). | P |
| 16 | C | Código | Há ficheiros muito grandes: `ServicoDocumentosRelatorio.cs` (1 062 linhas), `api.ts` (1 169), `PaginaPos.tsx` (795), `ServicoPedidos.cs` (732). | Dividir por assunto; gerar `api.ts` a partir do Swagger, para os tipos não se desencontrarem do servidor. | M |
| 17 | C | Testes | Há poucos testes de componentes React (o login, o POS e o pagamento não têm). Os E2E demoram 25 min e falham por tempo quando a máquina está ocupada. | Testes de componentes (Testing Library); E2E à espera do estado do ecrã em vez de tempo, divididos em grupos. | M |
| 18 | C | Idioma | Os textos novos em Português podem ficar sem tradução sem ninguém dar por isso. | Teste que extrai os textos das páginas e falha se faltar tradução em `en.json`. | P |
| 19 | C | Código | O instalador (`ServicoSql`) e o configurador (`BasesSql`) repetem as mesmas regras SQL. | Ficheiros partilhados entre os dois projectos. | P |
| 20 | C | Documentação | `TAREFAS.md` tem itens já feitos por marcar (ex.: `IAppDbContext`) e itens repetidos (E2E «venda à mesa»). | Rever e arrumar `TAREFAS.md`. | P |

## 3. Fases propostas

### Fase 1 — Proteger o cliente (≈ 1 semana) — **feita em 20/11/2026**, excepto a decisão legal (item 1)
Itens **2, 4, 5, 6, 9, 13** e a decisão legal (**1**).
Resultado: dependências seguras; o administrador é avisado de falhas nas cópias e de falta de espaço; o ecrã não fica em branco por um erro; o início de sessão fica protegido contra tentativas em massa; a base é mantida todas as semanas.

### Fase 2 — Verificação automática e actualizações sem surpresas (≈ 1 semana) — **feita em 20/11/2026** (falta confirmar a primeira execução no GitHub)
Itens **3, 7, 18, 20**.
Resultado: cada push é testado no GitHub; as actualizações dão as permissões novas aos perfis; não há textos sem tradução.

### Fase 3 — Instalação e distribuição profissionais (≈ 1–2 semanas) — **feita em 21/11/2026**, excepto a compra do certificado de assinatura, a chave do APK e o teste numa máquina virtual
Itens **10, 11, 8**.
Resultado: Setup e APK assinados; desinstalação; teste numa máquina limpa antes de cada versão; HTTPS na LAN.

### Fase 4 — Desempenho e manutenção do código (≈ 2 semanas) — **feita em 21/11/2026**
Itens **14, 15, 16, 17, 19**.
Resultado: relatórios rápidos com um ano de dados; carga inicial menor; código mais fácil de alterar; testes de ecrã fiáveis.

### Fase 5 — Verificação no restaurante (em paralelo, quando houver acesso)
Item **12**: lista do anexo.

### Funcionalidades em falta (backlog, depois das fases 1–2)
Estorno parcial; separar a conta por produto; resgate de pontos; facturas fora do POS com baixa de stock; nota de débito e guia de remessa; exportação `.xlsx`; reserva que ocupa a mesa; app do PDA em inglês; versão iPhone.

## Medição dos relatórios com um ano de dados (Fase 4)
Teste `VolumeRelatoriosTests` (só a pedido: `$env:LAMGEST_TESTES_VOLUME = "1"; dotnet test --filter VolumeRelatorios`): 54 750 vendas DEMO, 191 563 linhas e 220 334 movimentos de stock numa base de testes, na máquina de desenvolvimento. Tempo de cada pedido à API (segunda vez, com a cache do SQL Server):

| Relatório (1 ano) | Antes | Depois |
|---|---|---|
| Vendas (ecrã) — também a base do Financeiro e dos documentos de vendas e produtos | 2,7 s (8,2 s na 1.ª vez) | 2,0 s (6,3 s na 1.ª vez, com o arranque do serviço) |
| Financeiro; documentos de vendas e de produtos | 2,6–2,8 s | 2,0–2,1 s |
| Lista de vendas | recusa mais de 20 000 linhas (pede um período menor); 1 mês: 0,1 s | igual |
| IVA, stock, painel, anulações, clientes, acompanhamentos | ≤ 0,8 s | igual ou menos |

Alterações: o relatório de vendas agrupa as linhas por produto no SQL (antes trazia todas as linhas para a memória, com duas subconsultas por linha) e dois índices de cobertura para os relatórios (pedidos pagos por data e linhas por pedido), que reduzem estas consultas a cerca de um terço.

## Anexo — verificação com equipamento real
- Impressora térmica: corte, gaveta, margens, papel de 58 mm e garantir que não sai A4 no rolo.
- Impressora de etiquetas (30 × 20 mm), balança etiquetadora e balança ligada ao posto.
- Leitor de código de barras, leitor de cartões magnéticos, visor de poste (ESC/POS e CD5220) e segundo monitor.
- PDA Sunmi: emparelhamento, venda, impressão, sem internet, Tailscale com dados móveis.
- Monitor táctil de 15″; PDF dentro do visualizador do Chrome do posto.
- Troca real de base com o LAMGest-Configurar e o modo Servidor completo do Setup.
