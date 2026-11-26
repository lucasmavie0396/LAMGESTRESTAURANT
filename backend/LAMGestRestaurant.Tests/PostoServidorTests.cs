using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>O servidor é também o primeiro posto: o instalador cria-o pela base de dados (abre por localhost, sem IP).</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class PostoServidorTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Posto_do_servidor_e_criado_pela_base_reutilizado_ao_repetir_e_marcado_na_lista()
    {
        await using var db0 = app.CriarContexto();
        var anterior = await db0.Configuracoes.AsNoTracking().FirstOrDefaultAsync(c => c.Chave == ChavesConfiguracao.PostoServidor);
        var ocupados = await db0.Terminais.Where(t => t.Numero != null).Select(t => t.Numero!.Value).ToListAsync();
        var numero = Enumerable.Range(700, 299).First(n => !ocupados.Contains(n));
        try
        {
            var criado = await app.ComServicos(s => s.GetRequiredService<ServicoPostoServidor>().GarantirAsync(numero));
            Assert.True(criado.Criado);
            Assert.StartsWith($"Posto {numero} — Servidor", criado.Nome);

            // O instalador pode correr de novo: o posto é o mesmo.
            var repetido = await app.ComServicos(s => s.GetRequiredService<ServicoPostoServidor>().GarantirAsync(numero));
            Assert.Equal((criado.TerminalId, false), (repetido.TerminalId, repetido.Criado));

            await using (var db = app.CriarContexto())
            {
                var t = await db.Terminais.SingleAsync(x => x.Id == criado.TerminalId);
                Assert.Equal((numero, true, ServicoPostoServidor.DescricaoPadrao), (t.Numero, t.Activo, t.Descricao));
                Assert.Equal(1, await db.RegistosAuditoria.CountAsync(a => a.Accao == "posto.servidor" && a.EntidadeId == criado.TerminalId.ToString()));
                Assert.Equal(1, await db.Terminais.CountAsync(x => x.Numero == numero));
            }

            // Configurações → Postos: só este aparece como o do servidor.
            var admin = (await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador)).Cliente;
            var lista = await admin.Obter<List<PostoEstadoDto>>("/api/configuracoes/postos");
            Assert.Equal([criado.TerminalId], lista.Where(p => p.DoServidor).Select(p => p.Id));

            // Agente de impressão do servidor: chave gerada sem login (o instalador tem a base).
            var chave = await app.ComServicos(async s => (await s.GetRequiredService<ServicoAgentes>().RegistarAsync(numero)).Chave);
            Assert.Equal(criado.TerminalId, await app.ComServicos(s => s.GetRequiredService<ServicoAgentes>().AutenticarAsync(numero, chave)));

            // Desactivado por engano: o instalador volta a activá-lo.
            await using (var db = app.CriarContexto())
                await db.Terminais.Where(x => x.Id == criado.TerminalId).ExecuteUpdateAsync(x => x.SetProperty(t => t.Activo, false));
            await app.ComServicos(s => s.GetRequiredService<ServicoPostoServidor>().GarantirAsync(numero));
            await using (var db = app.CriarContexto())
                Assert.True((await db.Terminais.SingleAsync(x => x.Id == criado.TerminalId)).Activo);

            await Assert.ThrowsAsync<RegraNegocioException>(() => app.ComServicos(s => s.GetRequiredService<ServicoPostoServidor>().GarantirAsync(0)));
        }
        finally
        {
            // Outros testes listam os postos: repõe a configuração como estava.
            await using var db = app.CriarContexto();
            await db.Configuracoes.Where(c => c.Chave == ChavesConfiguracao.PostoServidor).ExecuteDeleteAsync();
            if (anterior is not null)
            {
                db.Configuracoes.Add(new Configuracao { Chave = anterior.Chave, Valor = anterior.Valor, Descricao = anterior.Descricao });
                await db.SaveChangesAsync();
            }
        }
    }
}
