using LAMGestRestaurant.Application.Impressao;

namespace LAMGestRestaurant.Infrastructure.Impressao;

public enum EstiloTexto { Normal, Negrito, Grande }

/// <summary>Medição de texto do dispositivo (PDF ou impressora), em milímetros.</summary>
public interface IMedidorTexto
{
    double Largura(string texto, EstiloTexto estilo);
    double Altura(EstiloTexto estilo);
}

/// <summary>Cores dos relatórios em A4 (os talões saem sempre a preto).</summary>
public enum CorTexto { Preto, Cinzento, Branco, Azul, Vermelho }
/// <summary>Fundos: títulos das colunas, faixa da grelha (escura), linhas alternadas, subtotal, alerta (rosado) e marca de alerta (vermelha).</summary>
public enum TipoFundo { Cabecalho, Faixa, Zebra, Subtotal, Alerta, MarcaAlerta }

/// <summary>Cores (RGB) comuns ao PDF e à impressão directa; iguais às da impressão pelo navegador.</summary>
public static class Paleta
{
    public static (int R, int G, int B) Texto(CorTexto cor) => cor switch
    {
        CorTexto.Cinzento => (90, 90, 90),
        CorTexto.Branco => (255, 255, 255),
        CorTexto.Azul => (31, 58, 104),
        CorTexto.Vermelho => (166, 27, 27),
        _ => (0, 0, 0),
    };

    public static (int R, int G, int B) Fundo(TipoFundo tipo) => tipo switch
    {
        TipoFundo.Faixa => (31, 58, 104),
        TipoFundo.Zebra => (246, 248, 251),
        TipoFundo.Subtotal => (236, 241, 248),
        TipoFundo.Alerta => (253, 243, 243),
        TipoFundo.MarcaAlerta => (198, 40, 40),
        _ => (232, 237, 245),
    };

    public static readonly (int R, int G, int B) Forte = (31, 58, 104), Clara = (205, 212, 224), CaixaAlerta = (224, 163, 163);
}

public abstract record Operacao;
public sealed record OpTexto(string Texto, EstiloTexto Estilo, double X, double Y, double Largura, Alinhamento Alinhamento, bool Cinzento = false,
    CorTexto Cor = CorTexto.Preto) : Operacao
{
    public CorTexto CorFinal => Cinzento ? CorTexto.Cinzento : Cor;
}
/// <summary><see cref="Forte"/>: traço azul-escuro mais grosso (topo do documento, subtotais).</summary>
public sealed record OpLinha(double X1, double X2, double Y, bool Tracejada, bool Clara = false, bool Forte = false) : Operacao;
public sealed record OpFundo(double X, double Y, double Largura, double Altura, TipoFundo Tipo = TipoFundo.Cabecalho) : Operacao;
/// <summary>Contorno de um quadro (resumo ou total); <see cref="Alerta"/> a vermelho, <see cref="Forte"/> azul-escuro e grosso.</summary>
public sealed record OpCaixa(double X, double Y, double Largura, double Altura, bool Alerta = false, bool Forte = false) : Operacao;
public sealed record OpLogotipo(double X, double Y, double Largura, double Altura) : Operacao;
/// <summary>Código de barras: módulos (true = barra) com a largura indicada, a partir de (X, Y).</summary>
public sealed record OpBarras(double X, double Y, double LarguraModulo, double Altura, IReadOnlyList<bool> Modulos) : Operacao;
/// <summary>QR code: matriz de módulos quadrados (true = preto), a partir de (X, Y).</summary>
public sealed record OpMatriz(double X, double Y, double LadoModulo, IReadOnlyList<IReadOnlyList<bool>> Modulos) : Operacao;

/// <summary>Uma página já disposta: tamanho em mm e o que desenhar.</summary>
public sealed record PaginaDisposta(double Largura, double Altura, IReadOnlyList<Operacao> Operacoes);

/// <summary>
/// Disposição comum à impressão directa e ao PDF (as duas saem iguais), em milímetros.
/// A4: logótipo à esquerda e identificação do estabelecimento à direita; tabelas em colunas com títulos sombreados
/// (repetidos no topo de cada página); rodapé com o título e «Página N de M».
/// Talão (58/80 mm): uma só página com a altura do conteúdo; logótipo e identificação centrados; nas tabelas a descrição
/// ocupa uma linha e os valores vão em colunas por baixo.
/// </summary>
public static class Disposicao
{
    /// <summary>Larguras de papel suportadas (mm): talões de 58 e 80 mm, A4 a partir de 210.</summary>
    public const int Largura58 = 58, Largura80 = 80, LarguraA4 = 210;
    public const double MargemA4 = 15, MargemTalao = 2, AlturaA4 = 297;
    /// <summary>Margem dentro da área imprimível da impressora térmica: o texto fica encostado à esquerda.</summary>
    public const double MargemImpressora = 0.5;
    private const double EspacoLinha = 0.35, RodapeA4 = 9;

    public static double TamanhoFonte(int larguraPapelMm) =>
        larguraPapelMm >= LarguraA4 ? 10 : larguraPapelMm <= Largura58 ? 7.5 : 9;

    /// <param name="proporcaoLogotipo">Largura/altura do logótipo, ou null se não houver logótipo (ou for ilegível).</param>
    /// <param name="larguraImprimivelMm">Talões impressos: largura que a impressora consegue imprimir (ex.: 72 mm num rolo de 80 mm). O desenho começa
    /// no início dessa área, com uma margem mínima, em vez de usar a largura do papel com margens (que deixava o texto deslocado para a direita).</param>
    public static IReadOnlyList<PaginaDisposta> Dispor(DocumentoImpressao documento, int larguraPapelMm, double? proporcaoLogotipo, IMedidorTexto m,
        double? larguraImprimivelMm = null)
    {
        if (documento.Etiquetas is { } etiquetas)
            return DisporEtiquetas(etiquetas, documento.Formato ?? new FormatoEtiqueta(50, 30), m);
        var a4 = larguraPapelMm >= LarguraA4;
        var imprimivel = !a4 && larguraImprimivelMm is double li && li > 2 * MargemImpressora ? li : (double?)null;
        var largura = a4 ? 210 : imprimivel ?? larguraPapelMm;
        var margem = a4 ? MargemA4 : imprimivel is null ? MargemTalao : MargemImpressora;
        var util = largura - 2 * margem;
        var corpo = new Corpo(m, a4, margem, util);

        // ---- topo: logótipo + identificação do estabelecimento
        var topo = new List<Operacao>();
        double y = margem;
        var cabecalho = documento.Cabecalho ?? [];
        if (a4)
        {
            double alturaLogo = 0, larguraLogo = 0;
            if (proporcaoLogotipo is double p)
            {
                (larguraLogo, alturaLogo) = Ajustar(p, 55, 28);
                topo.Add(new OpLogotipo(margem, y, larguraLogo, alturaLogo));
            }
            // Com logótipo, a identificação fica à direita (alinhada à direita); sem ele, à esquerda.
            var xTexto = larguraLogo > 0 ? margem + larguraLogo + 6 : margem;
            var larguraTexto = margem + util - xTexto;
            var yTexto = y;
            foreach (var linha in cabecalho)
                yTexto = corpo.Paragrafo(topo, linha.Texto, Estilo(linha), xTexto, yTexto, larguraTexto, larguraLogo > 0 ? Alinhamento.Direita : Alinhamento.Esquerda);
            y = Math.Max(y + alturaLogo, yTexto);
            if (alturaLogo > 0 || cabecalho.Count > 0)
            {
                y += 2;
                topo.Add(new OpLinha(margem, margem + util, y, Tracejada: false, Forte: true));
                y += 4;
            }
        }
        else
        {
            if (proporcaoLogotipo is double p)
            {
                var (l, a) = Ajustar(p, util * 0.75, larguraPapelMm <= Largura58 ? 18 : 23);
                topo.Add(new OpLogotipo(margem + (util - l) / 2, y, l, a));
                y += a + 2;
            }
            foreach (var linha in cabecalho)
                y = corpo.Paragrafo(topo, linha.Texto, Estilo(linha), margem, y, util, Alinhamento.Centro);
            if (cabecalho.Count > 0)
            {
                y += 0.8;
                topo.Add(new OpLinha(margem, margem + util, y, Tracejada: true));
                y += 1.6;
            }
        }

        // ---- corpo: blocos com altura conhecida, para poder paginar (A4)
        var blocos = corpo.Blocos(documento.Linhas);
        if (!a4)
        {
            var ops = new List<Operacao>(topo);
            foreach (var b in blocos)
            {
                b.Desenhar(ops, y);
                y += b.Altura;
            }
            return [new PaginaDisposta(largura, y + margem + 4, ops)];
        }

        var paginas = new List<List<Operacao>> { topo };
        var limite = AlturaA4 - margem - RodapeA4;
        Bloco? tituloTabela = null;
        for (var k = 0; k < blocos.Count; k++)
        {
            var b = blocos[k];
            if (b.TituloTabela) tituloTabela = b;
            else if (!b.LinhaTabela) tituloTabela = null;
            // A faixa de uma grelha não fica sozinha no fim da página: vai com os títulos das colunas e a primeira linha.
            var junto = b.Faixa ? blocos.Skip(k + 1).Take(2).Sum(x => x.Altura) : 0;
            if ((b.QuebraPagina || y + b.Altura + junto > limite) && y > margem + 1)
            {
                paginas.Add([]);
                y = margem;
                // Tabela que continua na página seguinte: repete os títulos das colunas.
                if (b.LinhaTabela && tituloTabela is not null)
                {
                    tituloTabela.Desenhar(paginas[^1], y);
                    y += tituloTabela.Altura;
                }
            }
            b.Desenhar(paginas[^1], y);
            y += b.Altura;
        }
        var total = paginas.Count;
        return [.. paginas.Select((ops, i) =>
        {
            var yRodape = AlturaA4 - margem - RodapeA4 + 3;
            ops.Add(new OpLinha(margem, margem + util, yRodape, Tracejada: false, Clara: true));
            ops.Add(new OpTexto(documento.Titulo, EstiloTexto.Normal, margem, yRodape + 1, util * 0.7, Alinhamento.Esquerda, Cinzento: true));
            ops.Add(new OpTexto($"Página {i + 1} de {total}", EstiloTexto.Normal, margem, yRodape + 1, util, Alinhamento.Direita, Cinzento: true));
            return new PaginaDisposta(largura, AlturaA4, ops);
        })];
    }

    private static (double Largura, double Altura) Ajustar(double proporcao, double maxLargura, double maxAltura)
    {
        var l = Math.Min(maxLargura, maxAltura * proporcao);
        return (l, l / proporcao);
    }

    /// <summary>
    /// Etiquetas: uma página por etiqueta, com o tamanho configurado. Texto centrado em cima (cortado se não couber na largura);
    /// o código de barras ocupa o espaço que sobra, com o número por baixo. Margem branca de 10 módulos dos dois lados.
    /// </summary>
    private static IReadOnlyList<PaginaDisposta> DisporEtiquetas(IReadOnlyList<EtiquetaImpressao> etiquetas, FormatoEtiqueta formato, IMedidorTexto m)
    {
        const double Margem = 1.5, ModuloMaximo = 0.5, AlturaMinimaBarras = 4;
        var (largura, altura) = (formato.LarguraMm, formato.AlturaMm);
        var util = largura - 2 * Margem;
        var paginas = new List<PaginaDisposta>();
        foreach (var e in etiquetas)
        {
            var ops = new List<Operacao>();
            // QR code à esquerda (com a sua margem branca incluída); o resto da etiqueta fica à direita.
            var esquerda = Margem;
            var coluna = util;
            if (e.QrCode is { Length: > 0 } qr)
            {
                var matriz = Qr.Matriz(qr);
                var lado = Math.Min(altura - 2 * Margem, util * 0.5);
                ops.Add(new OpMatriz(Margem, (altura - lado) / 2, lado / matriz.Count, matriz));
                esquerda = Margem + lado + 1;
                coluna = util - lado - 1;
            }
            double y = Margem;
            foreach (var l in e.Linhas)
            {
                var estilo = Estilo(l);
                ops.Add(new OpTexto(Cortar(l.Texto, estilo, coluna, m), estilo, esquerda, y, coluna, Alinhamento.Centro));
                y += m.Altura(estilo) + 0.2;
            }
            if (e.CodigoBarras is { Length: > 0 } codigo && Code128.Valido(codigo))
            {
                var modulos = Code128.Modulos(codigo);
                var modulo = Math.Min(coluna / (modulos.Count + 20), ModuloMaximo);
                var numero = m.Altura(EstiloTexto.Normal);
                var alturaBarras = Math.Max(altura - Margem - numero - y - 0.5, AlturaMinimaBarras);
                var x = esquerda + (coluna - modulos.Count * modulo) / 2;
                ops.Add(new OpBarras(x, y + 0.5, modulo, alturaBarras, modulos));
                ops.Add(new OpTexto(codigo, EstiloTexto.Normal, esquerda, y + 0.5 + alturaBarras, coluna, Alinhamento.Centro));
            }
            paginas.Add(new PaginaDisposta(largura, altura, ops));
        }
        return paginas;
    }

    /// <summary>Texto que não cabe na largura: corta e acaba em «…».</summary>
    private static string Cortar(string texto, EstiloTexto estilo, double largura, IMedidorTexto m)
    {
        if (m.Largura(texto, estilo) <= largura)
            return texto;
        for (var n = texto.Length - 1; n > 0; n--)
        {
            var curto = texto[..n].TrimEnd() + "…";
            if (m.Largura(curto, estilo) <= largura)
                return curto;
        }
        return "…";
    }

    private static EstiloTexto Estilo(LinhaImpressao l) => l.Grande ? EstiloTexto.Grande : l.Negrito ? EstiloTexto.Negrito : EstiloTexto.Normal;

    /// <summary>Parte do documento com altura fixa, desenhada numa posição vertical.</summary>
    private sealed class Bloco(double altura, Action<List<Operacao>, double> desenhar, bool linhaTabela = false, bool tituloTabela = false, bool quebraPagina = false,
        bool faixa = false)
    {
        public double Altura { get; } = altura;
        public bool Faixa { get; } = faixa;
        public bool LinhaTabela { get; } = linhaTabela;
        public bool TituloTabela { get; } = tituloTabela;
        public bool QuebraPagina { get; } = quebraPagina;
        public void Desenhar(List<Operacao> ops, double y) => desenhar(ops, y);
    }

    private sealed class Corpo(IMedidorTexto m, bool a4, double margem, double util)
    {
        /// <summary>Em A4 qualquer linha com 2 ou mais células é uma grelha; nos talões só com 3 ou mais (2 células são «texto … valor»).</summary>
        private bool EhTabela(LinhaImpressao l) => l.Celulas is { } c && c.Count >= (a4 ? 2 : 3);

        public List<Bloco> Blocos(IReadOnlyList<LinhaImpressao> todas)
        {
            var linhas = todas.Where(l => !(l.SoTalao && a4) && !(l.SoA4 && !a4)).ToList();
            // Grelhas seguidas (linhas de tabela consecutivas).
            var grelhas = new List<(int Inicio, int Fim)>();
            for (var i = 0; i < linhas.Count; i++)
            {
                if (!EhTabela(linhas[i])) continue;
                var fim = i;
                while (fim + 1 < linhas.Count && EhTabela(linhas[fim + 1])) fim++;
                grelhas.Add((i, fim));
                i = fim;
            }
            // Em A4, grelhas com a mesma forma (n.º de colunas e de colunas de texto) têm as mesmas larguras em todo o documento.
            var larguras = new Dictionary<Forma, double[]>();
            if (a4)
                foreach (var g in grelhas.Select(g => linhas[g.Inicio..(g.Fim + 1)]).GroupBy(FormaDe))
                    larguras[g.Key] = LargurasA4([.. g.SelectMany(x => x)], g.Key);

            var blocos = new List<Bloco>();
            for (var i = 0; i < linhas.Count; i++)
            {
                var linha = linhas[i];
                if (linha.QuebraPagina)
                {
                    blocos.Add(new Bloco(0, (_, _) => { }, quebraPagina: true));
                    if (linha.Texto.Length == 0 && linha.Celulas is null) continue;
                }
                if (EhTabela(linha))
                {
                    var (_, fim) = grelhas.First(g => g.Inicio == i);
                    var grelha = linhas[i..(fim + 1)];
                    blocos.AddRange(a4 ? TabelaA4(grelha, larguras[FormaDe(grelha)]) : Tabela([.. grelha.Select(ParaTalao)]));
                    i = fim;
                }
                else if (linha.Celulas is { Count: 2 } duas)
                    blocos.Add(Linha(linha with { Texto = duas[0] + (linha.Alerta ? " (!)" : ""), Direita = duas[1], Celulas = null }, sublinhado: linha.Cabecalho));
                else if (linha.Separador)
                {
                    var altura = m.Altura(EstiloTexto.Normal) * 0.9;
                    blocos.Add(new Bloco(altura, (ops, y) => ops.Add(new OpLinha(margem, margem + util, y + altura / 2, Tracejada: !a4, Clara: a4))));
                }
                else if (linha.Cartao && a4)
                {
                    var fim = i;
                    while (fim + 1 < linhas.Count && linhas[fim + 1].Cartao) fim++;
                    blocos.Add(Cartoes(linhas[i..(fim + 1)]));
                    i = fim;
                }
                else if (linha.Faixa)
                    blocos.Add(a4 ? FaixaA4(linha) : Linha(linha with { Negrito = true }, sublinhado: true, espacoAntes: 1));
                else if (linha.Titulo)
                    blocos.Add(a4 ? TituloA4(linha) : TituloTalao(linha));
                else if (linha.Total && a4)
                    blocos.Add(TotalA4(linha));
                else
                    blocos.Add(!linha.Alerta ? Linha(linha) : a4 ? Linha(linha with { Negrito = true }, cor: CorTexto.Vermelho) : Linha(linha with { Texto = linha.Texto + " (!)", Negrito = true }));
            }
            return blocos;
        }

        private static string Celula(LinhaImpressao l, int c) => c < l.Celulas!.Count ? l.Celulas[c] : "";

        private sealed record Forma(int Colunas, int Texto);

        private static Forma FormaDe(IReadOnlyList<LinhaImpressao> grelha)
        {
            var colunas = grelha.Max(l => l.Celulas!.Count);
            return new(colunas, Math.Clamp(grelha.Max(l => l.ColunasTexto), 1, colunas));
        }

        /// <summary>
        /// Larguras das colunas em A4: valores com a largura do maior conteúdo (mín. 16 mm); colunas de texto também,
        /// até 28% da largura, excepto a de texto mais comprido, que fica com o resto.
        /// </summary>
        private double[] LargurasA4(IReadOnlyList<LinhaImpressao> linhas, Forma f)
        {
            const double Folga = 4;
            // O texto dos subtotais ocupa todas as colunas de texto: não conta para as larguras dessas colunas.
            var maximo = Enumerable.Range(0, f.Colunas)
                .Select(c => linhas.Where(l => c >= f.Texto || !l.Subtotal).Select(l => m.Largura(Celula(l, c), Estilo(l))).DefaultIfEmpty(0).Max() + Folga).ToArray();
            var flexivel = Enumerable.Range(0, f.Texto).MaxBy(c => maximo[c]);
            var larguras = Enumerable.Range(0, f.Colunas)
                .Select(c => c == flexivel ? 0 : c < f.Texto ? Math.Min(maximo[c], util * 0.28) : Math.Max(16, maximo[c])).ToArray();
            var fixas = larguras.Sum();
            if (fixas > util * 0.75)
            {
                for (var c = 0; c < larguras.Length; c++) larguras[c] *= util * 0.75 / fixas;
                fixas = util * 0.75;
            }
            larguras[flexivel] = util - fixas;
            return larguras;
        }

        /// <summary>
        /// Grelha em A4: títulos das colunas a azul sobre fundo claro, linhas alternadas, alertas a vermelho com marca à esquerda
        /// e subtotal com fundo e traço forte por cima. As colunas de texto quebram linha; os valores não.
        /// </summary>
        private IEnumerable<Bloco> TabelaA4(IReadOnlyList<LinhaImpressao> linhas, double[] larguras)
        {
            const double Recuo = 1.5, Vertical = 1.2;
            var f = FormaDe(linhas);
            var dados = 0;
            foreach (var l in linhas)
            {
                var estilo = Estilo(l);
                var h = m.Altura(estilo);
                // Subtotal: o texto junta-se e ocupa a largura de todas as colunas de texto.
                var juntar = l.Subtotal && f.Texto > 1;
                var larguraTexto = larguras.Take(f.Texto).Sum();
                var textos = Enumerable.Range(0, f.Colunas)
                    .Select(c => juntar && c < f.Texto
                        ? c == 0 ? Quebrar(string.Join(" ", Enumerable.Range(0, f.Texto).Select(k => Celula(l, k)).Where(x => x.Length > 0)), estilo, larguraTexto - 2 * Recuo) : []
                        : c < f.Texto ? Quebrar(Celula(l, c), estilo, larguras[c] - 2 * Recuo) : [Celula(l, c)]).ToList();
                var altura = h * textos.Max(t => t.Count) + 2 * Vertical;
                var zebra = !l.Cabecalho && !l.Subtotal && dados++ % 2 == 1;
                yield return new Bloco(altura, (ops, y) =>
                {
                    if (l.Cabecalho) ops.Add(new OpFundo(margem, y, util, altura));
                    else if (l.Subtotal) ops.Add(new OpFundo(margem, y, util, altura, TipoFundo.Subtotal));
                    else if (zebra) ops.Add(new OpFundo(margem, y, util, altura, TipoFundo.Zebra));
                    if (l.Alerta) ops.Add(new OpFundo(margem, y, 0.8, altura, TipoFundo.MarcaAlerta));
                    var cor = l.Cabecalho ? CorTexto.Azul : l.Alerta ? CorTexto.Vermelho : CorTexto.Preto;
                    var x = margem;
                    for (var c = 0; c < f.Colunas; c++)
                    {
                        var largura = juntar && c == 0 ? larguraTexto : larguras[c];
                        for (var k = 0; k < textos[c].Count; k++)
                            ops.Add(new OpTexto(textos[c][k], estilo, x + Recuo, y + Vertical + k * h, largura - 2 * Recuo,
                                c < f.Texto ? Alinhamento.Esquerda : Alinhamento.Direita, Cor: cor));
                        x += larguras[c];
                    }
                    if (l.Subtotal) ops.Add(new OpLinha(margem, margem + util, y, Tracejada: false, Forte: true));
                    ops.Add(new OpLinha(margem, margem + util, y + altura, Tracejada: false, Clara: true));
                }, linhaTabela: true, tituloTabela: l.Cabecalho);
            }
        }

        /// <summary>Talão: as colunas de texto juntam-se na descrição; o alerta fica a negrito com «(!)».</summary>
        private static LinhaImpressao ParaTalao(LinhaImpressao l)
        {
            var celulas = l.Celulas!;
            var texto = Math.Clamp(l.ColunasTexto, 1, celulas.Count);
            var descricao = string.Join(" · ", celulas.Take(texto).Where(c => c.Length > 0)) + (l.Alerta ? " (!)" : "");
            return l with { Texto = descricao, Celulas = [descricao, .. celulas.Skip(texto)], ColunasTexto = 1, Negrito = l.Negrito || l.Alerta };
        }

        /// <summary>Faixa escura no topo de uma grelha (A4): título à esquerda e resumo à direita, a branco.</summary>
        private Bloco FaixaA4(LinhaImpressao l)
        {
            const double Antes = 3, Vertical = 1.4;
            var h = m.Altura(EstiloTexto.Negrito);
            var altura = Antes + h + 2 * Vertical;
            return new Bloco(altura, (ops, y) =>
            {
                ops.Add(new OpFundo(margem, y + Antes, util, h + 2 * Vertical, TipoFundo.Faixa));
                ops.Add(new OpTexto(l.Texto.ToUpperInvariant(), EstiloTexto.Negrito, margem + 2, y + Antes + Vertical, util * 0.6, Alinhamento.Esquerda, Cor: CorTexto.Branco));
                if (l.Direita is not null)
                    ops.Add(new OpTexto(l.Direita, EstiloTexto.Normal, margem, y + Antes + Vertical, util - 2, Alinhamento.Direita, Cor: CorTexto.Branco));
            }, faixa: true);
        }

        /// <summary>Título do relatório em A4: grande e a azul à esquerda; o período à direita.</summary>
        private Bloco TituloA4(LinhaImpressao l)
        {
            var hg = m.Altura(EstiloTexto.Grande);
            var h = m.Altura(EstiloTexto.Negrito);
            var larguraDireita = l.Direita is null ? 0 : m.Largura(l.Direita, EstiloTexto.Negrito) + 4;
            var titulo = Quebrar(l.Texto, EstiloTexto.Grande, util - larguraDireita);
            var altura = hg * titulo.Count + 2;
            return new Bloco(altura, (ops, y) =>
            {
                for (var i = 0; i < titulo.Count; i++)
                    ops.Add(new OpTexto(titulo[i], EstiloTexto.Grande, margem, y + i * hg, util - larguraDireita, Alinhamento.Esquerda, Cor: CorTexto.Azul));
                if (l.Direita is not null)
                    ops.Add(new OpTexto(l.Direita, EstiloTexto.Negrito, margem, y + hg - h, util, Alinhamento.Direita));
            });
        }

        private Bloco TituloTalao(LinhaImpressao l)
        {
            var h = m.Altura(EstiloTexto.Negrito);
            var titulo = Quebrar(l.Texto, EstiloTexto.Negrito, util);
            var periodo = l.Direita is null ? [] : Quebrar(l.Direita, EstiloTexto.Normal, util);
            var altura = h * (titulo.Count + periodo.Count) + EspacoLinha;
            return new Bloco(altura, (ops, y) =>
            {
                foreach (var t in titulo) { ops.Add(new OpTexto(t, EstiloTexto.Negrito, margem, y, util, Alinhamento.Centro)); y += h; }
                foreach (var t in periodo) { ops.Add(new OpTexto(t, EstiloTexto.Normal, margem, y, util, Alinhamento.Centro)); y += h; }
            });
        }

        /// <summary>Quadros de resumo lado a lado (até 4 por fila): rótulo pequeno a cinzento e valor grande; os de alerta a vermelho.</summary>
        private Bloco Cartoes(IReadOnlyList<LinhaImpressao> cartoes)
        {
            const double Espaco = 3, Interior = 2.5, Depois = 4;
            var porFila = Math.Min(4, cartoes.Count);
            var largura = (util - Espaco * (porFila - 1)) / porFila;
            var h = m.Altura(EstiloTexto.Normal);
            var hg = m.Altura(EstiloTexto.Grande);
            var alturaCartao = h + hg + 2 * Interior + 0.8;
            var filas = (cartoes.Count + porFila - 1) / porFila;
            var altura = filas * alturaCartao + (filas - 1) * Espaco + Depois;
            return new Bloco(altura, (ops, y) =>
            {
                for (var i = 0; i < cartoes.Count; i++)
                {
                    var c = cartoes[i];
                    var x = margem + i % porFila * (largura + Espaco);
                    var yc = y + i / porFila * (alturaCartao + Espaco);
                    if (c.Alerta) ops.Add(new OpFundo(x, yc, largura, alturaCartao, TipoFundo.Alerta));
                    ops.Add(new OpCaixa(x, yc, largura, alturaCartao, Alerta: c.Alerta));
                    ops.Add(new OpTexto(Cortar(c.Texto.ToUpperInvariant(), EstiloTexto.Normal, largura - 2 * Interior, m), EstiloTexto.Normal,
                        x + Interior, yc + Interior, largura - 2 * Interior, Alinhamento.Esquerda, Cinzento: true));
                    ops.Add(new OpTexto(Cortar(c.Direita ?? "", EstiloTexto.Grande, largura - 2 * Interior, m), EstiloTexto.Grande,
                        x + Interior, yc + Interior + h + 0.8, largura - 2 * Interior, Alinhamento.Esquerda, Cor: c.Alerta ? CorTexto.Vermelho : CorTexto.Preto));
                }
            });
        }

        /// <summary>Total do documento em A4: quadro com contorno forte, rótulo à esquerda e valor grande a azul à direita.</summary>
        private Bloco TotalA4(LinhaImpressao l)
        {
            const double Antes = 2, Interior = 2.2, Lado = 4;
            var hg = m.Altura(EstiloTexto.Grande);
            var larguraValor = l.Direita is null ? 0 : m.Largura(l.Direita, EstiloTexto.Grande) + 4;
            var rotulo = Quebrar(l.Texto, EstiloTexto.Grande, util - 2 * Lado - larguraValor);
            var caixa = hg * rotulo.Count + 2 * Interior;
            return new Bloco(Antes + caixa + 1.5, (ops, y) =>
            {
                ops.Add(new OpCaixa(margem, y + Antes, util, caixa, Forte: true));
                for (var i = 0; i < rotulo.Count; i++)
                    ops.Add(new OpTexto(rotulo[i], EstiloTexto.Grande, margem + Lado, y + Antes + Interior + i * hg, util - 2 * Lado - larguraValor, Alinhamento.Esquerda));
                if (l.Direita is not null)
                    ops.Add(new OpTexto(l.Direita, EstiloTexto.Grande, margem, y + Antes + Interior, util - Lado, Alinhamento.Direita, Cor: CorTexto.Azul));
            });
        }

        /// <summary>Texto com valor à direita na mesma linha; se o valor não couber (58 mm), passa para a linha de baixo.</summary>
        private Bloco Linha(LinhaImpressao linha, bool sublinhado = false, double espacoAntes = 0, CorTexto cor = CorTexto.Preto)
        {
            var estilo = Estilo(linha);
            var h = m.Altura(estilo);
            var larguraDireita = linha.Direita is null ? 0 : m.Largura(linha.Direita, estilo) + 2;
            var abaixo = larguraDireita > util * 0.55;
            var texto = Quebrar(linha.Texto, estilo, Math.Max(util - (abaixo ? 0 : larguraDireita), 10));
            var altura = espacoAntes + h * (texto.Count + (abaixo ? 1 : 0)) + EspacoLinha + (sublinhado ? 0.8 : 0);
            return new Bloco(altura, (ops, y) =>
            {
                y += espacoAntes;
                for (var i = 0; i < texto.Count; i++)
                    ops.Add(new OpTexto(texto[i], estilo, margem, y + i * h, util - (abaixo ? 0 : larguraDireita), linha.Alinhamento, Cor: cor));
                if (linha.Direita is not null)
                    ops.Add(new OpTexto(linha.Direita, estilo, margem, abaixo ? y + texto.Count * h : y, util, Alinhamento.Direita, Cor: cor));
                if (sublinhado)
                    ops.Add(new OpLinha(margem, margem + util, y - espacoAntes + altura - 0.5, Tracejada: false));
            });
        }

        private IEnumerable<Bloco> Tabela(IReadOnlyList<LinhaImpressao> linhas)
        {
            var colunas = linhas.Max(l => l.Celulas!.Count);
            // Talão: a tabela fica numa linha por registo se a descrição e os valores couberem lado a lado; senão, empilha.
            var larguraValores = Enumerable.Range(1, colunas - 1).Sum(c => linhas.Max(l => m.Largura(Celula(l, c), Estilo(l))) + 2);
            var cabeNumaLinha = linhas.Max(l => m.Largura(Celula(l, 0), Estilo(l))) + larguraValores + 1 <= util;
            if (a4 || cabeNumaLinha)
            {
                // Colunas de valores com a largura do maior conteúdo; a descrição fica com o resto.
                var larguras = Enumerable.Range(1, colunas - 1)
                    .Select(c => Math.Max(a4 ? 16 : 0, linhas.Max(l => m.Largura(Celula(l, c), Estilo(l))) + (a4 ? 4 : 2))).ToArray();
                var larguraDescricao = Math.Max(util - larguras.Sum(), util * 0.3);
                foreach (var l in linhas)
                {
                    var estilo = Estilo(l);
                    var h = m.Altura(estilo);
                    var recuo = a4 ? 1 : 0;
                    var texto = Quebrar(Celula(l, 0), estilo, larguraDescricao - 2 * recuo);
                    var altura = h * texto.Count + (a4 ? 1.6 : EspacoLinha + (l.Cabecalho ? 0.8 : 0));
                    var topo = a4 ? 0.8 : 0;
                    yield return new Bloco(altura, (ops, y) =>
                    {
                        if (l.Cabecalho && a4) ops.Add(new OpFundo(margem, y, util, altura));
                        for (var i = 0; i < texto.Count; i++)
                            ops.Add(new OpTexto(texto[i], estilo, margem + recuo, y + topo + i * h, larguraDescricao - 2 * recuo, Alinhamento.Esquerda));
                        var x = margem + larguraDescricao;
                        for (var c = 1; c < colunas; c++)
                        {
                            ops.Add(new OpTexto(Celula(l, c), estilo, x, y + topo, larguras[c - 1] - recuo, Alinhamento.Direita));
                            x += larguras[c - 1];
                        }
                        if (a4 && !l.Cabecalho) ops.Add(new OpLinha(margem, margem + util, y + altura, Tracejada: false, Clara: true));
                        if (!a4 && l.Cabecalho) ops.Add(new OpLinha(margem, margem + util, y + altura - 0.5, Tracejada: false));
                    }, linhaTabela: true, tituloTabela: l.Cabecalho);
                }
                yield break;
            }

            // Talão: descrição numa linha (com quebra), valores em colunas iguais por baixo, alinhados à direita.
            var larguraValor = util / (colunas - 1);
            foreach (var l in linhas)
            {
                var estilo = Estilo(l);
                var h = m.Altura(estilo);
                var texto = Quebrar(Celula(l, 0), estilo, util);
                var altura = h * (texto.Count + 1) + EspacoLinha + (l.Cabecalho ? 0.8 : 0.3);
                yield return new Bloco(altura, (ops, y) =>
                {
                    for (var i = 0; i < texto.Count; i++)
                        ops.Add(new OpTexto(texto[i], estilo, margem, y + i * h, util, Alinhamento.Esquerda));
                    for (var c = 1; c < colunas; c++)
                        ops.Add(new OpTexto(Celula(l, c), estilo, margem + (c - 1) * larguraValor, y + texto.Count * h, larguraValor, Alinhamento.Direita));
                    if (l.Cabecalho) ops.Add(new OpLinha(margem, margem + util, y + altura - 0.5, Tracejada: false));
                }, linhaTabela: true, tituloTabela: l.Cabecalho);
            }
        }

        /// <summary>Parágrafo com quebra de linha; devolve o y seguinte.</summary>
        public double Paragrafo(List<Operacao> ops, string texto, EstiloTexto estilo, double x, double y, double largura, Alinhamento alinhamento)
        {
            var h = m.Altura(estilo);
            foreach (var parte in Quebrar(texto, estilo, largura))
            {
                ops.Add(new OpTexto(parte, estilo, x, y, largura, alinhamento));
                y += h;
            }
            return y + EspacoLinha;
        }

        /// <summary>Quebra por palavras (mantém os espaços iniciais, usados nos recuos «   Ref.»).</summary>
        public List<string> Quebrar(string texto, EstiloTexto estilo, double largura)
        {
            if (texto.Length == 0)
                return [""];
            var linhas = new List<string>();
            var recuo = texto[..(texto.Length - texto.TrimStart().Length)];
            var actual = recuo;
            foreach (var palavra in texto.TrimStart().Split(' '))
            {
                var tentativa = actual.Trim().Length == 0 ? recuo + palavra : actual + " " + palavra;
                if (m.Largura(tentativa, estilo) <= largura || actual.Trim().Length == 0)
                    actual = tentativa;
                else
                {
                    linhas.Add(actual);
                    actual = recuo + palavra;
                }
            }
            linhas.Add(actual);
            return linhas;
        }
    }
}
