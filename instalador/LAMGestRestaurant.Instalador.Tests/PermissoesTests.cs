using System.Security.AccessControl;
using System.Security.Principal;
using LAMGestRestaurant.Instalador.Motor;

namespace LAMGestRestaurant.Instalador.Tests;

/// <summary>Permissões da pasta do programa no servidor (numa pasta temporária; nunca na instalação real).</summary>
public class PermissoesTests
{
    private static readonly SecurityIdentifier Utilizadores = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier Autenticados = new(WellKnownSidType.AuthenticatedUserSid, null);
    private static readonly SecurityIdentifier Todos = new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier Administradores = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private const FileSystemRights Escrita = FileSystemRights.CreateFiles | FileSystemRights.AppendData | FileSystemRights.WriteData
        | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private static IEnumerable<FileSystemAccessRule> Regras(FileSystemSecurity acl) =>
        acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Where(r => r.AccessControlType == AccessControlType.Allow);

    private static bool UtilizadorComumAltera(FileSystemSecurity acl) =>
        Regras(acl).Any(r => (r.IdentityReference.Equals(Utilizadores) || r.IdentityReference.Equals(Autenticados) || r.IdentityReference.Equals(Todos))
                             && (r.FileSystemRights & Escrita) != 0);

    [Fact]
    public async Task Pasta_do_programa_deixa_de_herdar_a_raiz_e_so_administradores_alteram()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "lamgest-permissoes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(pasta, "wwwroot"));
        // Como na raiz do disco C:: os utilizadores podem criar ficheiros e pastas (herdado por tudo lá dentro).
        var inicial = new DirectoryInfo(pasta).GetAccessControl();
        inicial.AddAccessRule(new FileSystemAccessRule(Utilizadores, FileSystemRights.CreateFiles | FileSystemRights.AppendData,
            InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(pasta).SetAccessControl(inicial);
        File.WriteAllText(Path.Combine(pasta, "wwwroot", "index.html"), "<!doctype html>");
        try
        {
            Assert.True(UtilizadorComumAltera(new DirectoryInfo(Path.Combine(pasta, "wwwroot")).GetAccessControl()));

            InstalacaoServidor.ProtegerPastaPrograma(pasta);

            var acl = new DirectoryInfo(pasta).GetAccessControl();
            Assert.True(acl.AreAccessRulesProtected);
            Assert.False(UtilizadorComumAltera(acl));
            Assert.False(UtilizadorComumAltera(new DirectoryInfo(Path.Combine(pasta, "wwwroot")).GetAccessControl()));
            Assert.False(UtilizadorComumAltera(new FileInfo(Path.Combine(pasta, "wwwroot", "index.html")).GetAccessControl()));
            // Os utilizadores (e o serviço) continuam a ler e executar; os administradores alteram.
            Assert.Contains(Regras(acl), r => r.IdentityReference.Equals(Utilizadores) && r.FileSystemRights.HasFlag(FileSystemRights.ReadAndExecute));
            Assert.Contains(Regras(acl), r => r.IdentityReference.Equals(Administradores) && r.FileSystemRights.HasFlag(FileSystemRights.FullControl));
        }
        finally
        {
            // Sem elevação, o grupo Administradores não conta: o dono (este utilizador) devolve-se o acesso para apagar.
            await Processos.ExigirAsync("icacls", pasta, "/grant", $"*{WindowsIdentity.GetCurrent().User!.Value}:(OI)(CI)F", "/T", "/Q");
            Directory.Delete(pasta, recursive: true);
        }
    }
}
