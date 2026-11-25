using LAMGestRestaurant.Instalador.Motor;

namespace LAMGestRestaurant.Instalador.Tests;

/// <summary>Regra da firewall do servidor: rede local e VPN dos tablets, nunca a internet.</summary>
public class FirewallTests
{
    [Fact]
    public void Sem_Radmin_so_rede_local_e_Tailscale() =>
        Assert.Equal("localsubnet,100.64.0.0/10", InstalacaoServidor.RemotosFirewall(radmin: false));

    [Fact]
    public void Com_a_Radmin_VPN_no_servidor_tambem_a_rede_Radmin() =>
        Assert.Equal("localsubnet,100.64.0.0/10,26.0.0.0/8", InstalacaoServidor.RemotosFirewall(radmin: true));
}
