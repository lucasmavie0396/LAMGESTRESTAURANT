using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LAMGestRestaurant.Licenciamento;

namespace LAMGest.GeradorLicencas;

/// <summary>Licença emitida (registo local do gerador, para consultar e renovar).</summary>
public sealed record LicencaEmitida(DadosLicenca Dados, string Texto, DateTime EmitidaEmUtc, string? Observacoes);

/// <summary>
/// Pasta de dados do gerador (por omissão %APPDATA%\LAMGest\Licencas):
/// - chave-privada.dat: chave privada ECDSA protegida pelo Windows (DPAPI) para este utilizador;
/// - registo.json: licenças emitidas.
/// A chave privada nunca sai em claro; a cópia de segurança é um PEM cifrado com palavra-passe (PKCS#8, AES-256).
/// Com <paramref name="publicaDoSistema"/> (a chave embutida no sistema), cada licença é verificada com ela antes de ser
/// entregue: uma chave errada neste computador nunca produz licenças que o sistema recusaria.
/// </summary>
public sealed class ArmazemLicencas(string pasta, string? publicaDoSistema = null)
{
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("LAMGest.Licencas.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string PastaPadrao => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LAMGest", "Licencas");

    public string Pasta { get; } = pasta;
    private string FicheiroChave => Path.Combine(Pasta, "chave-privada.dat");
    private string FicheiroRegisto => Path.Combine(Pasta, "registo.json");

    public bool TemChave => File.Exists(FicheiroChave);

    public ECDsa CarregarChave()
    {
        var pkcs8 = ProtectedData.Unprotect(File.ReadAllBytes(FicheiroChave), Entropia, DataProtectionScope.CurrentUser);
        try
        {
            var chave = ECDsa.Create();
            chave.ImportPkcs8PrivateKey(pkcs8, out _);
            return chave;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    /// <summary>Cria a chave (só se ainda não existir) e devolve a chave pública em base64.</summary>
    public string CriarChave()
    {
        if (TemChave)
            throw new InvalidOperationException("Já existe uma chave neste computador. Substituí-la invalidaria todas as licenças emitidas.");
        using var chave = ChavesLicenca.Nova();
        GuardarChave(chave);
        return ChavesLicenca.ExportarPublica(chave);
    }

    private void GuardarChave(ECDsa chave)
    {
        Directory.CreateDirectory(Pasta);
        var pkcs8 = chave.ExportPkcs8PrivateKey();
        try
        {
            File.WriteAllBytes(FicheiroChave, ProtectedData.Protect(pkcs8, Entropia, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    public string ChavePublica()
    {
        using var chave = CarregarChave();
        return ChavesLicenca.ExportarPublica(chave);
    }

    /// <summary>Cópia de segurança da chave privada, cifrada com a palavra-passe (para outro computador ou se este avariar).</summary>
    public void ExportarCopia(string ficheiro, string palavraPasse)
    {
        if (palavraPasse.Length < 10)
            throw new ArgumentException("A palavra-passe da cópia tem de ter pelo menos 10 caracteres.");
        using var chave = CarregarChave();
        var pem = chave.ExportEncryptedPkcs8PrivateKeyPem(palavraPasse, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
        File.WriteAllText(ficheiro, pem);
    }

    /// <summary>Repõe a chave a partir de uma cópia. Recusa substituir uma chave diferente da que já existe.</summary>
    public string ImportarCopia(string ficheiro, string palavraPasse)
    {
        using var chave = ECDsa.Create();
        try
        {
            chave.ImportFromEncryptedPem(File.ReadAllText(ficheiro), palavraPasse);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Palavra-passe errada ou ficheiro de cópia inválido.", ex);
        }
        var publica = ChavesLicenca.ExportarPublica(chave);
        if (TemChave && ChavePublica() != publica)
            throw new InvalidOperationException("Este computador já tem outra chave. Não é possível substituí-la por esta cópia.");
        GuardarChave(chave);
        return publica;
    }

    public IReadOnlyList<LicencaEmitida> Registo() =>
        File.Exists(FicheiroRegisto) ? JsonSerializer.Deserialize<List<LicencaEmitida>>(File.ReadAllText(FicheiroRegisto), Json) ?? [] : [];

    /// <summary>
    /// Emite uma licença nova (também para renovar: o mesmo NUIT com outra validade). Um NUIT é um estabelecimento:
    /// o nome comercial pode mudar, fica o da última emissão.
    /// </summary>
    public LicencaEmitida Emitir(string nome, string nuit, DateOnly validaAte, DateOnly hoje, string? observacoes = null)
    {
        nome = nome.Trim();
        nuit = nuit.Trim();
        if (nome.Length is < 2 or > 150)
            throw new ArgumentException("Indique o nome comercial (2 a 150 caracteres).");
        if (!FormatoLicenca.NuitValido(nuit))
            throw new ArgumentException("O NUIT tem 9 algarismos.");
        if (validaAte <= hoje)
            throw new ArgumentException("A data de validade tem de ser depois de hoje.");
        using var chave = CarregarChave();
        var dados = new DadosLicenca(Guid.NewGuid(), nome, nuit, hoje, validaAte);
        var licenca = new LicencaEmitida(dados, FormatoLicenca.Codificar(dados, chave), DateTime.UtcNow, string.IsNullOrWhiteSpace(observacoes) ? null : observacoes.Trim());
        if (!AceiteNoSistema(licenca))
            throw new InvalidOperationException("A chave deste computador não é a embutida no sistema LAMGest: o sistema recusaria esta licença. " +
                "Reponha a cópia de segurança da chave verdadeira (menu Chave) antes de emitir licenças.");
        var registo = Registo().ToList();
        registo.Add(licenca);
        Directory.CreateDirectory(Pasta);
        File.WriteAllText(FicheiroRegisto, JsonSerializer.Serialize(registo, Json));
        return licenca;
    }

    /// <summary>
    /// Apaga uma licença do registo deste gerador (ex.: emitida por engano ou com outra chave). Não a revoga num sistema onde
    /// já foi activada: só deixa de aparecer aqui.
    /// </summary>
    public void Apagar(Guid id)
    {
        var registo = Registo().ToList();
        if (registo.RemoveAll(l => l.Dados.Id == id) == 0)
            throw new InvalidOperationException("A licença já não está no registo.");
        File.WriteAllText(FicheiroRegisto, JsonSerializer.Serialize(registo, Json));
    }

    /// <summary>A licença verifica com a chave do sistema (sempre true sem chave do sistema, ex.: nos testes).</summary>
    public bool AceiteNoSistema(LicencaEmitida licenca)
    {
        if (publicaDoSistema is null)
            return true;
        using var sistema = ChavesLicenca.Publica(publicaDoSistema);
        return FormatoLicenca.Ler(licenca.Texto, sistema, out _) is not null;
    }

    /// <summary>Um estabelecimento por NUIT, com a última licença emitida.</summary>
    public IReadOnlyList<LicencaEmitida> Estabelecimentos() =>
        [.. Registo().GroupBy(l => l.Dados.Nuit).Select(g => g.OrderBy(l => l.EmitidaEmUtc).Last()).OrderBy(l => l.Dados.Nome, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Validade sugerida para renovar: mais um ano a partir do fim da licença actual (ou de hoje, se já expirou).</summary>
    public static DateOnly ValidadeRenovacao(DateOnly validaAteActual, DateOnly hoje) =>
        (validaAteActual > hoje ? validaAteActual : hoje).AddYears(1);
}
