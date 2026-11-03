# LAMGest Restaurant ERP

Sistema web de gestão integrada de restaurante/bar (POS, mesas, cozinha, caixa, stock, compras, clientes, relatórios) para funcionar numa rede local com um servidor central e SQL Server.

> **Estado:** fases 1–5 implementadas (ver [docs/TAREFAS.md](docs/TAREFAS.md)). Antes de usar com dinheiro real, siga [docs/INSTALACAO_LAN.md](docs/INSTALACAO_LAN.md), [docs/BACKUP.md](docs/BACKUP.md) e a lista [docs/PRODUCAO.md](docs/PRODUCAO.md).

## Pré-requisitos (servidor)
- .NET SDK 9.0
- Node.js 22 LTS e npm
- Microsoft SQL Server 2019+ (Express serve) com autenticação Windows ou SQL
- Google Chrome nos terminais (os atalhos dos postos abrem o Chrome; o Edge só serve de recurso)

## Estrutura
```text
backend/   Solução .NET (Domain, Application, Infrastructure, Api, Tests)
frontend/  React + TypeScript + Vite
docs/      Documentação (tarefas, instalação LAN, backup)
scripts/   Scripts auxiliares
```

## Configuração da base de dados (desenvolvimento)
A connection string **nunca** fica no repositório. Em desenvolvimento usa-se user-secrets:

```bash
cd backend
dotnet user-secrets --project LAMGestRestaurant.Api set "ConnectionStrings:Default" "Server=.\SYSON;Database=LAMGestRestaurantDB;Trusted_Connection=True;TrustServerCertificate=True"
```

Em produção usa-se a variável de ambiente `ConnectionStrings__Default` (ver `.env.example`).

## Base de dados: migrations
As migrations **não** são aplicadas automaticamente no arranque. Para criar ou actualizar a base:

```bash
cd backend
dotnet tool restore
dotnet ef database update -p LAMGestRestaurant.Infrastructure -s LAMGestRestaurant.Api
```

Antes de actualizar uma base com dados reais, faça backup e reveja o SQL gerado:

```bash
cd backend
dotnet ef migrations script --idempotent -p LAMGestRestaurant.Infrastructure -s LAMGestRestaurant.Api -o migracao.sql
```

Com a base actualizada, a API semeia no arranque os dados de referência em falta (perfis, permissões, métodos de pagamento, unidades, armazém principal, configurações). Não cria produtos, mesas nem clientes.

## Primeiro administrador
Corra uma única vez, na consola do servidor (a palavra-passe é pedida sem eco e não fica gravada em nenhum ficheiro):

```bash
cd backend
dotnet run --project LAMGestRestaurant.Api -- bootstrap-admin
```

O comando recusa-se a correr se já existir um administrador.

## Executar em desenvolvimento
API em `http://localhost:5080` (Swagger em `/swagger`):

```bash
cd backend
dotnet run --project LAMGestRestaurant.Api
```

Frontend em `http://localhost:5173` (reencaminha `/api` e `/hubs` para a API):

```bash
cd frontend
npm run dev
```

Se a porta 5173 estiver ocupada por outra aplicação, use outra porta:

```bash
cd frontend
npm run dev -- --port 5180
```

## Testes
Os testes do backend criam e recriam a base **`LAMGestRestaurantDB_Tests`** no servidor `.\SYSON` (altere com a variável `LAMGEST_TESTES_SQL`). Nunca tocam na base principal.

```bash
cd backend
dotnet test
```
```bash
cd frontend
npm test
```

Testes E2E (Playwright, usando o Edge instalado): recriam a base **`LAMGestRestaurantDB_E2E`**, criam um administrador com palavra-passe aleatória (apagada no fim), arrancam a API na porta 5091 e o frontend na 5181:

```bash
cd frontend
npm run e2e
```

Cobrem o login, a venda à mesa com a cozinha em tempo real noutro separador (envio → em preparação → pronto → pagamento → recibo → mesa livre → relatório) e o bloqueio das gravações quando o servidor deixa de responder. Noutra máquina sem Edge: `PW_CANAL=chrome npm run e2e`.

## Instalador (servidor e postos)
```powershell
.\scripts\criar-setup.ps1
```
Gera `publicar\LAMGest-Setup.exe`, um único ficheiro sem dependências. No servidor pede a instância e a autenticação do SQL Server (Windows ou `sa` + palavra-passe), cria ou actualiza a base, o primeiro administrador, o serviço, a firewall e a cópia diária. Nos postos pede o IP do servidor e o número do posto e cria o atalho. Ver [docs/SETUP.md](docs/SETUP.md).

Testes do instalador (bases `*_Tests` próprias; não cria serviços nem altera a firewall):
```bash
cd instalador/LAMGestRestaurant.Instalador.Tests
dotnet test
```

## Publicação manual (alternativa ao instalador)
```powershell
.\scripts\publicar.ps1
```
Gera `publicar\LAMGestRestaurant` (API + frontend em `wwwroot` + `migracoes.sql` + scripts). A instalação como serviço Windows, firewall, HTTPS e actualizações estão em [docs/INSTALACAO_LAN.md](docs/INSTALACAO_LAN.md); as cópias de segurança em [docs/BACKUP.md](docs/BACKUP.md).

## Autenticação e permissões
- Sessão por cookie HttpOnly (`LAMGest.Sessao`, SameSite=Strict, 12 h deslizantes).
- 5 tentativas falhadas bloqueiam a conta durante 15 minutos.
- Permissões no formato `modulo.operacao`, atribuídas a perfis. O perfil Administrador recebe sempre todas.
- Recuperação de palavra-passe: não há envio de e-mail (rede local). O administrador repõe a palavra-passe em **Utilizadores** (a conta é também desbloqueada) e o utilizador é obrigado a alterá-la no login seguinte. Até a alterar, a API só aceita pedidos a `/api/auth`.
- Alterações de perfis/permissões e desactivação de contas reflectem-se nas sessões abertas em até 1 minuto.

## Regras de caixa (turnos e terminais)
- Cada computador de caixa é registado como **terminal** (Caixa → Terminais, permissão `configuracoes.gerir`). Em cada computador escolhe-se uma vez o terminal correspondente; a escolha fica guardada no navegador desse computador.
- Uma sessão de caixa corresponde a um turno: só pode existir **uma sessão aberta por terminal** e **uma por utilizador**.
- O valor esperado é calculado no servidor apenas com as operações dessa sessão: fundo inicial (em dinheiro) + pagamentos válidos + entradas − saídas.
- Só quem abriu a sessão, ou um utilizador com `caixa.ver-todas` (Gerente), a pode movimentar, consultar ou fechar.
- No fecho conta-se cada método; as diferenças ficam gravadas e a sessão não pode ser reaberta.

## Licença
O sistema só trabalha normalmente com uma **licença** emitida pela LAM company para o estabelecimento (nome comercial, NUIT e validade). A licença é um texto assinado digitalmente (`LAMGEST1.…`, ou um ficheiro `.lic`) e activa-se em **Configurações → Licença**; o nome comercial e o NUIT do estabelecimento passam a ser os da licença (o NUIT deixa de se poder mudar à mão). O sistema verifica a licença no arranque e de 5 em 5 minutos:
- faltam 30 dias ou menos: aviso em todas as páginas;
- expirou há 7 dias ou menos: aviso a vermelho, mas continua a funcionar (tolerância);
- expirada há mais de 7 dias, inexistente ou inválida: **vendas e gravações bloqueadas**; consultas e relatórios continuam, e o administrador pode entrar e activar a licença renovada.

Alterar a validade na base de dados, usar uma licença de outro NUIT ou atrasar o relógio do servidor torna a licença inválida.

**Gerador de licenças (só LAM company):** `publicar\LAMGest-Licencas.exe`, gerado por `.\scripts\criar-gerador-licencas.ps1` (não vai no Setup dos clientes). Preenche-se o nome comercial, o NUIT e a validade → **Gerar licença** → copiar ou guardar o `.lic`. Um NUIT é um estabelecimento: **Renovar (+1 ano)** emite uma licença nova a partir do fim da actual. A chave privada que assina as licenças fica cifrada no perfil do Windows de quem a criou (`%APPDATA%\LAMGest\Licencas`); faça uma **cópia de segurança** (menu Chave → Exportar cópia de segurança, com palavra-passe) e guarde-a fora do computador: sem a chave não é possível emitir licenças aceites pelos sistemas já instalados. A chave pública correspondente está em `backend/LAMGestRestaurant.Licenciamento/chave-publica.txt`.

## Ecrã de entrada táctil (código)
Nos postos com o ecrã táctil (opção «Ecrã de entrada táctil» no instalador, ou o botão «Ecrã táctil» no ecrã de entrada), aparecem botões com o nome de cada utilizador; toca-se no nome e escreve-se o **código de 4 a 6 algarismos** num teclado no ecrã. O administrador define o código e se o utilizador aparece no ecrã em **Utilizadores → «Código do ecrã táctil»**; cada utilizador pode mudar o seu em «Alterar palavra-passe» (confirma com a palavra-passe). Regras:
- quem gere utilizadores, perfis ou configurações (ex.: **Administrador**) entra sempre com a palavra-passe («Entrar com palavra-passe»);
- códigos fracos são recusados (1111, 1234, 4321…); 5 tentativas erradas bloqueiam a conta 15 minutos (o mesmo bloqueio da palavra-passe);
- o código é cifrado como a palavra-passe e cada entrada fica na auditoria; nesses postos o botão de sair passa a «Trocar utilizador».

## Fluxo de venda
1. **Caixa:** cada terminal tem de ter o caixa aberto para receber pagamentos.
2. **POS:** o ecrã abre em «Iniciar venda»: tocar numa mesa do mapa, em Take Away ou em Delivery (ou chegar já com a mesa escolhida a partir do ecrã Mesas/Reservas, ou com Delivery a partir do ecrã Delivery). Os produtos só aparecem depois disso; o modo activo fica destacado. O pedido é criado no servidor ao primeiro produto.
3. **Enviar:** os itens vão para o ecrã da Cozinha em tempo real e os talões saem no centro de produção de cada item: primeiro o do **produto**, senão o da **categoria** (ou da categoria-mãe), senão as regras automáticas (estação → Bar → Cozinha). Bebidas configuradas como «não vai à cozinha» ficam logo entregues.
4. **Cozinha:** Iniciar → Pronto → Entregue. O POS recebe o aviso «Pronto».
5. **Pedir conta:** o botão «Pedir conta» imprime a **conta** (itens, total, já pago, valor por pessoa) na impressora de caixa do terminal — ou pela janela de impressão do posto, se não houver. É um documento de consulta, não um recibo; fica no histórico do pedido.
6. **Pagar:** total ou parcial (dividir a conta), com um ou **vários métodos de uma vez** («Juntar outro método de pagamento»: ex.: 1 000 em dinheiro + 156 por M-Pesa), troco em dinheiro e referência para M-Pesa/e-Mola/transferência. Os vários pagamentos gravam-se juntos (tudo ou nada). Quando o saldo chega a zero, o pedido fecha, a mesa fica livre, o stock é baixado pela ficha técnica e é emitido o recibo.

**Estorno (reembolso):** um pedido pago pode ser estornado na totalidade no POS («Pedidos» → «Recentes» → abrir → «Estornar pedido»), com a permissão `pagamentos.anular` e motivo obrigatório. O valor sai da sessão de caixa aberta nesse terminal como «Reembolso» (em dinheiro, só até ao valor na gaveta), o recibo é anulado e o stock reposto. Os relatórios mostram os estornos à parte das vendas.

**Relatórios:** separadores com a sua permissão — com `relatorios.vendas`: **Vendas** (resumo, métodos e categorias com percentagem, produtos por facturação ou quantidade, produtos sem vendas, operadores com descontos e formas de pagamento), **Lista de vendas** (venda a venda com filtros de estado, tipo, operador, forma de pagamento, horas e pesquisa; totais de tudo o que foi filtrado), **Clientes** (vendas com cliente associado no POS), **IVA e descontos** (por taxa e por dia/mês) e **Anulações** (cancelamentos, estornos, itens e pagamentos anulados, com motivo e quem); com `relatorios.financeiros`: Financeiro e caixa; com `relatorios.stock`: Stock e compras. Períodos: hoje, ontem, esta semana, últimos 7 dias, este mês, mês anterior, este ano ou personalizado. As vendas contam pela data local (Maputo) em que o pedido foi pago; cancelamentos e estornos pela data em que aconteceram (nunca são facturação); as despesas pela data da despesa; os fechos de caixa pela data de fecho; os movimentos de stock pela data do movimento. Margem e resultado são estimativas e a metodologia aparece no próprio relatório. Cada tabela exporta em **CSV** e **Excel (.xlsx)**, e todos os relatórios imprimem em talão de 80/58 mm ou A4 (com quem e quando o emitiu). O painel mostra também as vendas do mês face ao mesmo período do mês anterior.

O recibo é um **documento interno**, não uma factura fiscal certificada.

**Impressão:** em **Configurações → Impressoras** registam-se as impressoras do Windows do servidor com a função **Caixa** (recibos), **Bar** ou **Cozinha** (talões) e o papel **80 mm**, **58 mm** ou **A4**. O servidor imprime sem diálogo: talões ao «Enviar» no POS e recibo ao fechar o pedido (desligável), em segundo plano — uma impressora parada nunca bloqueia uma venda. Sem impressora de caixa configurada, «Imprimir recibo» usa a janela de impressão do navegador (talão de 80 mm; o navegador tem de permitir pop-ups). Cada impressora tem um **modo**: **Imprimir directo** (o servidor imprime) ou **Abrir em PDF** (o documento abre em PDF no ecrã de quem o pediu, para imprimir ou guardar; dispensa impressora no servidor). Ver [docs/SETUP.md](docs/SETUP.md#impressoras-caixa-bar-cozinha).

**Documentos impressos:** em **Relatórios → Imprimir documento** e no **Caixa → Imprimir** escolhe-se o documento (pagamentos, resumo de vendas, produtos vendidos, despesas, financeiro, stock, fecho de caixa), o papel (talão 80 mm, 58 mm ou A4) e onde imprimir: neste computador (janela do Windows, também PDF) ou numa impressora do servidor. O **logótipo** (Configurações → Geral) sai nos documentos A4 e nos recibos de 80 mm. Cancelar um item ou pedido já enviado imprime um talão de **anulação** no centro de produção que o recebeu; «Reimprimir talões» no POS repete os talões dos itens enviados. Além de Bar e Cozinha, pode haver **outros centros de produção** (ex.: pastelaria), que recebem só o que as categorias ou produtos lhes atribuem.

**Categorias e produtos:** cada categoria pode ter subcategorias (ex.: Bebidas › Cervejas, Vinhos, Cidras) e define a impressora dos pedidos dos seus produtos; uma subcategoria sem impressora usa a da categoria principal e cada produto pode escolher outra. Na criação de um produto de revenda pode-se lançar logo o stock inicial (quantidade, custo e mínimo) — cria o artigo de stock e a ficha técnica 1:1, e cada venda baixa o stock. A imagem do produto pode ser procurada na internet (imagens com licença livre), importada do endereço de uma imagem ou carregada do computador; fica guardada na base de dados (o servidor precisa de internet só para procurar/importar).

**Modos de pagamento:** em **Configurações → Modos de pagamento** criam-se e editam-se (nome, ordem no POS, dá troco, exige referência, conta na gaveta). Nunca se eliminam: desactivam-se, mantendo o histórico.

**Clientes e fidelização:** no POS, «Associar cliente» procura por telefone, nome ou cartão (ou cria o cliente). Com a fidelização activa em Configurações, o pedido pago acumula 1 ponto por cada N MT do total; um estorno retira esses pontos. O resgate de pontos ainda não está disponível.

**Reservas:** a mesa é opcional; duas reservas na mesma mesa não se podem sobrepor. A reserva não ocupa a mesa automaticamente: a mesa fica ocupada quando se abre o pedido.

**Despesas:** com «Paga com dinheiro da caixa deste terminal», a despesa sai da sessão de caixa aberta (entra no valor esperado do fecho). Anular uma despesa de uma caixa já fechada não altera esse fecho.

**Funcionários:** ligue cada garçom ao seu utilizador para que os pedidos que abre fiquem atribuídos a ele (relatório «Por garçom»).

**Tempo real:** usa SignalR em `/hubs/operacoes` (WebSockets na mesma porta da aplicação). Se a ligação cair, o indicador «Tempo real» na barra superior avisa, a cozinha passa a actualizar a cada 15 segundos e a ligação é retomada automaticamente.

## Resolução de problemas
- **"Connection string 'Default' não configurada":** em desenvolvimento, confirme os user-secrets e que a API corre com o perfil `http` (ambiente Development).
- **`/api/saude` devolve 503 com `migracoesPendentes: true`:** aplique as migrations (ver acima).
- **Não liga ao SQL Server:** confirmar que o serviço `SQL Server (SYSON)` está a correr e o nome da instância na connection string.
- **Outros computadores não acedem:** ver [docs/INSTALACAO_LAN.md](docs/INSTALACAO_LAN.md) (firewall, rede privada, endereço IP).
