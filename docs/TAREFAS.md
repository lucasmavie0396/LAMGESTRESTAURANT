# Lista de tarefas por módulo

Legenda: `[x]` concluído e verificado · `[~]` em curso · `[ ]` por fazer

## Fase 1 — Inspecção e planeamento
- [x] Inspecção do ambiente (.NET 9.0.315, Node 22.23, Git 2.56, SQL Server 2022 Express `.\SYSON`)
- [x] Arquitectura e modelo de dados inicial apresentados
- [x] Pasta do projecto e servidor SQL decididos

## Fase 2 — Fundação
- [x] Solução backend (Domain, Application, Infrastructure, Api, Tests) — compila sem avisos
- [x] Frontend Vite + React + TS com dependências instaladas
- [x] CLAUDE.md, README.md, TAREFAS.md, .gitignore, .env.example
- [x] Entidades de domínio (47 tabelas) e `AppDbContext` com convenções (decimais, UTC, enums texto, rowversion, FKs sem cascata)
- [x] Connection string via user-secrets; erro claro se faltar
- [x] Identity (cookie HttpOnly SameSite=Strict, bloqueio após 5 falhas), 6 perfis, 41 permissões por claims
- [x] Endpoints `api/auth` (login, logout, me, alterar-palavra-passe) e `api/saude`
- [x] Comando `bootstrap-admin` (implementado; ainda não executado — cabe ao administrador)
- [x] Serilog (consola + ficheiro diário), tratador global de excepções com ProblemDetails em Português
- [x] Migration `Inicial` aplicada a `LAMGestRestaurantDB` (.\SYSON)
- [x] Auditoria por inserção na mesma transacção; registos imutáveis protegidos no SaveChanges
- [x] Semeador idempotente de dados de referência (perfis, métodos de pagamento, unidades, armazém, taxas, configurações)
- [x] Testes: 21 xUnit (base `LAMGestRestaurantDB_Tests`) e 3 Vitest a passar
- [x] Proxy Vite → API em desenvolvimento
- [x] Tailwind com tema azul, layout base (menu lateral filtrado por permissões, barra superior com utilizador, perfil, estado da ligação, data/hora de Maputo)
- [x] Indicador de ligação (`/api/saude` a cada 10 s; 3 s após falha) e faixa de bloqueio de gravações sem servidor/BD
- [x] Ecrã de login, alteração de palavra-passe e alteração obrigatória no primeiro acesso (imposta também pela API)
- [x] Gestão de utilizadores (API + ecrã): pesquisa, filtro, ordenação, paginação, criar, editar perfis, activar/desactivar com confirmação, repor palavra-passe, desbloquear
- [x] Gestão de perfis (API + ecrã): permissões por módulo; Administrador fixo com todas; perfis de sistema não renomeáveis nem elimináveis
- [x] Protecção: tem de existir sempre outro administrador activo; não é possível desactivar a própria conta
- [x] `bootstrap-admin` testado (cria na base vazia; recusa se já houver administrador; tolera BOM na entrada redireccionada)
- [x] Testes: 30 xUnit + 3 Vitest; verificação manual no browser (login, validações, criação de utilizador, alteração obrigatória, sem permissão, queda e recuperação da API)
- [ ] Testes de componentes React (Vitest + Testing Library) para login e formulários
- [ ] Interface `IAppDbContext` em Application para os serviços de caso de uso

## Fase 3 — Fluxo principal
- [x] `IAppDbContext` em Application; serviços de caso de uso em Application (catálogo, sala, caixa)
- [x] Caixa: terminais; abrir (1 sessão por terminal e por utilizador, protegido por índice único — testada corrida de 4 aberturas simultâneas); entradas/saídas com motivo (saída não excede o esperado na gaveta); fecho com contagem por método, diferenças gravadas e relatório imprimível; histórico (cada caixa vê só as suas; `caixa.ver-todas` vê todas)
- [x] Produtos e categorias: CRUD, subcategorias sem ciclos, estações de preparação, código sugerido, código de barras único, esgotado/disponível (também pela cozinha), desactivação em vez de eliminação, alteração de preço com permissão própria e auditoria, concorrência optimista (409)
- [x] Mesas e zonas: CRUD, mapa por zona com cores e contagem por estado, Livre↔Indisponível manual (Ocupada/Reservada só por pedidos/reservas)
- [x] Validação decimal independente da cultura do servidor (corrigido bug real com cultura pt)
- [x] Testes: 47 xUnit; verificação manual no browser de categorias, produtos, mesas e ciclo completo de caixa
- [x] Pedidos Mesa/Take Away/Delivery: criação idempotente (Idempotency-Key), numeração PED-AAAA-NNNNNN sem duplicados, uma mesa = um pedido aberto (índice único), totais e impostos incluídos calculados no servidor (`CalculoPedido`, imposto por linha), alterações com RowVersion (clique repetido → 409, sem duplicar itens)
- [x] Itens: adicionar, alterar/retirar antes do envio, observações, cancelar depois do envio (permissão + motivo), desconto em valor ou % (permissão + auditoria), transferência de mesa registada, cancelamento do pedido (motivo; exige anular pagamentos antes)
- [x] Cozinha (KDS): envio em tempo real via SignalR (`/hubs/operacoes`), estados Pendente → Em preparação → Pronto → Entregue com utilizador/hora no histórico, filtro por estação e estado, alerta visual por tempo de espera, protecção contra dois postos alterarem o mesmo item (409), aviso «Pronto» no POS
- [x] Pagamentos: idempotentes (testado com 5 reenvios simultâneos), parciais/divisão por valor, troco só em Dinheiro, referência obrigatória em M-Pesa/e-Mola/Transferência/Cheque, sessão de caixa aberta do operador, dois terminais a pagar o mesmo pedido em simultâneo nunca excedem o total
- [x] Fecho automático quando o saldo chega a zero: mesa libertada, recibo interno REC-AAAA-NNNNNN, baixa de stock única pela ficha técnica (conversão de unidades, stock negativo bloqueado salvo autorização), tudo na mesma transacção
- [x] Recibo imprimível em formato de talão 80 mm (janela de impressão do Windows)
- [x] Auditoria: criar, cancelar item, desconto, transferir, cancelar pedido, pagamento, anulação, fecho
- [x] Testes: 79 xUnit (incl. corridas de concorrência e stock); verificação manual no browser do ciclo mesa → cozinha em tempo real → pagamento parcial → M-Pesa → recibo
- [x] Estorno total de pedidos pagos: reembolso pela sessão de caixa aberta do operador (movimento «Reembolso», limitado ao dinheiro na gaveta), recibo anulado, stock reposto com movimentos de estorno, pedido «Estornado» sai do KDS, auditoria; sessões fechadas nunca são alteradas
- [x] Ecrã Delivery/Take Away em colunas por estado (Recebido → Em preparação → Pronto → Saiu → Entregue), contacto e endereço, estado do pagamento, tempo real
- [x] Relatório de vendas (hoje, ontem, 7 dias, mês, mês anterior, personalizado): totais, ticket médio, impostos, margem estimada com metodologia, por dia (gráfico), método, tipo, categoria, produto (mais → menos vendido), utilizador, garçom, cancelamentos e estornos; CSV para Excel e impressão/PDF
- [x] Dashboard com dados reais: vendas de hoje vs ontem, pedidos, ticket médio, pedidos em aberto, cozinha, 7 dias, métodos, top produtos, caixas abertos (conforme permissão), stock baixo (com `stock.ver`), alertas de cancelamentos/estornos; actualiza em tempo real
- [x] Testes: 86 xUnit + 4 Vitest; verificação no browser com vendas reais (dashboard, relatório, delivery, estorno)
- [ ] Estorno parcial (hoje: estornar o pedido completo e registar um novo)
- [ ] Separar conta por produto (por valor já funciona com pagamentos parciais)
- [ ] Impressão directa ESC/POS em impressoras de rede (talões de cozinha e recibos)
- [ ] Relatórios financeiros (fechos de caixa e diferenças) e de stock/compras — depois dos módulos de stock, compras e despesas

## Fase 4 — Restantes módulos
- [x] API de stock: artigos (CRUD, mínimo, stock negativo só com `stock.ajustar`), movimentos, ajustes/perdas com motivo, transferências entre armazéns, inventários (contagem; no fecho o saldo passa a ser o contado, mesmo com movimentos posteriores à abertura)
- [x] API de fichas técnicas versionadas (nova versão encerra a anterior; validação de unidades; custo estimado pelo custo médio)
- [x] API de fornecedores e compras: rascunho editável/eliminável, totais no servidor (custos sem IVA), confirmação única com custo médio ponderado e último custo, anulação com estorno de stock (bloqueada se já consumido), resumo por fornecedor
- [x] Testes: 93 xUnit (custo médio, anulação, versões de receita, inventário, transferências, permissões)
- [x] Ecrã de Stock: artigos (pesquisa, filtros, alerta abaixo do mínimo, saldo por armazém, valor), ajuste/perda, transferência, histórico de movimentos, criação de armazéns, inventários (folha de contagem, guardar, confirmar com aviso, cancelar), fichas técnicas (lista com custo/preço, editor com unidades compatíveis, histórico de versões, retirar)
- [x] Ecrã de Compras: lista com pesquisa e estados, formulário de linhas com pré-visualização dos totais, rascunho → confirmar (entrada no stock) → anular com motivo, eliminar rascunho; fornecedores (NUIT validado, activar/desactivar, total comprado)
- [x] Verificação no browser: artigo → fornecedor → compra 10 kg (2 204 MT, custo médio 190/kg) → ficha técnica 300 g (custo 57 MT) → inventário (−0,5 kg) com movimentos correctos
- [x] Clientes e fidelização: ficha (telefone normalizado e único, NUIT, cartão), activar/desactivar, histórico de pedidos pagos; no POS procura/cria cliente e associa-o ao pedido aberto; ao fechar o pedido acumulam pontos (1 ponto por cada N MT, configurável) e no estorno são retirados — movimentos de pontos só por inserção
- [x] Reservas: agenda por dia, duração, mesa opcional com lugares suficientes, conflito de horário na mesma mesa (409), estados Pendente → Confirmada → Chegou / Não compareceu / Cancelada com motivo
- [x] Despesas: categorias (semeadas), registo com método e referência, opção «paga com dinheiro da caixa» (cria movimento «Despesa» na sessão aberta deste terminal), anulação com motivo, totais por categoria
- [x] Funcionários: função, contactos, documento (dado pessoal), ligação a um utilizador — os pedidos abertos por esse utilizador no POS ficam atribuídos ao garçom
- [x] Configurações: dados do restaurante, regional, taxa de entrega, fidelização, taxas de imposto, métodos de pagamento e terminais; valores numéricos aceitam vírgula decimal e são guardados em formato invariante
- [x] Testes: 99 xUnit + 6 Vitest; verificação no browser: cliente associado no POS → pagamento 450,50 MT → 4 pontos e 1 pedido; reserva + conflito na mesma mesa; despesa de 1 250,75 MT pela caixa (esperado 199,75 MT); funcionário ligado a utilizador
- [ ] Resgate de pontos de fidelização (hoje só acumulam e são retirados no estorno)
- [ ] Reservas não alteram automaticamente o estado da mesa (o operador ocupa a mesa ao abrir o pedido)
- [ ] Anular uma despesa paga por uma caixa já fechada não altera esse fecho (fica registada a anulação; o acerto é manual)
- [x] Relatório financeiro (`relatorios.financeiros`): resultado estimado (vendas sem impostos − custo estimado − despesas), recebimentos por método, despesas por categoria e forma de pagamento, fechos de caixa do período com esperado/contado/diferença e totais por método; estornos e compras mostrados à parte
- [x] Relatório de stock e compras (`relatorios.stock`): valor actual do stock (por tipo e armazém), movimentos por tipo, consumo por artigo, ajustes/perdas/inventário, entradas por compra e compras por fornecedor
- [x] Ecrã de Relatórios com separadores conforme as permissões (Vendas / Financeiro e caixa / Stock e compras), período partilhado, CSV para Excel em cada secção e impressão/PDF
- [x] Testes: 102 xUnit; verificação no browser dos três separadores com os dados da base de testes (resultado 4 763,76 − 1 415 − 500 = 2 848,76; fechos com diferença −60)
- [ ] Exportação nativa .xlsx (hoje CSV, que o Excel abre directamente)
- [ ] Dividir o bundle do frontend por rotas (aviso do Vite > 500 kB; não afecta o funcionamento em LAN)

## Fase 5 — Testes e LAN
- [x] Testes de concorrência e reenvio de pagamentos (feitos nas fases 3–4: aberturas de caixa simultâneas, 5 reenvios do mesmo pagamento, dois terminais a pagar o mesmo pedido, RowVersion → 409)
- [x] Testes de permissões em cada módulo (403 por perfil) — 104 xUnit no total
- [x] Playwright E2E (`npm run e2e`): base própria `LAMGestRestaurantDB_E2E`, administrador com palavra-passe aleatória apagada no fim; login, venda à mesa com cozinha em tempo real noutro separador até ao recibo e relatório, bloqueio de gravações sem servidor — 3/3 a passar com o Edge
- [x] Serviço Windows (`UseWindowsService`, directório da aplicação como raiz), cabeçalhos de segurança (nosniff, DENY, no-referrer)
- [x] Chaves de Data Protection persistidas em `DataProtection:Pasta` e cifradas com DPAPI da máquina — testado: a sessão sobrevive a uma nova instância da aplicação
- [x] Opção `Seguranca:CookieSoHttps` e guia de HTTPS com certificado interno (Kestrel por variáveis do serviço)
- [x] `scripts/publicar.ps1` (frontend em wwwroot, `migracoes.sql` idempotente, scripts, docs) — testado: pacote publicado arrancou em Production, serviu `/pos`, `/api/saude` 200, API 401 sem sessão, Swagger desligado
- [x] `scripts/instalar-servico.ps1`: conta virtual `NT SERVICE\LAMGestRestaurant` só com leitura/escrita na base, pastas em ProgramData com ACL, variáveis do serviço, firewall Domínio/Privada, reinício automático — **não executado aqui** (altera o sistema; corre-se no servidor de produção)
- [x] Backups: `backup-sql.ps1` (CHECKSUM + VERIFYONLY + segunda cópia + retenção só dos seus ficheiros, recusa bases que não sejam LAMGestRestaurant*) e `testar-restauro.ps1` (restaura para `_Restauro`, contagens, elimina) — ambos testados com a base de testes; `registar-backup.ps1` (Agendador de Tarefas) não executado aqui
- [x] Documentação: `docs/INSTALACAO_LAN.md`, `docs/BACKUP.md`, `docs/PRODUCAO.md` (lista de verificação)
- [ ] No servidor de produção: correr `instalar-servico.ps1`, `bootstrap-admin` e `registar-backup.ps1`, e seguir `docs/PRODUCAO.md`

## Impressoras e modos de pagamento
- [x] Configurações com separadores: Geral, Impressoras, Modos de pagamento
- [x] Impressoras: função Caixa/Bar/Cozinha, impressora do Windows do servidor (lista das instaladas, validada), papel 80 mm/58 mm/A4, terminal (caixa) ou estação (bar/cozinha), activar/desactivar, página de teste e estado da última impressão; migration `ImpressorasConfiguraveis` só renomeia/acrescenta colunas
- [x] Impressão automática pelo servidor em segundo plano (fila + spooler do Windows, GDI): talões por destino ao enviar (estação → Bar → Cozinha), recibo na impressora do terminal ao fechar o pedido (configurável), reimpressão pelo POS com recurso ao navegador quando não há impressora de caixa
- [x] Modos de pagamento: criar e editar (código gerado e fixo, ordem, troco só com gaveta, referência), desactivar; nunca eliminar
- [x] Testes: 113 xUnit (6 de impressão com impressoras simuladas + desenho real para PDF pelo spooler do Windows, revisto visualmente: recibo 80 mm, talão 58 mm, teste A4); verificação no browser (criar/editar impressora com impressoras reais do Windows, criar modo de pagamento)
- [ ] Testar com as impressoras térmicas reais do restaurante (drivers de talão; corte de papel)
- [ ] Abrir a gaveta de dinheiro (comando ESC/POS) — depende da impressora/driver

## Categorias, subcategorias, impressoras por categoria, stock inicial e imagens
- [x] Subcategorias geridas no ecrã de categorias («+ Subcategoria» em cada categoria; herdam cor e estação; lista em árvore com o número de subcategorias)
- [x] Impressora dos pedidos na categoria (Bar/Cozinha); subcategorias herdam da principal; o produto pode escolher outra. Ordem: produto → categoria → categoria principal → regras automáticas; com impressora escolhida, imprime mesmo o que não vai ao ecrã da cozinha (ex.: cervejas ao balcão)
- [x] POS: categorias principais nos separadores e subcategorias por baixo; imagem nos botões dos produtos; a barra de categorias desliza em vez de alargar a página
- [x] Stock inicial na criação do produto (permissão `stock.ajustar`): artigo de revenda em unidades com o mesmo código, ficha técnica 1:1, entrada «Stock inicial» com custo (passa a custo médio e custo de referência), stock mínimo; tudo na mesma transacção
- [x] Imagens: pesquisa no Openverse (licença livre, sem chave, miniaturas via servidor), importação de endereço (ex.: Google Imagens) e carregamento de ficheiro; reduzidas a 640 px JPEG e guardadas na base (`ImagensProduto`, entram nos backups); autor/licença guardados; o servidor só descarrega de endereços públicos (bloqueia rede local, loopback, link-local, outros protocolos) e no máximo 8 MB
- [x] Migration `ImpressoraCategoriaProdutoEImagens` só aditiva
- [x] Testes: 136 xUnit (herança da impressora e escolha no produto, stock inicial e baixa na venda, tudo-ou-nada, permissões, imagem reduzida e protegida por sessão, 7 endereços internos recusados, 12 casos de IP público/privado); E2E 4/4; verificação no browser com pesquisa real no Openverse e importação (Flickr → 640×400, 91 KB)
- [ ] Limpeza de imagens carregadas e nunca usadas por um produto

## Documentos impressos, logótipo e talões de produção
- [x] Relatórios em documento imprimível (talão 80 mm, 58 mm ou A4): pagamentos (por hora, referência, troco, totais por método, anulados, reembolsos, líquido), resumo de vendas, produtos vendidos por categoria, despesas, financeiro, existências em stock e fecho/ponto de situação de caixa (por método, movimentos, assinaturas); permissões por documento
- [x] Diálogo «Imprimir» em Relatórios e no Caixa: este computador (janela do Windows, escolhe-se o papel; também PDF) ou impressora do servidor (papel da impressora, sem diálogo)
- [x] Logótipo do estabelecimento (Configurações → Geral): sai nos documentos A4 e no recibo de 80 mm (servidor e navegador), nunca em 58 mm nem nos talões; o recibo pelo navegador passou a usar o mesmo documento do servidor
- [x] Talões de anulação («*** ANULAÇÃO ***», «NÃO FAZER», motivo) ao cancelar item ou pedido já enviado, no mesmo centro de produção; botão «Reimprimir talões» no POS («(REIMPRESSÃO)»)
- [x] «Outro centro de produção» (pastelaria, grelhados…): só recebe o que categorias, produtos ou estação lhe atribuem
- [x] Testes: 140 xUnit (anulação/reimpressão, centro de produção, relatório de pagamentos e fecho com permissões, logótipo por papel), 10 Vitest (HTML por papel, escape, regra do logótipo); desenho real em PDF revisto (recibo 80 mm com logótipo, anulação 58 mm); browser: logótipo carregado, relatório de pagamentos A4 com logótipo, fecho de caixa 80 mm, reimpressão no POS

## Licenças
- [x] Formato `LAMGEST1.<dados>.<assinatura>` (ECDSA P-256/SHA-256) na biblioteca `LAMGestRestaurant.Licenciamento`, partilhada pelo sistema (verifica com a chave pública embutida) e pelo gerador (assina)
- [x] Tabela `Licencas` (histórico; migration `Licencas` só aditiva); activação em Configurações → Licença (texto ou .lic, `configuracoes.gerir`), nome e NUIT do estabelecimento passam a ser os da licença e o NUIT fica preso a ela
- [x] Estados: válida, a expirar (≤ 30 dias), tolerância (≤ 7 dias depois), expirada/inexistente/inválida → gravações bloqueadas (middleware: só GET, /api/auth e /api/licenca); verificação no arranque (registo) e de 5 em 5 minutos; avisos na barra e no ecrã de entrada
- [x] Protecções: texto guardado volta a ser verificado (alterar a validade na base invalida), NUIT da configuração tem de ser o da licença, relógio atrasado (> 36 h em relação à última utilização) invalida; licença mais curta do mesmo NUIT, expirada ou já substituída é recusada
- [x] Gerador `LAMGest-Licencas.exe` (WinForms, ficheiro único): estabelecimentos por NUIT, gerar, renovar +1 ano, copiar/guardar .lic, histórico; chave privada protegida por DPAPI no perfil, cópia de segurança PEM cifrada (AES-256, PBKDF2 600 000) com palavra-passe; `criar-gerador-licencas.ps1` confirma que a chave do computador é a embutida no sistema
- [x] Testes: 10 xUnit (formato, estados, bloqueio e desbloqueio, activação e recusas, NUIT preso, adulteração e relógio), 6 do gerador (emitir, renovar, validações, cópia de segurança), E2E (activar a renovação no ecrã); captura do gerador revista
- [ ] Pedir a licença também no instalador (modo Servidor), para o sistema já arrancar licenciado
- Limite conhecido: quem administra o servidor do cliente pode, com esforço técnico, alterar os ficheiros do programa; a licença é uma protecção comercial, não absoluta

## Módulo de relatórios (análise do prompt de relatórios)
- [x] Lista de vendas venda a venda (`GET /api/relatorios/lista-vendas`): estado Pago/Estornado/Cancelado (data do pagamento ou do estorno/cancelamento), tipo, operador, forma de pagamento, janela de horas locais (também a passar a meia-noite), pesquisa; recebido, troco, métodos, recibo, motivo; totais de tudo o filtrado; paginação e exportação de tudo (até 20 000)
- [x] Vendas por cliente (`/clientes`), IVA e descontos por taxa e por dia/mês (`/impostos`, arredondamentos acertados ao imposto de cada pedido), anulações e devoluções com motivo e quem (`/anulacoes`)
- [x] Resumo de vendas: código e preço médio do produto, ordenação por facturação/quantidade, produtos sem vendas, operadores com descontos e formas de pagamento, percentagens de métodos e categorias
- [x] Períodos «Esta semana» (segunda a hoje) e «Este ano»; painel com vendas do mês e comparação com o mesmo período do mês anterior, produto e forma de pagamento mais usados
- [x] Exportação Excel (.xlsx) gerada no navegador (fflate, MIT) em todas as tabelas, além do CSV; validada com o Open XML SDK (0 erros)
- [x] Documentos imprimíveis novos: lista de vendas, clientes, IVA e descontos, anulações (80/58 mm ou A4, rodapé com quem e quando)
- [x] Testes: 10 xUnit (totais iguais ao resumo, filtros, horas, IVA por taxa com isento, clientes e telefone por permissão, anulações, painel, documentos e permissões), 5 Vitest (períodos, percentagem, xlsx), E2E (lista de vendas e descarga Excel); browser: lista, IVA e anulações revistos com valores conferidos
- Fora do sistema: tipo «balcão» (é o Take Away) e desconto por produto (o desconto é ao pedido)
- [x] `publicar/LAMGest-Setup.exe` no repositório por Git LFS (passa os 100 MB do GitHub); o `criar-setup.ps1` ignora a pasta `publicar` ao decidir «+alterações»

## Ecrã de entrada táctil (PIN)
- [x] Código por utilizador (4 a 6 algarismos, sem iguais/sequências), cifrado com o hasher do Identity; migration `PinEcraTactil` só aditiva (`PinHash`, `EcraTactil`)
- [x] `GET /api/auth/tactil` (só nomes de quem tem código, está marcado e pode usá-lo), `POST /api/auth/login-pin` (bloqueio partilhado com a palavra-passe: 5 erros → 15 min; auditoria), `POST /api/auth/alterar-pin` (confirma com a palavra-passe), `POST /api/utilizadores/{id}/pin` (definir, mostrar/ocultar, retirar)
- [x] Quem tem `utilizadores.gerir`, `perfis.gerir` ou `configuracoes.gerir` (Administrador) entra só com palavra-passe — também se o perfil mudar depois de ter código
- [x] Ecrã de entrada: grelha de nomes → teclado numérico (também teclado físico), «Entrar com palavra-passe»; posto liga o modo pelo atalho (`?tactil=1`/`0`, opção no instalador) ou pelo botão no ecrã de entrada; «Trocar utilizador» na barra superior
- [x] Testes: 10 xUnit (entrada, bloqueio, códigos fracos, administrador excluído, retirar, mudar o próprio), 10 Vitest (regras, parâmetro do atalho), E2E (código errado → certo), instalador (`tactil=1`/`0` no atalho); browser: grelha e teclado revistos
- [ ] Saída automática por inactividade nos postos tácteis (opcional)

## Conta, vários pagamentos e impressão em PDF
- [x] POS: botão «Pedir conta» (pedido aberto com valor): conta com itens, total, já pago, «A PAGAR» e valor por pessoa, «não serve de recibo»; sai na impressora de caixa do terminal (directa ou PDF) ou pela janela de impressão do posto; fica no histórico e na auditoria (`pedido.conta`)
- [x] Vários pagamentos de uma vez (`POST /api/pedidos/{id}/pagamentos/multiplos`, até 10 linhas): tudo ou nada numa transacção, soma ≤ saldo, regras de troco/referência por método, uma Idempotency-Key (linhas seguintes «chave#n»); modal com «Juntar outro método de pagamento», troco total e o que fica em dívida
- [x] Modo da impressora: «Imprimir directo» ou «Abrir em PDF» (migration `ModoImpressora` só aditiva, existentes = Directo); em PDF o servidor gera o PDF (PDFsharp, mesmo layout de 58/80 mm/A4, logótipo) e envia-o por SignalR só a quem pediu; visualizador na aplicação (`X-Frame-Options: SAMEORIGIN` só nesse endpoint), PDF em memória 2 h; em PDF a impressora do Windows é opcional
- [x] Testes: 147 xUnit (vários pagamentos, tudo-ou-nada, reenvio, conta, PDF só para o dono, PDF nos 3 papéis), E2E: pagamento em 2 métodos e conta em PDF; browser: conta 80 mm em PDF revista, pagamento Dinheiro (troco) + M-Pesa (referência), recibo aberto em PDF
- [x] Correcção: conta e recibo em PDF falhavam com o logótipo («MemoryStream's internal buffer cannot be accessed» no PDFsharp); o logótipo passa a ser lido com buffer visível e uma imagem ilegível nunca impede o documento; teste com o logótipo carregado como na aplicação
- [x] Falha de impressão (directa ou PDF) avisa no posto de quem a pediu (evento `impressaoFalhou`, com a impressora e o motivo)
- [ ] Confirmar no Chrome de um posto que o PDF aparece dentro do visualizador (no navegador de testes usado a área ficou escura, sem leitor de PDF; «Abrir noutra janela» como recurso)

## Ícone LAM
- [x] Logótipo LAM («LAM company — Sistema de vendas») como ícone: `LAMGest-Setup.exe`, janela do assistente, atalhos dos postos (ambiente de trabalho, menu Iniciar, arranque — `lam.ico` copiado para a pasta de dados, em vez do ícone do navegador) e ícone do separador/janela da aplicação (`favicon.ico`, `icone-512.png`); teste do atalho verifica o ícone

## Início da venda no POS
- [x] Sem pedido nem modo escolhido, o POS mostra «Iniciar venda» (mapa de mesas por zona, Take Away, Delivery) em vez dos produtos; o modo activo fica destacado nos botões
- [x] Entrada já com o modo: `/pos?mesa=ID` (Mesas, Reservas), `/pos?tipo=TakeAway|Delivery` (ecrã Delivery → «Novo pedido no POS»), `/pos?pedido=ID`
- [x] Centro de produção: produto > categoria (herdada da categoria-mãe) > regras automáticas — coberto pelo teste `Subcategoria_herda_a_impressora_da_categoria_e_o_produto_pode_escolher_outra`
- [x] Teste E2E: o POS só mostra produtos depois de escolhida a mesa (no mapa) ou Take Away (pelo endereço)
- [x] Correcção: nos postos (http://IP:porta, contexto não seguro) não existe `crypto.randomUUID` e escolher a mesa/Take Away/Delivery falhava sem aviso; a chave de idempotência passou a usar `crypto.getRandomValues` (teste Vitest e E2E sem `randomUUID`)

## Instalador (LAMGest-Setup.exe)
- [x] Número de posto nos terminais (único, migration `NumeroPosto` só aditiva), consulta pública `GET /api/postos/{n}`, ecrã Caixa → Terminais com «N.º posto»
- [x] Frontend: atalho com `?posto=N` associa o navegador ao terminal antes de arrancar; barra superior mostra «Posto N · nome» ou «não registado»
- [x] Comando `migrar` (e `migrar --listar`) na API; ficheiro de configuração protegido indicado por `LAMGEST_CONFIG`
- [x] Assistente WinForms (administrador, ficheiro único self-contained ~100 MB): modo Servidor (instâncias locais detectadas, autenticação Windows ou SQL `sa`+palavra-passe, só bases `LAMGestRestaurant*`, análise da base vs. migrations do pacote, recusa bases de outros sistemas e versões mais recentes, cópia de segurança + confirmação antes de actualizar base com dados, primeiro administrador, serviço com conta virtual, login SQL próprio com palavra-passe aleatória, ACLs, firewall, tarefa de backup como SYSTEM com permissões mínimas, verificação de `/api/saude`) e modo Posto (teste do servidor, confirmação do número do posto, atalhos do Chrome em modo aplicação (Edge só se não houver Chrome e o utilizador confirmar), arranque automático, ecrã inteiro)
- [x] `scripts/criar-setup.ps1` gera o instalador — testado (101 MB)
- [x] Testes: 11 xUnit do instalador (base nova → migrar → administrador → cópia → API responde ao instalador do posto; base de outro sistema recusada; nomes fora do padrão recusados sem ligar; login SQL do serviço lê/escreve mas não altera estrutura; atalhos e registo do posto); E2E do `?posto=N`; capturas de todas as páginas do assistente revistas
- [ ] Executar o modo Servidor completo numa máquina de teste (cria serviço, firewall e tarefa: não foi executado aqui por alterar o sistema)
- [ ] Desinstalação (hoje: parar/remover o serviço e apagar a pasta manualmente)
- [ ] Assinatura digital do Setup.exe (sem ela o Windows SmartScreen pode avisar)

## Documentos oficiais (facturação)
- [x] Modelo único `Documento` + linhas + liquidações (migration `Facturacao` só aditiva: 3 tabelas novas): Venda a Dinheiro (VD), Factura (FT), Recibo (RC), Cotação (CT) e Nota de Crédito (NC); numeração por tipo e ano sem saltos (`VD-2026-000001`…); emitente e cliente copiados na emissão; linhas e liquidações só por inserção; criação idempotente
- [x] POS: cada pedido pago emite a VD (substitui o recibo interno `REC`; o número fica no recibo do pedido); cliente do pedido com NUIT ou «Consumidor final»; desconto do pedido repartido pelas linhas (IVA de cada linha sobre o valor pago); taxa de entrega como linha isenta; o estorno emite a NC total da VD; impressão/reimpressão do recibo passa a ser a VD
- [x] Factura a crédito (cliente da ficha obrigatório, vencimento pelo prazo configurado), linhas de produto (preço editável) ou livres com taxa de IVA; recibos parciais ou de várias facturas (dinheiro exige caixa aberto e entra na sessão como entrada); conta corrente do cliente (em dívida e vencido)
- [x] Nota de crédito sobre factura, total ou por linhas/quantidades, limitada ao valor em dívida (para creditar o pago anula-se o recibo, que devolve a dívida e retira o dinheiro da caixa aberta); VD só se corrige pelo estorno; facturas/VD/NC nunca se anulam
- [x] Cotação com validade e «não serve de factura»; conversão única em factura (mesmas linhas e preços)
- [x] Documento impresso (80/58 mm, A4, PDF e impressoras do servidor): NUIT do emitente e do cliente, IVA por taxa (base e imposto), motivo de isenção nas linhas a 0 % (configurável), já pago/creditado/em dívida, documento de origem, «Processado por computador»
- [x] Permissões `facturacao.ver` / `facturacao.emitir` / `facturacao.anular` (perfil Caixa novo: só consulta); configurações `facturacao.motivo-isencao`, `facturacao.prazo-dias`, `facturacao.validade-cotacao-dias`
- [x] Ecrã Facturação: lista com filtros (tipo, estado, período, pesquisa, só em dívida), detalhe com documentos relacionados, nova factura/cotação, recibo, nota de crédito, conversão, anulação e impressão
- [x] Testes: 181 xUnit (5 novos: VD com desconto repartido e NUIT, estorno → NC, factura → recibo parcial → NC → anulação do recibo → NC do resto, cotação → factura, permissões e numeração sob concorrência), 28 Vitest, 7/7 E2E; browser: fluxo completo e documentos impressos (FT A4, VD e NC 80 mm) revistos
- [ ] Confirmar com o contabilista os textos legais (motivo de isenção, taxa de entrega isenta) e se é exigida certificação do programa pela AT
- [ ] Emitir VD/factura fora do POS com baixa de stock (hoje as facturas não movimentam stock)
- [ ] Nota de débito e guia de remessa

## Fecho de caixa cego e produtos por categoria
- [x] Fecho cego: com a sessão aberta, a API (`GET /api/caixa/sessoes/{id}`) não devolve recebimentos, entradas/saídas por método, esperado nem diferença — só o fundo, o n.º de pedidos, os movimentos manuais e os métodos a contar (`contar`); o painel deixou de mostrar o esperado dos caixas abertos; a mensagem de saída acima do disponível já não revela o valor
- [x] Ecrã do caixa e diálogo de fecho só pedem o valor contado; a confirmação mostra o que foi contado; o esperado e as diferenças aparecem no relatório depois do fecho
- [x] Documento do caixa: aberto (ponto de situação) sem valores esperados, com os métodos a contar; fechado com esperado/contado/diferença por método e os produtos vendidos por categoria (quantidades e valores, descontos, taxas de entrega e total das vendas)
- [x] Testes: 182 xUnit (novo: ponto de situação sem esperado e fecho com produtos por categoria; testes que verificavam o esperado de sessões abertas passaram a lê-lo no servidor), 28 Vitest, 7/7 E2E; browser: caixa aberto, fecho com 5 MT a menos e documento revistos

## Requisitos do servidor no Setup e guia de instalação
- [x] Setup (modo Servidor) verifica antes de instalar: SQL Server instalado (sem ele: ligação para o SQL Server Express e «Procurar de novo» sem fechar o assistente), serviço do SQL Server parado, instância inacessível (mensagem clara em vez do erro técnico), sqlcmd em falta com cópia diária escolhida (como instalar), espaço em disco (≥ 1 GB) e porta ocupada por outro programa
- [x] `backup-sql.ps1` encontra o sqlcmd fora do PATH (pastas do SQL Server/ODBC e do winget) — testado com a base E2E
- [x] `docs/INSTALACAO_LAN.md` reescrito para o Setup (requisitos, preparação, instalação, licença, postos, verificação, actualização, mudança de servidor, HTTPS, desinstalação, problemas); `docs/TESTE_INSTALACAO.md` com a lista de teste num computador limpo; SETUP.md e BACKUP.md actualizados
- [x] Testes: 16 xUnit do instalador (4 novos); capturas das páginas SQL e Aplicação revistas
- [ ] Executar `docs/TESTE_INSTALACAO.md` num computador limpo (não foi possível aqui: o servidor tem 6,4 GB livres e 4 GB de RAM, e o Hyper-V exigiria reiniciar o servidor em produção)

## Documentos impressos: logótipo em todos e nova disposição
- [x] O logótipo carregado em Configurações → Geral sai em todos os documentos e papéis (58/80 mm e A4: recibos, VD, facturas, contas, relatórios, fecho de caixa e talões); acabou a regra por papel (`UsoLogotipo`)
- [x] Motor de disposição único (`Disposicao`, em mm) usado pela impressão directa (GDI) e pelo PDF — saem iguais; o navegador segue as mesmas regras
- [x] A4: logótipo à esquerda e identificação do estabelecimento à direita; tabelas em colunas com títulos sombreados (repetidos em cada página); rodapé com o título e «Página N de M»
- [x] Talões: logótipo e identificação centrados; tabelas numa linha por registo quando cabem (ex.: resumo do IVA) e, senão, descrição numa linha e valores em colunas por baixo
- [x] Documentos com tabelas de artigos (Descrição, Qtd., Preço, IVA, Total) e resumo do IVA; valores das colunas sem moeda (vai no título)
- [x] Testes: 182 xUnit (incl. impressão real para «Microsoft Print to PDF» em 58/80 mm e A4), 29 Vitest, 7/7 E2E; revisão visual de VD (58/80 mm, A4), factura A4 de 2 páginas, talão e da impressão pelo navegador

## Stock de todos os produtos e recibos impressos pelo posto
- [x] Produtos controlam stock por omissão (`ControlaStock`, migration `ControloStockProdutos` só aditiva, existentes = sim): sem ficha técnica recebem um artigo de revenda (mesmo código e nome; se o código já for de outro artigo, «código-P») e a ficha 1:1 — aparecem no Stock, inventários e movimentos e a venda baixa-os; pratos com ficha de ingredientes continuam a baixar os ingredientes
- [x] No arranque do servidor os produtos antigos sem artigo são activados (idempotente); sem stock inicial o artigo permite stock negativo (as vendas nunca ficam bloqueadas); «Não controlar stock» no produto: a venda não mexe no stock
- [x] Recibos com impressora USB no posto: `GET /api/pedidos/recibo/destino`; sem impressora de caixa no servidor para o terminal, o POS imprime a VD no posto automaticamente ao fechar o pedido; impressão pelo navegador numa moldura escondida (sem janelas que o navegador bloqueia)
- [x] Instalador do posto: opção «Impressão directa» (Chrome/Edge com `--kiosk-printing` e perfil próprio com permissão para todos os utilizadores do posto)
- [x] Registo: recusas de regras de negócio deixaram de aparecer como «500/ERR» no log (o registo dos pedidos vê o estado final)
- [x] Testes: 185 xUnit (3 novos: produto novo no stock e venda sem bloqueio, não controlar/activar/renomear, activação dos antigos), 16 do instalador, 29 Vitest, 7/7 E2E; browser: produto vendido aparece no Stock com −2 e o recibo saiu no posto sem impressora no servidor
- [ ] Papel de 58 mm nos postos com impressão directa (hoje o recibo do posto sai no formato de 80 mm)

## Impressoras de cada posto (agente de impressão)
- [x] Cada impressora fica «Ligada a» Servidor ou a um posto (`Impressora.PostoId`); migration `ImpressorasPostos` só aditiva (colunas do agente em `Terminais`, `PostoId` em `Impressoras`, tabela `TrabalhosImpressaoPosto`)
- [x] Agente de impressão (`backend/LAMGestRestaurant.Agente`, ícone junto ao relógio): envia as impressoras instaladas no Windows do posto, recebe os trabalhos por pedido longo, imprime-os com o mesmo desenho do servidor (biblioteca partilhada `LAMGestRestaurant.Desenho`) e devolve o resultado (estado da impressora e aviso a quem pediu)
- [x] API: `POST /api/postos/{n}/agente` (configuracoes.gerir) gera a chave (guardada em hash SHA-256), `DELETE /api/terminais/{id}/agente` retira-a, `api/agente/*` autenticado por posto + chave; reentrega ao fim de 2 min, trabalhos com mais de 30 min falham
- [x] Corrigido: `[AllowAnonymous]` na classe `PostosController` anulava as permissões dos outros endpoints dela (agora só na consulta pública)
- [x] Setup (modo Posto): opção «Agente de impressão» com conta de administrador; instala em Programas, `agente.json` protegido (utilizadores só lêem), arranque automático para todos os utilizadores; `criar-setup.ps1` publica e embebe o agente
- [x] Frontend: «Ligada a» e lista das impressoras do posto no formulário; estado do agente (ligado, versão, impressoras) e «Desligar o agente» em Caixa → Terminais
- [x] Testes: 189 xUnit (5 novos: registo/chave/revogação, impressoras só do próprio posto, talão da cozinha no posto, ciclo do agente contra a API), 18 do instalador (2 novos), 29 Vitest, 8/8 E2E (novo: executável real do agente ligado ao servidor de testes, posto na escolha «Ligada a» com as impressoras deste Windows e estado em Terminais)
- [ ] Impressão em papel por um posto real (aqui as impressoras são redireccionadas do Ambiente de Trabalho Remoto; não foi enviado nada para papel)

## Impressoras que imprimiam uma vez e paravam
- [x] Causa provável (não reproduzida aqui com a impressora do cliente): a fila tratava os documentos um a um; um controlador preso (impressora desligada, fila do Windows em erro, janela do controlador que o serviço não mostra) deixava tudo o resto à espera, sem erro
- [x] Servidor: uma fila por impressora (uma impressora presa não atrasa as outras; na mesma impressora mantém a ordem) e tempo máximo por impressão (`Impressao:TempoMaximoSegundos`, 60 s): ao fim dele fica o erro «A impressora não respondeu…» no estado e no aviso, e os documentos seguintes continuam
- [x] Agente do posto: o mesmo tempo máximo; o trabalho preso é dado como falhado e o agente continua
- [x] Testes: 191 xUnit (2 novos: impressora presa não pára o bar nem os envios seguintes; agente sempre a correr com 4 envios seguidos e o primeiro trabalho preso), 18 do instalador
- [ ] Confirmar no restaurante com a impressora real (estado em Configurações → Impressoras e fila do Windows dessa impressora)

## Várias sessões de caixa no mesmo terminal (uma por operador)
- [x] Cada utilizador com permissão de caixa abre a sua própria sessão no terminal (fundo, pagamentos, movimentos e fecho cego próprios); continua uma única sessão aberta por utilizador
- [x] `GET /api/caixa/terminais/{id}/sessao-aberta` devolve a sessão do próprio utilizador nesse terminal (POS, pagamentos, estornos, recibos e despesas usam-na); «Sessões abertas» lista todos os operadores do terminal
- [x] Migration `SessoesCaixaPorOperador` (só índices, sem alterar dados): sai o índice único por terminal, entra o único por utilizador com sessão aberta (criado não único se uma base antiga tiver duplicados)
- [x] Testes: 191 xUnit (reescritos: dois operadores no mesmo terminal, cada um com a sua sessão; aberturas simultâneas do mesmo operador; integridade na BD)

## Configurações «Documentos e POS», conta com dados para factura e venda ao Balcão
- [x] Aba Configurações → Documentos e POS: fecho imprime produtos; contagem cega (o fecho impresso mostra só o valor contado); logótipo nos documentos de 80 mm; cópias de 1 a 10 por documento (talão, anulação, conta, venda a dinheiro, factura, recibo, cotação, nota de crédito, fecho, relatórios), nas impressoras do servidor, dos postos e no navegador (em modo PDF abre uma vez); botões Mesa, Take Away, Delivery e Balcão (o servidor recusa o tipo desligado e fica sempre pelo menos um) e leitor de código de barras
- [x] Conta: depois do rodapé, linhas para preencher à mão o nome comercial e o NUIT
- [x] Venda ao Balcão (`TipoPedido.Balcao`, sem migration): sem mesa nem entrega; talões da cozinha/bar saem ao pagar; depois de paga, o ecrã fica pronto para a venda seguinte
- [x] Leitor de código de barras no POS (teclado rápido + Enter, leituras em fila); sem venda iniciada começa uma venda ao Balcão; Enter no campo de pesquisa também aceita o código
- [x] Avisos passaram para o topo direito (em baixo tapavam o botão Pagar)
- [x] Corrigido: um aviso em tempo real atrasado substituía a venda nova pelo pedido anterior no POS (só se aplica se o pedido ainda estiver no ecrã)
- [x] Testes: 197 xUnit (6 novos), 18 do instalador (sem alterações), 30 Vitest, 8/8 E2E (novo: Balcão com leitor no navegador e botões do POS; o do agente ignorado por haver um agente aberto neste computador); aba, POS e conta (80 mm) revistos em captura
- [ ] Confirmar com um leitor de código de barras físico e com cópias numa impressora real

## Conta paga finaliza o pedido no ecrã da cozinha
- [x] Ao ficar paga a conta (pagamento total), os itens pendentes, em preparação ou prontos passam a entregues e saem do ecrã da cozinha, com registo no histórico do pedido; pagamento parcial não altera nada
- [x] Take Away/Delivery ainda por preparar passam a «Pronto» na lista de entregas; o ecrã e o contador do painel só contam pedidos por pagar (os antigos já pagos também desaparecem)
- [x] Balcão: o talão sai na cozinha/bar ao pagar, mas o pedido não aparece no ecrã (já está pago)
- [x] Testes: 198 xUnit (1 novo: parcial mantém, total finaliza, Take Away pronto; 2 ajustados à regra nova)
