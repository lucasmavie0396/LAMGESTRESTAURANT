# Tablets fora do restaurante (dados móveis) — VPN privada

O LAMGest funciona na rede local do restaurante. Para a app de venda (tablets/telemóveis Android) funcionar também
**fora do restaurante, com dados móveis**, usa-se uma **VPN privada**: o servidor e os tablets entram numa rede privada
cifrada, sem abrir o servidor à internet. Recomenda-se o **Tailscale** (o ZeroTier também serve).

Corresponde ao «Acesso externo» das versões Pro e Plus.

## O que é preciso

- Internet no servidor (sempre ligada) e nos tablets (dados móveis ou outro Wi-Fi).
- Uma conta Tailscale do restaurante (o plano gratuito chega para poucos aparelhos).
- O **Setup novo** no servidor (abre a porta na firewall também para a VPN) e a **app de venda nova** nos tablets.

## Passo a passo

1. **Servidor**: instalar o Tailscale para Windows (tailscale.com/download), entrar com a conta do restaurante e deixá-lo
   a arrancar com o Windows. O servidor recebe um endereço `100.x.y.z`.
2. **Tablets**: instalar a app Tailscale (Play Store), entrar com a **mesma** conta e ligar a VPN.
3. **LAMGest** → Configurações → **Postos** → «Tablets fora do restaurante (dados móveis)»:
   - toque no endereço da VPN detectado (ex.: `http://100.101.102.103:8080`) ou escreva-o;
   - **Guardar**.
4. Abra a app de venda em cada tablet **uma vez no Wi-Fi do restaurante**: a app recebe e guarda o endereço da VPN.
   (Os tablets novos emparelham-se sempre no Wi-Fi do restaurante, como até agora.)
5. Teste: desligue o Wi-Fi do tablet, use dados móveis com a VPN ligada e abra a app. Aparece «Ligado fora do restaurante (VPN)».

## Como funciona

- A app tenta primeiro o servidor na rede do restaurante; se não responder, usa o endereço da VPN.
- Ao mudar de rede pede-se de novo a entrada (a sessão é por endereço).
- A porta do LAMGest só aceita ligações da rede local e da VPN (100.64.0.0/10; com a Radmin VPN instalada no servidor quando o Setup corre, também 26.0.0.0/8); nunca da internet.
- Com «Só HTTPS» e o endereço da VPN em HTTP, a app passa sozinha para o HTTPS do mesmo servidor (se a autoridade for a desta loja).
- Se a página de venda não aparecer em 40 segundos, ou faltar um ficheiro do sistema, a app mostra o erro e o endereço (antes ficava um ecrã branco).

## Se a app fica em branco ou não abre pela VPN

No telemóvel, sem Wi-Fi e com a VPN ligada, abra no **Chrome**:
1. `http://ENDEREÇO-DA-VPN:8080/api/saude` — tem de mostrar o estado do servidor;
2. `http://ENDEREÇO-DA-VPN:8080/pos` — tem de aparecer a entrada do sistema.

Se no Chrome também não abrir (ou ficar a carregar), o problema é a VPN ou os dados móveis, não a app.

## Radmin VPN

A Radmin VPN só tem programa oficial para **Windows** (endereços 26.x.x.x). Não há app oficial para Android: as apps que usam esse
nome na loja são de outros autores e todo o tráfego do sistema passaria por elas. Para telemóveis e tablets use o **Tailscale**.
Entre computadores Windows (ex.: o portátil do gerente), a Radmin funciona: o LAMGest reconhece a placa Radmin em Configurações → Postos
e o Setup abre a firewall à rede Radmin.
- Para desligar: Configurações → Postos → «Desligar». Os tablets deixam de usar a VPN na próxima vez que abrirem no restaurante.
- Com HTTPS no servidor, use o endereço da VPN em HTTPS (ex.: `https://100.101.102.103:8443`; aparece assim quando abre Configurações → Postos pelo endereço HTTPS). O Tailscale tem de estar ligado no servidor quando o Setup corre, para o certificado incluir o endereço `100.x.y.z`; se o ligou depois, volte a correr o Setup no servidor.

## Atenção

- Sem sinal de dados (ou com a VPN desligada) não há venda: o sistema bloqueia as gravações até a ligação voltar.
- Retire da conta Tailscale os aparelhos perdidos ou de funcionários que saíram (painel do Tailscale → Machines → Remove).

## Várias lojas (cada uma com o seu servidor)

Quando o mesmo dono tem várias lojas, todas entram na **mesma VPN** (mesma conta Tailscale): os servidores e os aparelhos
do dono e dos gerentes. Cada loja continua independente (base de dados, stock, caixas e documentos próprios).

1. Em cada servidor, Configurações → Postos → «Tablets fora do restaurante»: guarde o endereço da VPN desse servidor.
2. Numa das lojas, Configurações → **Lojas**: escreva o endereço da VPN de outra loja e «Acrescentar loja» (o sistema confirma o
   nome e o NUIT dessa loja). Escolha também a **cor** desta loja (aparece no topo do ecrã e no seletor).
3. Nas outras lojas, use «Importar a lista dessa loja» com o endereço da primeira: ficam com todas as lojas de uma vez.
4. No ecrã de entrada (computador) aparece o seletor **«Loja»**: escolher outra loja abre a entrada do servidor dela.
   Dentro do sistema, quem tem a permissão «Mudar de loja» tem o mesmo seletor no topo; os restantes só vêem o nome da loja.
5. App Android de **gestor**: no ecrã «Ligação ao servidor», marque «Aparelho de gestor» e emparelhe na primeira loja; a app
   recebe as outras lojas da lista e mostra o seletor «Loja» no ecrã de entrada. Para vender numa loja (caixa e impressora do posto),
   emparelhe também no Wi-Fi dessa loja. Os tablets de garçom continuam ligados só à sua loja.

Os utilizadores são de cada loja: o dono precisa de um utilizador em cada servidor.
