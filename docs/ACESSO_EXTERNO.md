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
