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
- A porta do LAMGest só aceita ligações da rede local e da VPN (100.64.0.0/10); nunca da internet.
- Para desligar: Configurações → Postos → «Desligar». Os tablets deixam de usar a VPN na próxima vez que abrirem no restaurante.

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
